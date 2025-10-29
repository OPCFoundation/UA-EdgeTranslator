namespace Matter.Core
{
    using System;
    using System.Formats.Asn1;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;

    internal class CASEClient
    {
        private readonly Node _node;
        private readonly UnsecureSession _unsecureSession;
        private readonly Fabric _fabric;
        private readonly UdpConnection _connection;

        public CASEClient(Node node, UnsecureSession unsecureSession, Fabric fabric, UdpConnection connection)
        {
            _node = node;
            _unsecureSession = unsecureSession;
            _fabric = fabric;
            _connection = connection;
        }

        internal ISession EstablishSession()
        {
            // Certificate - Authenticated Session Establishment (CASE)
            var caseExchange = _unsecureSession.CreateExchange();

            Console.WriteLine("UDP connection established. Starting SPAKE/CASE exchange...");
            if (!ExecuteSIGMA(_connection, out ushort initiatorSessionId, out ushort peerSessionId, out byte[] Z))
            {
                return null;
            }

            // HKDF with SHA-256
            using var hmac = new HMACSHA256(Array.Empty<byte>());
            byte[] prk = hmac.ComputeHash(Z);

            // Expand to get session key (32 bytes)
            byte[] info = Encoding.ASCII.GetBytes("SessionKeys");
            byte[] t = new byte[info.Length + 1];
            Buffer.BlockCopy(info, 0, t, 0, info.Length);
            t[info.Length] = 0x01;
            byte[] keys = new HMACSHA256(prk).ComputeHash(t);
            var encryptKey = keys.AsSpan().Slice(0, 16).ToArray();
            var decryptKey = keys.AsSpan().Slice(16, 16).ToArray();

            var caseSession = new SecureSession(_connection, initiatorSessionId, peerSessionId, encryptKey, decryptKey);
            caseSession.UseMRP = true;

            MessageExchange secureExchange = caseSession.CreateExchange();

            MessageFrame completeCommissioningResult = secureExchange.SendCommand(0, 0x30, 4, 8).GetAwaiter().GetResult(); // CompleteCommissioning
            if (MessageFrame.IsStatusReport(completeCommissioningResult))
            {
                Console.WriteLine("Received error status report in response to CompleteCommissioning message, abandoning commissioning!");
                return null;
            }

            secureExchange.AcknowledgeMessageAsync(completeCommissioningResult.MessageCounter).GetAwaiter().GetResult();
            secureExchange.Close();

            Console.WriteLine("Commissioning of Matter Device {0} is complete.", _node.NodeId);

            return caseSession;
        }

        private bool ExecuteSIGMA(UdpConnection udpConnection, out ushort initiatorSessionId, out ushort peerSessionId, out byte[] Z)
        {
            initiatorSessionId = BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(2));
            peerSessionId = 0;
            Z = Array.Empty<byte>();

            UnsecureSession unsecureSession = new(udpConnection);
            MessageExchange unsecureExchange = unsecureSession.CreateExchange();

            // Extract the public key (EC P-256)
            ECParameters pubParams = _fabric.CA.OperationalKeyPair.ExportParameters(false);

            // Convert EC point to uncompressed format: 0x04 || X || Y
            byte[] initiatorPubKey = new byte[1 + pubParams.Q.X.Length + pubParams.Q.Y.Length];
            initiatorPubKey[0] = 0x04;
            Buffer.BlockCopy(pubParams.Q.X, 0, initiatorPubKey, 1, pubParams.Q.X.Length);
            Buffer.BlockCopy(pubParams.Q.Y, 0, initiatorPubKey, 1 + pubParams.Q.X.Length, pubParams.Q.Y.Length);

            byte[] nonce = RandomNumberGenerator.GetBytes(32);

            using MemoryStream ms = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(ms);
            writer.Write(initiatorSessionId);
            writer.Write(_fabric.CA.RootCertificate.GetPublicKey());
            writer.Write(BitConverter.GetBytes(_fabric.FabricId));
            writer.Write(BitConverter.GetBytes(_node.NodeId));
            var destinationId = ms.ToArray();
            var hmac = new HMACSHA256(_fabric.OperationalIPK);
            byte[] hashedDestinationId = hmac.ComputeHash(destinationId);

            MatterTLV sigma1 = new();
            sigma1.AddStructure();
            sigma1.AddOctetString(1, nonce);
            sigma1.AddUInt16(2, initiatorSessionId);
            sigma1.AddOctetString(3, hashedDestinationId);
            sigma1.AddOctetString(4, initiatorPubKey);
            sigma1.EndContainer();
            MessageFrame sigma2MessageFrame = unsecureExchange.SendAndReceiveMessageAsync(sigma1, 0, 0x30).GetAwaiter().GetResult();
            if (MessageFrame.IsStatusReport(sigma2MessageFrame))
            {
                Console.WriteLine("Received error status report in response to SIGMA1 message, abandoning commissioning!");
                return false;
            }

            var sigma2Payload = sigma2MessageFrame.MessagePayload.ApplicationPayload;
            sigma2Payload.OpenStructure();
            var sigma2ResponderRandom = sigma2Payload.GetOctetString(1);
            var sigma2ResponderSessionId = sigma2Payload.GetUnsignedInt16(2);
            var sigma2ResponderEphPublicKey = sigma2Payload.GetOctetString(3);
            var sigma2EncryptedPayload = sigma2Payload.GetOctetString(4);

            //var sigmaKeyAgreement = AgreementUtilities.GetBasicAgreement("ECDH");
            //sigmaKeyAgreement.Init(ephermeralPrivateKey);
            //var curve = ECNamedCurveTable.GetByName("P-256");
            //var ecPoint = curve.Curve.DecodePoint(sigma2ResponderEphPublicKey);
            //var ephPublicKey = new ECPublicKeyParameters(ecPoint, new ECDomainParameters(curve));
            //var sharedSecretResult = sigmaKeyAgreement.CalculateAgreement(ephPublicKey);
            //var sharedSecret = sharedSecretResult.ToByteArrayUnsigned();
            //var transcriptHash = SHA256.HashData(sigma1Payload.GetBytes());

            //ms = new MemoryStream();
            //BinaryWriter saltWriter = new BinaryWriter(ms);
            //saltWriter.Write(_fabric.OperationalIPK);
            //saltWriter.Write(sigma2ResponderRandom);
            //saltWriter.Write(sigma2ResponderEphPublicKey);
            //saltWriter.Write(transcriptHash);
            //var salt = ms.ToArray();

            //var info = Encoding.ASCII.GetBytes("Sigma2");
            //var hkdf = new HkdfBytesGenerator(new Sha256Digest());
            //hkdf.Init(new HkdfParameters(sharedSecret, salt, info));
            //var sigma2Key = new byte[16];
            //hkdf.GenerateBytes(sigma2Key, 0, 16);
            //var nonce = Encoding.ASCII.GetBytes("NCASE_Sigma2N");
            //IBlockCipher cipher = new AesEngine();
            //int macSize = 8 * cipher.GetBlockSize();
            //AeadParameters keyParamAead = new AeadParameters(new KeyParameter(sigma2Key), macSize, nonce);
            //CcmBlockCipher cipherMode = new CcmBlockCipher(cipher);
            //cipherMode.Init(false, keyParamAead);
            //var outputSize = cipherMode.GetOutputSize(sigma2EncryptedPayload.Length);
            //var plainTextData = new byte[outputSize];
            //var result = cipherMode.ProcessBytes(sigma2EncryptedPayload, 0, sigma2EncryptedPayload.Length, plainTextData, 0);
            //cipherMode.DoFinal(plainTextData, result);
            //var TBEData2 = new MatterTLV(plainTextData);
            //var nocSignature = _fabric.OperationalCertificate.GetSignature();

            //AsnDecoder.ReadSequence(nocSignature.AsSpan(), AsnEncodingRules.DER, out var offset, out var length, out _);
            //var source = nocSignature.AsSpan().Slice(offset, length).ToArray();
            //var r = AsnDecoder.ReadInteger(source, AsnEncodingRules.DER, out var bytesConsumed);
            //var s = AsnDecoder.ReadInteger(source.AsSpan().Slice(bytesConsumed), AsnEncodingRules.DER, out bytesConsumed);
            //var encodedNocCertificateSignature = r.ToByteArray(isUnsigned: true, isBigEndian: true).Concat(s.ToByteArray(isUnsigned: true, isBigEndian: true)).ToArray();

            //var sigma3tbs = new MatterTLV();
            //sigma3tbs.AddStructure();
            //sigma3tbs.AddOctetString(1, _fabric.CA.GenerateCertMessage(_fabric.OperationalCertificate)); // initiatorNOC
            //sigma3tbs.AddOctetString(3, initiatorPubKey); // initiatorEphPubKey
            //sigma3tbs.AddOctetString(4, sigma2ResponderEphPublicKey); // responderEphPubKey
            //sigma3tbs.EndContainer();

            //var sigma3tbsBytes = sigma3tbs.GetBytes();
            //var signer = SignerUtilities.GetSigner("SHA256WITHECDSA");
            //signer.Init(true, _fabric.OperationalCertificateKeyPair.Private as ECPrivateKeyParameters);
            //signer.BlockUpdate(sigma3tbsBytes, 0, sigma3tbsBytes.Length);
            //byte[] sigma3tbsSignature = signer.GenerateSignature();
            //AsnDecoder.ReadSequence(sigma3tbsSignature.AsSpan(), AsnEncodingRules.DER, out offset, out length, out _);
            //source = sigma3tbsSignature.AsSpan().Slice(offset, length).ToArray();
            //r = AsnDecoder.ReadInteger(source, AsnEncodingRules.DER, out bytesConsumed);
            //s = AsnDecoder.ReadInteger(source.AsSpan().Slice(bytesConsumed), AsnEncodingRules.DER, out bytesConsumed);
            //var encodedSigma3TbsSignature = r.ToByteArray(isUnsigned: true, isBigEndian: true).Concat(s.ToByteArray(isUnsigned: true, isBigEndian: true)).ToArray();

            //var sigma3tbe = new MatterTLV();
            //sigma3tbe.AddStructure();
            //sigma3tbe.AddOctetString(1, encodedNocCertificate.GetBytes());
            //sigma3tbe.AddOctetString(3, encodedSigma3TbsSignature);
            //sigma3tbe.EndContainer();
            //var sigma3tbeTranscriptHash = SHA256.HashData(sigma1Payload.GetBytes().Concat(sigma2Payload.GetBytes()).ToArray());
            //ms = new MemoryStream();
            //saltWriter = new BinaryWriter(ms);
            //saltWriter.Write(_fabric.OperationalIPK);
            //saltWriter.Write(sigma3tbeTranscriptHash);
            //salt = ms.ToArray();

            //info = Encoding.ASCII.GetBytes("Sigma3");
            //hkdf = new HkdfBytesGenerator(new Sha256Digest());
            //hkdf.Init(new HkdfParameters(sharedSecret, salt, info));
            //var sigma3Key = new byte[16];
            //hkdf.GenerateBytes(sigma3Key, 0, 16);
            //nonce = Encoding.ASCII.GetBytes("NCASE_Sigma3N");
            //keyParamAead = new AeadParameters(new KeyParameter(sigma3Key), macSize, nonce);
            //cipherMode = new CcmBlockCipher(cipher);
            //cipherMode.Init(true, keyParamAead);
            //var sigma3tbeBytes = sigma3tbe.GetBytes();
            //outputSize = cipherMode.GetOutputSize(sigma3tbeBytes.Length);
            //var encryptedData = new byte[outputSize];
            //result = cipherMode.ProcessBytes(sigma3tbeBytes, 0, sigma3tbeBytes.Length, encryptedData, 0);
            //cipherMode.DoFinal(encryptedData, result);
            //var sigma3Payload = new MatterTLV();
            //sigma3Payload.AddStructure();
            //sigma3Payload.AddOctetString(1, encryptedData); // sigma3EncryptedPayload
            //sigma3Payload.EndContainer();

            //var sigma3MessagePayload = new MessagePayload(sigma3Payload);
            //sigma3MessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
            //sigma3MessagePayload.ProtocolId = 0x00;
            //sigma3MessagePayload.ProtocolOpCode = 0x32; // Sigma3
            //var sigma3MessageFrame = new MessageFrame(sigma3MessagePayload);
            //sigma3MessageFrame.MessageFlags |= MessageFlags.S;
            //sigma3MessageFrame.SecurityFlags = 0x00;
            //sigma3MessageFrame.SourceNodeID = 0x00;

            //await caseExchange.SendAsync(sigma3MessageFrame);
            //var successMessageFrame = await caseExchange.WaitForNextMessageAsync();
            //await caseExchange.AcknowledgeMessageAsync(successMessageFrame.MessageCounter);

            //byte[] caseInfo = Encoding.ASCII.GetBytes("SessionKeys");
            //ms = new MemoryStream();
            //var transcriptWriter = new BinaryWriter(ms);
            //transcriptWriter.Write(sigma1Payload.GetBytes());
            //transcriptWriter.Write(sigma2Payload.GetBytes());
            //transcriptWriter.Write(sigma3Payload.GetBytes());
            //transcriptHash = SHA256.HashData(ms.ToArray());
            //ms = new MemoryStream();
            //saltWriter = new BinaryWriter(ms);
            //saltWriter.Write(_fabric.OperationalIPK);
            //saltWriter.Write(transcriptHash);
            //var secureSessionSalt = ms.ToArray();
            //hkdf = new HkdfBytesGenerator(new Sha256Digest());
            //hkdf.Init(new HkdfParameters(sharedSecret, secureSessionSalt, caseInfo));
            //var caseKeys = new byte[48];
            //hkdf.GenerateBytes(caseKeys, 0, 48);
            //var encryptKey = caseKeys.AsSpan().Slice(0, 16).ToArray();
            //var decryptKey = caseKeys.AsSpan().Slice(16, 16).ToArray();
            //var attestationKey = caseKeys.AsSpan().Slice(32, 16).ToArray();

            return true;
        }
    }
}
