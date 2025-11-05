namespace Matter.Core
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using static Matter.Core.CertificateAuthority;

    internal class CASEClient
    {
        private readonly Node _node;
        private readonly Fabric _fabric;
        private IPAddress _ipAddress;
        private ushort _port;

        private record TrafficKeys(byte[] I2R, byte[] R2I);

        public CASEClient(Node node, Fabric fabric, IPAddress ipAddress, ushort port)
        {
            _node = node;
            _fabric = fabric;
            _ipAddress = ipAddress;
            _port = port;
        }

        internal ISession EstablishSession()
        {
            // Certificate - Authenticated Session Establishment (CASE)
            var connection = new UdpConnection(_ipAddress, _port);
            connection.OpenConnection();

            Console.WriteLine("Starting SIGMA / CASE exchange...");
            if (!ExecuteSIGMA(connection, out ushort initiatorSessionId, out ushort peerSessionId, out TrafficKeys keys))
            {
                return null;
            }

            Console.WriteLine("Establishing secure session to device {0} at IP address {1}:{2}", _node.NodeId.ToString("X16"), _ipAddress, _port);
            SecureSession secureSession = new SecureSession(connection, initiatorSessionId, peerSessionId, keys.I2R, keys.R2I);

            MessageExchange secureExchange = secureSession.CreateExchange(_fabric.RootNodeId, _node.NodeId);

            MessageFrame completeCommissioningResult = secureExchange.SendCommand(0, 0x30, 4, 8).GetAwaiter().GetResult(); // CompleteCommissioning
            if (MessageFrame.IsStatusReport(completeCommissioningResult))
            {
                Console.WriteLine("Received error status report in response to CompleteCommissioning message, abandoning commissioning!");
                return null;
            }

            secureExchange.AcknowledgeMessageAsync(completeCommissioningResult.MessageCounter).GetAwaiter().GetResult();
            secureExchange.Close();

            Console.WriteLine("Commissioning of Matter Device {0} is complete.", _node.NodeId);

            return secureSession;
        }

        private bool ExecuteSIGMA(UdpConnection udpConnection, out ushort initiatorSessionId, out ushort peerSessionId, out TrafficKeys keys)
        {
            initiatorSessionId = BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(2));
            peerSessionId = 0;
            keys = null;

            UnsecureSession unsecureSession = new(udpConnection);
            unsecureSession.UseMRP = true;

            MessageExchange unsecureExchange = unsecureSession.CreateExchange(0, 0);

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
                    Console.WriteLine("ResponderRandom must be 32B, abandoning operational commissioning!");
                    return false;
                }

                if (responderEphPub65.Length != 65 || responderEphPub65[0] != 0x04)
                {
                    Console.WriteLine("ResponderEphPubKey must be 65B uncompressed, abandoning operational commissioning!");
                    return false;
                }

                // Compute shared secret Z = ECDH(initiator eph priv, responder eph pub)
                using var responderEph = _fabric.CA.ImportEcdhPublic(responderEphPub65);
                byte[] Z;
                using (ECDiffieHellman ecdh = ECDiffieHellman.Create(operationalKeyPair.ExportParameters(true)))
                {
                    Z = ecdh.DeriveRawSecretAgreement(responderEph.PublicKey);
                }

                // Decrypt and parse inner To Be Encrypted Data (SenderNOC, optional ICAC, Signature, optional ResumptionID)
                byte[] decryptedSigma2 = DecryptSigma2(Z, sigma1, responderRandom, responderEphPub65, encryptedSigma2);
                var sigma2tbe = new MatterTLV(decryptedSigma2);
                sigma2tbe.OpenStructure();
                byte[] responderNoc = sigma2tbe.GetOctetString(1);
                byte[] signature = sigma2tbe.GetOctetString(3);
                byte[] resumptionId = sigma2tbe.GetOctetString(4);

                // Verify ECDSA(signature) over Responder TBS with public key from the Responder NOC
                if (!_node.OperationalNOCAsTLV.AsSpan().SequenceEqual(responderNoc))
                {
                    Console.WriteLine("Responder Sigma2 certificate invalid, abandoning operational commissioning!");
                    return false;
                }

                // Re-Build Responder To Be Signed Data = { ResponderNOC, ResponderICAC?, ResponderPubKey, SenderPubKey }
                var sigma2tbs = new MatterTLV();
                sigma2tbs.AddStructure();
                sigma2tbs.AddOctetString(1, responderNoc);
                sigma2tbs.AddOctetString(3, responderEphPub65);
                sigma2tbs.AddOctetString(4, opsPubKey65);
                sigma2tbs.EndContainer();

                using var ecdsa = _node.SubjectPublicKey;
                if (!ecdsa.VerifyData(sigma2tbs.GetBytes(), signature, HashAlgorithmName.SHA256))
                {
                    Console.WriteLine("Responder Sigma2 signature invalid, abandoning operational commissioning!");
                    return false;
                }

                // Build Initiator (i.e. us!) To Be Signed Data = { SenderNOC, SenderICAC?, SenderPubKey, ResponderPubKey }
                X509Certificate2 operationalCertificate = _fabric.CA.SignCertRequest(new CertificateRequest($"CN={_fabric.RootNodeId:X16}", operationalKeyPair, HashAlgorithmName.SHA256), _fabric.RootNodeId, _fabric.FabricId);
                var sigma3tbs = new MatterTLV();
                sigma3tbs.AddStructure();
                sigma3tbs.AddOctetString(1, _fabric.CA.GenerateCertMessage(operationalCertificate));
                sigma3tbs.AddOctetString(3, opsPubKey65);
                sigma3tbs.AddOctetString(4, responderEphPub65);
                sigma3tbs.EndContainer();

                // TBEData = { SenderNOC, SenderICAC?, Signature, ResumptionID? }
                var sigma3tbe = new MatterTLV();
                sigma3tbe.AddStructure();
                sigma3tbe.AddOctetString(1, _fabric.CA.GenerateCertMessage(operationalCertificate));
                sigma3tbe.AddOctetString(3, operationalKeyPair.SignData(sigma3tbs.GetBytes(), HashAlgorithmName.SHA256));
                sigma3tbe.EndContainer();

                byte[] encryptedSigma3 = EncryptSigma3(Z, sigma1, sigma2, sigma3tbe, out byte[] mic);

                var sigma3 = new MatterTLV();
                sigma3.AddStructure();
                sigma3.AddOctetString(1, _fabric.CA.Concat(encryptedSigma3, mic));
                sigma3.EndContainer();
                var sigma3Resp = unsecureExchange.SendAndReceiveMessageAsync(sigma3, 0, 0x32).GetAwaiter().GetResult();
                if (!MessageFrame.IsStandaloneAck(sigma3Resp) && !MessageFrame.IsStatusReport(sigma3Resp))
                {
                    Console.WriteLine("Expected Standalone ACK or Status Report to Sigma3, abandoning operational commissioning!");
                    return false;
                }

                // check for empty status report (success)
                if (MessageFrame.IsStatusReport(sigma3Resp) && (sigma3Resp.MessagePayload.ApplicationPayload.GetBytes()[0] != 0))
                {
                    Console.WriteLine($"Received failure status report in response to SIGMA3 message, error code: {sigma3Resp.MessagePayload.ApplicationPayload.GetBytes()[0]}, abandoning operational commissioning!");
                    return false;
                }

                unsecureExchange.AcknowledgeMessageAsync(sigma3Resp.MessageCounter).GetAwaiter().GetResult();
                unsecureExchange.Close();

                // Derive final application session keys
                keys = DeriveCaseTrafficKeys(Z, sigma1, sigma2, sigma3);

                Console.WriteLine("SIGMA/CASE exchange completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SIGMA/CASE exchange failed: {ex.Message}");
                unsecureExchange.Close();
                return false;
            }

            return true;
        }

       private byte[] DecryptSigma2(byte[] Z, MatterTLV sigma1, byte[] responderRandom, byte[] responderEphPub65, byte[] encryptedSigma2)
        {
            // Derive S2K (HKDF-SHA256, info="Sigma2") and AES-CCM decrypt
            byte[] salt = _fabric.CA.SigmaSalt(SigmaSaltVariant.IpkConcat_TranscriptHash_S1, responderRandom: responderRandom, responderEphPub65: responderEphPub65, ipk16: _fabric.OperationalIPK, sigma1Payload: sigma1.GetBytes());
            byte[] s2k = _fabric.CA.KeyDerivationFunctionHMACSHA256(Z, salt, Encoding.ASCII.GetBytes("Sigma2"), 16);

            // The Sigma2 payload is an AEAD (AES-CCM) over the TBEData TLV.
            // The last 16 bytes is the CCM tag (classic 16‑byte MIC), and the rest is ciphertext.
            int ctLen = encryptedSigma2.Length - 16;
            var ct = new byte[ctLen];
            Buffer.BlockCopy(encryptedSigma2, 0, ct, 0, ctLen);

            var tag = new byte[16];
            Buffer.BlockCopy(encryptedSigma2, ctLen, tag, 0, 16);

            byte[] decrypted = new byte[ctLen];
            using (var aead = new AesCcm(s2k))
            {
                aead.Decrypt(Encoding.ASCII.GetBytes("NCASE_Sigma2N"), ct, tag, decrypted);
            }

            return decrypted;
        }

        private byte[] EncryptSigma3(byte[] Z, MatterTLV sigma1, MatterTLV sigma2, MatterTLV sigma3, out byte[] tag)
        {
            // Derive S3K (HKDF-SHA256, info="Sigma3") and AES-CCM encrypt
            byte[] salt = _fabric.CA.SigmaSalt(SigmaSaltVariant.IpkConcat_TranscriptHash_S1S2, ipk16: _fabric.OperationalIPK, sigma1Payload: sigma1.GetBytes(), sigma2Payload: sigma2.GetBytes());
            byte[] s3k = _fabric.CA.KeyDerivationFunctionHMACSHA256(Z, salt, Encoding.ASCII.GetBytes("Sigma3"), 16);

            // The Sigma3 payload is an AEAD (AES-CCM) over the TBEData TLV.
            // The last 16 bytes is the CCM tag (classic 16‑byte MIC), and the rest is ciphertext.
            byte[] encrypted = new byte[sigma3.GetBytes().Length];
            tag = new byte[16];
            using (var aead = new AesCcm(s3k))
            {
                aead.Encrypt(Encoding.ASCII.GetBytes("NCASE_Sigma3N"), sigma3.GetBytes(), encrypted, tag);
            }

            return encrypted;
        }

        /// <summary>
        /// Derive the two unidirectional AES-CCM(128) traffic keys and the 8-byte nonce prefix
        /// for a CASE secure session.
        /// Output:
        ///   - (I2R 16B, R2I 16B, NoncePrefix 8B)
        /// </summary>
        private TrafficKeys DeriveCaseTrafficKeys(byte[] Z, MatterTLV sigma1, MatterTLV sigma2, MatterTLV sigma3)
        {
            // Derive session keys (HKDF-SHA256, info="SessionKeys")
            byte[] salt = _fabric.CA.SigmaSalt(SigmaSaltVariant.IpkConcat_TranscriptHash_S1S2S3, ipk16: _fabric.OperationalIPK, sigma1Payload: sigma1.GetBytes(), sigma2Payload: sigma2.GetBytes(), sigma3Payload: sigma3.GetBytes());
            byte[] s2k = _fabric.CA.KeyDerivationFunctionHMACSHA256(Z, salt, Encoding.ASCII.GetBytes("SessionKeys"), 32);

            byte[] i2r = new byte[16];
            byte[] r2i = new byte[16];
            Buffer.BlockCopy(s2k, 0, i2r, 0, 16);
            Buffer.BlockCopy(s2k, 16, r2i, 0, 16);

            return new TrafficKeys(i2r, r2i);
        }
    }
}
