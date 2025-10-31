namespace Matter.Core
{
    using System;
    using System.Formats.Asn1;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using static Matter.Core.CertificateAuthority;

    internal class CASEClient
    {
        private readonly Node _node;
        private readonly Fabric _fabric;
        private readonly UdpConnection _connection;

        public CASEClient(Node node, Fabric fabric, UdpConnection connection)
        {
            _node = node;
            _fabric = fabric;
            _connection = connection;
        }

        internal ISession EstablishSession()
        {
            // Certificate - Authenticated Session Establishment (CASE)
            Console.WriteLine("UDP connection established. Starting SPAKE/CASE exchange...");
            if (!ExecuteSIGMA(_connection, out ushort initiatorSessionId, out ushort peerSessionId, out CertificateAuthority.TrafficKeys keys))
            {
                return null;
            }

            var (i2rKey, r2iKey, noncePrefix) = keys;
            return new SecureSession(_connection, initiatorSessionId, peerSessionId, i2rKey, r2iKey);
        }

        private bool ExecuteSIGMA(UdpConnection udpConnection, out ushort initiatorSessionId, out ushort peerSessionId, out CertificateAuthority.TrafficKeys keys)
        {
            initiatorSessionId = BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(2));
            peerSessionId = 0;
            keys = null;

            UnsecureSession unsecureSession = new(udpConnection);
            MessageExchange unsecureExchange = unsecureSession.CreateExchange();

            try
            {
                ECDsa operationalKeyPair = ECDsa.Create(ECCurve.NamedCurves.nistP256);

                byte[] initiatorRandom = RandomNumberGenerator.GetBytes(32);
                byte[] rcacPubKey65 = _fabric.CA.GenerateUncompressed65ByteKey(_fabric.CA.RootKeyPair);
                byte[] destinationId = _fabric.CA.GenerateDestinationId(_fabric.OperationalIPK, initiatorRandom, rcacPubKey65, _fabric.FabricId, _node.NodeId);
                byte[] opsPubKey65 = _fabric.CA.GenerateUncompressed65ByteKey(operationalKeyPair);

                MatterTLV sigma1 = new();
                sigma1.AddStructure();
                sigma1.AddOctetString(1, initiatorRandom);
                sigma1.AddUInt16(2, initiatorSessionId);
                sigma1.AddOctetString(3, destinationId);
                sigma1.AddOctetString(4, opsPubKey65);
                sigma1.EndContainer();
                MessageFrame sigma2MessageFrame = unsecureExchange.SendAndReceiveMessageAsync(sigma1, 0, 0x30).GetAwaiter().GetResult();
                if (MessageFrame.IsStatusReport(sigma2MessageFrame))
                {
                    Console.WriteLine("Received error status report in response to SIGMA1 message, abandoning operational commissioning!");
                    return false;
                }

                var sigma2 = sigma2MessageFrame.MessagePayload.ApplicationPayload;
                sigma2.OpenStructure();
                byte[] responderRandom = sigma2.GetOctetString(1);
                peerSessionId = sigma2.GetUnsignedInt16(2);
                byte[] responderEphPub65 = sigma2.GetOctetString(3);
                byte[] encryptedSigma2 = sigma2.GetOctetString(4);

                if (responderRandom.Length != 32)
                {
                    throw new Exception("ResponderRandom must be 32B");
                }

                if (responderEphPub65.Length != 65 || responderEphPub65[0] != 0x04)
                {
                    throw new Exception("ResponderEphPubKey must be 65B uncompressed");
                }

                // Compute shared secret Z = ECDH(initiator eph priv, responder eph pub)
                using var responderEph = _fabric.CA.ImportEcdhPublic(responderEphPub65);
                byte[] Z;
                using (ECDiffieHellman ecdh = ECDiffieHellman.Create(operationalKeyPair.ExportParameters(true)))
                {
                    Z = ecdh.DeriveKeyFromHash(responderEph.PublicKey, HashAlgorithmName.SHA256);
                }

                // Decrypt and parse inner To Be Encrypted Data (SenderNOC, optional ICAC, Signature, optional ResumptionID)
                byte[] decryptedSigma2 = DecryptSigma2(Z, sigma2, initiatorRandom, responderRandom, responderEphPub65, initiatorSessionId, peerSessionId, encryptedSigma2);
                var sigma2tbe = new MatterTLV(decryptedSigma2);
                byte[] responderNoc = sigma2tbe.GetOctetString(1) ?? throw new Exception("Responder NOC missing");
                byte[] responderICAC = sigma2tbe.GetOctetString(2);
                byte[] signature = sigma2tbe.GetOctetString(3) ?? throw new Exception("Signature missing");
                byte[] resumptionId = sigma2tbe.GetOctetString(4);

                // Re-Build Responder To Be Signed Data = { ResponderNOC, ResponderICAC?, ResponderPubKey, SenderPubKey }
                var sigma2tbs = new MatterTLV();
                sigma2tbs.AddStructure();
                sigma2tbs.AddOctetString(1, responderNoc);
                sigma2tbs.AddOctetString(3, responderEphPub65);
                sigma2tbs.AddOctetString(4, opsPubKey65);
                sigma2tbs.EndContainer();

                // Verify ECDSA(signature) over Responder TBS with public key from the Responder NOC
                if (!_node.OperationalNOCAsTLV.AsSpan().SequenceEqual(responderNoc))
                {
                    Console.WriteLine("Responder Sigma2 certificate invalid!");
                    return false;
                }

                using var ecdsa = _node.SubjectPublicKey;
                if (!ecdsa.VerifyData(sigma2tbs.GetBytes(), signature, HashAlgorithmName.SHA256))
                {
                    throw new CryptographicException("Responder Sigma2 signature invalid");
                }

                // Build Initiator (i.e. us!) To Be Signed Data = { SenderNOC, SenderICAC?, SenderPubKey, ReceiverPubKey }
                X509Certificate2 operationalCertificate = _fabric.CA.SignCertRequest(new CertificateRequest($"CN={_fabric.RootNodeId:X16}", operationalKeyPair, HashAlgorithmName.SHA256), _fabric.RootNodeId, _fabric.FabricId);
                var sigma3tbs = new MatterTLV();
                sigma3tbs.AddStructure();
                sigma3tbs.AddOctetString(1, _fabric.CA.GenerateCertMessage(operationalCertificate));
                sigma3tbs.AddOctetString(3, opsPubKey65);
                sigma3tbs.AddOctetString(4, responderEphPub65);
                sigma3tbs.EndContainer();

                // Sign TBS with your operational ECDSA private key
                byte[] tbsSignature = operationalKeyPair.SignData(sigma3tbs.GetBytes(), HashAlgorithmName.SHA256);

                AsnDecoder.ReadSequence(tbsSignature.AsSpan(), AsnEncodingRules.DER, out int offset, out int length, out _);
                var source = tbsSignature.AsSpan().Slice(offset, length).ToArray();
                var r = AsnDecoder.ReadInteger(source, AsnEncodingRules.DER, out int bytesConsumed);
                var s = AsnDecoder.ReadInteger(source.AsSpan().Slice(bytesConsumed), AsnEncodingRules.DER, out bytesConsumed);
                var encodedSigma3TbsSignature = r.ToByteArray(isUnsigned: true, isBigEndian: true).Concat(s.ToByteArray(isUnsigned: true, isBigEndian: true)).ToArray();

                // TBEData = { SenderNOC, SenderICAC?, Signature, ResumptionID? }
                var sigma3tbe = new MatterTLV();
                sigma3tbe.AddStructure();
                sigma3tbe.AddOctetString(1, _fabric.CA.GenerateCertMessage(operationalCertificate));
                sigma3tbe.AddOctetString(3, tbsSignature);
                sigma3tbe.EndContainer();

                // Reuse the same salt strategy we used for S2K (e.g., concat nonces)
                // We try (IPK || SHA256(Sigma1 || Sigma2)) first, then (init||resp), then (resp||init)
                byte[] saltA = _fabric.CA.SigmaSalt(SigmaSaltVariant.IpkConcat_TranscriptHash_S1S2, ipk16: _fabric.IPK, sigma1Payload: sigma1.GetBytes(), sigma2Payload: sigma2.GetBytes());
                byte[] saltB = _fabric.CA.SigmaSalt(SigmaSaltVariant.RandomsConcat_InitThenResp, initiatorRandom, responderRandom);
                byte[] saltC = _fabric.CA.SigmaSalt(SigmaSaltVariant.RandomsConcat_RespThenInit, initiatorRandom, responderRandom);
                foreach (var salt in new[] { saltA, saltB, saltC })
                {
                    // Derive S3K (HKDF-SHA256, info="Sigma3") and AES-CCM encrypt TBEData
                    byte[] s3k = _fabric.CA.HkdfExpand(_fabric.CA.HkdfExtract(salt, Z), Encoding.ASCII.GetBytes("NCASE_Sigma3N"), 16);
                    byte[] sigma3Nonce = _fabric.CA.BuildSigmaNonce(salt, initiatorSessionId, peerSessionId, "NCASE_Sigma3N", Z);

                    try
                    {
                        byte[] encryptedSigma3TBE = new byte[sigma3tbe.GetBytes().Length];
                        byte[] mic = new byte[16];
                        using (var aead = new AesCcm(s3k))
                        {
                            aead.Encrypt(sigma3Nonce, sigma3tbe.GetBytes(), encryptedSigma3TBE, mic);
                        }

                        var sigma3 = new MatterTLV();
                        sigma3.AddStructure();
                        sigma3.AddOctetString(1, _fabric.CA.Concat(encryptedSigma3TBE, mic));
                        sigma3.EndContainer();
                        var sigma3Resp = unsecureExchange.SendAndReceiveMessageAsync(sigma3, 0, 0x32).GetAwaiter().GetResult();
                        if (!MessageFrame.IsStandaloneAck(sigma3Resp))
                        {
                            throw new Exception("Expected Standalone ACK to Sigma3");
                        }
                    }
                    catch (Exception)
                    {
                        // try next salt candidate
                        continue;
                    }
                }

                unsecureExchange.Close();

                // Derive final application session keys and install a CASE SecureSession
                // (two directions + nonce prefix), then switch to secure transport.
                var (i2rKey, r2iKey, noncePrefix) = _fabric.CA.DeriveCaseTrafficKeys(Z, initiatorRandom, responderRandom, initiatorSessionId, peerSessionId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SIGMA/CASE exchange failed: {ex.Message}");
                unsecureExchange.Close();
                return false;
            }

            return true;
        }

        private byte[] DecryptSigma2(
            byte[] Z,
            MatterTLV sigma1,
            byte[] initiatorRandom,
            byte[] responderRandom,
            byte[] responderEphPub65,
            ushort initiatorSessionId,
            ushort responderSessionId,
            byte[] encrypted2)
        {
            // HKDF-Extract with salt = concat of the two 32B nonces; order is implementation-defined.
            // We try (IPK || respRand || respPub || SHA256(Sigma1)) first, then (initRand||respRand), then (respRand||initRand)
            byte[] saltA = _fabric.CA.SigmaSalt(SigmaSaltVariant.IpkConcat_TranscriptHash_S1, responderRandom: responderRandom, responderEphPub65: responderEphPub65, ipk16: _fabric.IPK, sigma1Payload: sigma1.GetBytes());
            byte[] saltB = _fabric.CA.SigmaSalt(SigmaSaltVariant.RandomsConcat_InitThenResp, initiatorRandom, responderRandom);
            byte[] saltC = _fabric.CA.SigmaSalt(SigmaSaltVariant.RandomsConcat_RespThenInit, initiatorRandom, responderRandom);

            foreach (var salt in new[] { saltA, saltB, saltC })
            {
                byte[] s2k = _fabric.CA.HkdfExpand(_fabric.CA.HkdfExtract(salt, Z), Encoding.ASCII.GetBytes("NCASE_Sigma2N"), 16);
                byte[] sigma2Nonce = _fabric.CA.BuildSigmaNonce(salt, initiatorSessionId, responderSessionId, "NCASE_Sigma2N", Z);

                // The Sigma2 payload is an AEAD (AES-CCM) over the TBEData TLV.
                try
                {
                    // Assume the last 16 bytes is the CCM tag (classic 16‑byte MIC), and the rest is ciphertext.
                    const int tagLen = 16;
                    if (encrypted2.Length < tagLen)
                    {
                        break;
                    }

                    int ctLen = encrypted2.Length - tagLen;
                    var ct = new byte[ctLen];
                    var tag = new byte[tagLen];
                    byte[] decrypted = new byte[ctLen];

                    Buffer.BlockCopy(encrypted2, 0, ct, 0, ctLen);
                    Buffer.BlockCopy(encrypted2, ctLen, tag, 0, tagLen);

                    using (var aead = new AesCcm(s2k))
                    {
                        aead.Decrypt(sigma2Nonce, ct, tag, decrypted);
                    }

                    return decrypted;
                }
                catch (Exception)
                {
                    // try next salt candidate
                    continue;
                }

            }

            throw new CryptographicException("Sigma2 AES-CCM decrypt failed with both salt orders");
        }
    }
}
