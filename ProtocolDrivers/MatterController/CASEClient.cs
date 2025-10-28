using Matter.Core.Fabrics;

namespace Matter.Core.Sessions
{
    internal class CASEClient
    {
        private readonly Node _node;
        private readonly Fabric _fabric;
        private readonly UnsecureSession _unsecureSession;

        public CASEClient(Node node, Fabric fabric, UnsecureSession unsecureSession)
        {
            _node = node;
            _fabric = fabric;
            _unsecureSession = unsecureSession;
        }

//        internal async Task<ISession> EstablishSessionAsync()
//        {
//            // Certificate - Authenticated Session Establishment (CASE)
//            var caseExchange = _unsecureSession.CreateExchange();

//            var spake1InitiatorRandomBytes = RandomNumberGenerator.GetBytes(32);
//            var spake1SessionId = RandomNumberGenerator.GetBytes(16);
//            var ephermeralKeys = CertificateAuthority.GenerateKeyPair();
//            var ephermeralPublicKey = ephermeralKeys.Public as ECPublicKeyParameters;
//            var ephermeralPrivateKey = ephermeralKeys.Private as ECPrivateKeyParameters;
//            var ephermeralPublicKeysBytes = ephermeralPublicKey.Q.GetEncoded(false);

//            MemoryStream ms = new MemoryStream();
//            BinaryWriter writer = new BinaryWriter(ms);
//            writer.Write(spake1InitiatorRandomBytes);
//            writer.Write(_fabric.RootPublicKeyBytes);
//            writer.Write(_fabric.FabricId.ToByteArrayUnsigned());
//            writer.Write(_node.NodeId.ToByteArrayUnsigned());

//            var destinationId = ms.ToArray();
//            var hmac = new HMACSHA256(_fabric.OperationalIPK);
//            byte[] hashedDestinationId = hmac.ComputeHash(destinationId);

//            var sigma1Payload = new MatterTLV();
//            sigma1Payload.AddStructure();
//            sigma1Payload.AddOctetString(1, spake1InitiatorRandomBytes); // initiatorRandom
//            sigma1Payload.AddUInt16(2, BitConverter.ToUInt16(spake1SessionId)); // initiatorSessionId
//            sigma1Payload.AddOctetString(3, hashedDestinationId); // destinationId
//            sigma1Payload.AddOctetString(4, ephermeralPublicKeysBytes); // initiatorEphPubKey
//            sigma1Payload.EndContainer();

//            var sigma1MessagePayload = new MessagePayload(sigma1Payload);
//            sigma1MessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
//            sigma1MessagePayload.ProtocolId = 0x00;
//            sigma1MessagePayload.ProtocolOpCode = 0x30; // Sigma1

//            var sigma1MessageFrame = new MessageFrame(sigma1MessagePayload);
//            sigma1MessageFrame.MessageFlags |= MessageFlags.S;
//            sigma1MessageFrame.SecurityFlags = 0x00;
//            sigma1MessageFrame.SourceNodeID = 0x00;

//            await caseExchange.SendAsync(sigma1MessageFrame);
//            var sigma2MessageFrame = await caseExchange.WaitForNextMessageAsync();

//            var sigma2Payload = sigma2MessageFrame.MessagePayload.ApplicationPayload;
//            sigma2Payload.OpenStructure();
//            var sigma2ResponderRandom = sigma2Payload.GetOctetString(1);
//            var sigma2ResponderSessionId = sigma2Payload.GetUnsignedInt16(2);
//            var sigma2ResponderEphPublicKey = sigma2Payload.GetOctetString(3);
//            var sigma2EncryptedPayload = sigma2Payload.GetOctetString(4);

//            var sigmaKeyAgreement = AgreementUtilities.GetBasicAgreement("ECDH");
//            sigmaKeyAgreement.Init(ephermeralPrivateKey);
//            var curve = ECNamedCurveTable.GetByName("P-256");
//            var ecPoint = curve.Curve.DecodePoint(sigma2ResponderEphPublicKey);
//            var ephPublicKey = new ECPublicKeyParameters(ecPoint, new ECDomainParameters(curve));
//            var sharedSecretResult = sigmaKeyAgreement.CalculateAgreement(ephPublicKey);
//            var sharedSecret = sharedSecretResult.ToByteArrayUnsigned();
//            var transcriptHash = SHA256.HashData(sigma1Payload.GetBytes());

//            ms = new MemoryStream();
//            BinaryWriter saltWriter = new BinaryWriter(ms);
//            saltWriter.Write(_fabric.OperationalIPK);
//            saltWriter.Write(sigma2ResponderRandom);
//            saltWriter.Write(sigma2ResponderEphPublicKey);
//            saltWriter.Write(transcriptHash);
//            var salt = ms.ToArray();

//            var info = Encoding.ASCII.GetBytes("Sigma2");
//            var hkdf = new HkdfBytesGenerator(new Sha256Digest());
//            hkdf.Init(new HkdfParameters(sharedSecret, salt, info));
//            var sigma2Key = new byte[16];
//            hkdf.GenerateBytes(sigma2Key, 0, 16);
//            var nonce = Encoding.ASCII.GetBytes("NCASE_Sigma2N");
//            IBlockCipher cipher = new AesEngine();
//            int macSize = 8 * cipher.GetBlockSize();
//            AeadParameters keyParamAead = new AeadParameters(new KeyParameter(sigma2Key), macSize, nonce);
//            CcmBlockCipher cipherMode = new CcmBlockCipher(cipher);
//            cipherMode.Init(false, keyParamAead);
//            var outputSize = cipherMode.GetOutputSize(sigma2EncryptedPayload.Length);
//            var plainTextData = new byte[outputSize];
//            var result = cipherMode.ProcessBytes(sigma2EncryptedPayload, 0, sigma2EncryptedPayload.Length, plainTextData, 0);
//            cipherMode.DoFinal(plainTextData, result);
//            var TBEData2 = new MatterTLV(plainTextData);
//            var nocSignature = _fabric.OperationalCertificate.GetSignature();

//            AsnDecoder.ReadSequence(nocSignature.AsSpan(), AsnEncodingRules.DER, out var offset, out var length, out _);
//            var source = nocSignature.AsSpan().Slice(offset, length).ToArray();
//            var r = AsnDecoder.ReadInteger(source, AsnEncodingRules.DER, out var bytesConsumed);
//            var s = AsnDecoder.ReadInteger(source.AsSpan().Slice(bytesConsumed), AsnEncodingRules.DER, out bytesConsumed);
//            var encodedNocCertificateSignature = r.ToByteArray(isUnsigned: true, isBigEndian: true).Concat(s.ToByteArray(isUnsigned: true, isBigEndian: true)).ToArray();
//            var encodedNocCertificate = new MatterTLV();
//            encodedNocCertificate.AddStructure();
//            encodedNocCertificate.AddOctetString(1, _fabric.OperationalCertificate.SerialNumber.ToByteArrayUnsigned()); // SerialNumber
//            encodedNocCertificate.AddUInt8(2, 1); // signature-algorithm
//            encodedNocCertificate.AddList(3); // Issuer
//            encodedNocCertificate.AddUInt64(20, _fabric.RootCACertificateId.ToByteArrayUnsigned());
//            encodedNocCertificate.EndContainer(); // Close List
//            var notBefore = new DateTimeOffset(_fabric.OperationalCertificate.NotBefore).ToEpochTime();
//            var notAfter = new DateTimeOffset(_fabric.OperationalCertificate.NotAfter).ToEpochTime();
//            encodedNocCertificate.AddUInt32(4, (uint)notBefore); // NotBefore
//            encodedNocCertificate.AddUInt32(5, (uint)notAfter); // NotAfter
//            encodedNocCertificate.AddList(6); // Subject
//            encodedNocCertificate.AddUInt64(17, _fabric.RootNodeId.ToByteArrayUnsigned()); // NodeId
//            encodedNocCertificate.AddUInt64(21, _fabric.FabricId.ToByteArrayUnsigned()); // FabricId
//            encodedNocCertificate.EndContainer(); // Close List
//            encodedNocCertificate.AddUInt8(7, 1); // public-key-algorithm
//            encodedNocCertificate.AddUInt8(8, 1); // elliptic-curve-id
//            var nocPublicKey = _fabric.OperationalCertificate.GetPublicKey() as ECPublicKeyParameters;
//            var nocPublicKeyBytes = nocPublicKey.Q.GetEncoded(false);
//            encodedNocCertificate.AddOctetString(9, nocPublicKeyBytes); // PublicKey
//            encodedNocCertificate.AddList(10); // Extensions
//            encodedNocCertificate.AddStructure(1); // Basic Constraints
//            encodedNocCertificate.AddBool(1, false); // is-ca
//            encodedNocCertificate.EndContainer(); // Close Basic Constraints
//            encodedNocCertificate.AddUInt8(2, 0x1);
//            encodedNocCertificate.AddArray(3); // Extended Key Usage
//            encodedNocCertificate.AddUInt8(0x02);
//            encodedNocCertificate.AddUInt8(0x01);
//            encodedNocCertificate.EndContainer();

//#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
//            var nocKeyIdentifier = SHA1.HashData(nocPublicKeyBytes).AsSpan().Slice(0, 20).ToArray();
//#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

//            encodedNocCertificate.AddOctetString(4, nocKeyIdentifier); // subject-key-id
//            encodedNocCertificate.AddOctetString(5, _fabric.RootKeyIdentifier); // authority-key-id
//            encodedNocCertificate.EndContainer(); // Close Extensions
//            encodedNocCertificate.AddOctetString(11, encodedNocCertificateSignature);
//            encodedNocCertificate.EndContainer(); // Close Structure

//            var sigma3tbs = new MatterTLV();
//            sigma3tbs.AddStructure();
//            sigma3tbs.AddOctetString(1, encodedNocCertificate.GetBytes()); // initiatorNOC
//            sigma3tbs.AddOctetString(3, ephermeralPublicKeysBytes); // initiatorEphPubKey
//            sigma3tbs.AddOctetString(4, sigma2ResponderEphPublicKey); // responderEphPubKey
//            sigma3tbs.EndContainer();
//            var sigma3tbsBytes = sigma3tbs.GetBytes();
//            var signer = SignerUtilities.GetSigner("SHA256WITHECDSA");
//            signer.Init(true, _fabric.OperationalCertificateKeyPair.Private as ECPrivateKeyParameters);
//            signer.BlockUpdate(sigma3tbsBytes, 0, sigma3tbsBytes.Length);
//            byte[] sigma3tbsSignature = signer.GenerateSignature();
//            AsnDecoder.ReadSequence(sigma3tbsSignature.AsSpan(), AsnEncodingRules.DER, out offset, out length, out _);
//            source = sigma3tbsSignature.AsSpan().Slice(offset, length).ToArray();
//            r = AsnDecoder.ReadInteger(source, AsnEncodingRules.DER, out bytesConsumed);
//            s = AsnDecoder.ReadInteger(source.AsSpan().Slice(bytesConsumed), AsnEncodingRules.DER, out bytesConsumed);
//            var encodedSigma3TbsSignature = r.ToByteArray(isUnsigned: true, isBigEndian: true).Concat(s.ToByteArray(isUnsigned: true, isBigEndian: true)).ToArray();

//            var sigma3tbe = new MatterTLV();
//            sigma3tbe.AddStructure();
//            sigma3tbe.AddOctetString(1, encodedNocCertificate.GetBytes());
//            sigma3tbe.AddOctetString(3, encodedSigma3TbsSignature);
//            sigma3tbe.EndContainer();
//            var sigma3tbeTranscriptHash = SHA256.HashData(sigma1Payload.GetBytes().Concat(sigma2Payload.GetBytes()).ToArray());
//            ms = new MemoryStream();
//            saltWriter = new BinaryWriter(ms);
//            saltWriter.Write(_fabric.OperationalIPK);
//            saltWriter.Write(sigma3tbeTranscriptHash);
//            salt = ms.ToArray();

//            info = Encoding.ASCII.GetBytes("Sigma3");
//            hkdf = new HkdfBytesGenerator(new Sha256Digest());
//            hkdf.Init(new HkdfParameters(sharedSecret, salt, info));
//            var sigma3Key = new byte[16];
//            hkdf.GenerateBytes(sigma3Key, 0, 16);
//            nonce = Encoding.ASCII.GetBytes("NCASE_Sigma3N");
//            keyParamAead = new AeadParameters(new KeyParameter(sigma3Key), macSize, nonce);
//            cipherMode = new CcmBlockCipher(cipher);
//            cipherMode.Init(true, keyParamAead);
//            var sigma3tbeBytes = sigma3tbe.GetBytes();
//            outputSize = cipherMode.GetOutputSize(sigma3tbeBytes.Length);
//            var encryptedData = new byte[outputSize];
//            result = cipherMode.ProcessBytes(sigma3tbeBytes, 0, sigma3tbeBytes.Length, encryptedData, 0);
//            cipherMode.DoFinal(encryptedData, result);
//            var sigma3Payload = new MatterTLV();
//            sigma3Payload.AddStructure();
//            sigma3Payload.AddOctetString(1, encryptedData); // sigma3EncryptedPayload
//            sigma3Payload.EndContainer();

//            var sigma3MessagePayload = new MessagePayload(sigma3Payload);
//            sigma3MessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
//            sigma3MessagePayload.ProtocolId = 0x00;
//            sigma3MessagePayload.ProtocolOpCode = 0x32; // Sigma3
//            var sigma3MessageFrame = new MessageFrame(sigma3MessagePayload);
//            sigma3MessageFrame.MessageFlags |= MessageFlags.S;
//            sigma3MessageFrame.SecurityFlags = 0x00;
//            sigma3MessageFrame.SourceNodeID = 0x00;

//            await caseExchange.SendAsync(sigma3MessageFrame);
//            var successMessageFrame = await caseExchange.WaitForNextMessageAsync();
//            await caseExchange.AcknowledgeMessageAsync(successMessageFrame.MessageCounter);

//            byte[] caseInfo = Encoding.ASCII.GetBytes("SessionKeys");
//            ms = new MemoryStream();
//            var transcriptWriter = new BinaryWriter(ms);
//            transcriptWriter.Write(sigma1Payload.GetBytes());
//            transcriptWriter.Write(sigma2Payload.GetBytes());
//            transcriptWriter.Write(sigma3Payload.GetBytes());
//            transcriptHash = SHA256.HashData(ms.ToArray());
//            ms = new MemoryStream();
//            saltWriter = new BinaryWriter(ms);
//            saltWriter.Write(_fabric.OperationalIPK);
//            saltWriter.Write(transcriptHash);
//            var secureSessionSalt = ms.ToArray();
//            hkdf = new HkdfBytesGenerator(new Sha256Digest());
//            hkdf.Init(new HkdfParameters(sharedSecret, secureSessionSalt, caseInfo));
//            var caseKeys = new byte[48];
//            hkdf.GenerateBytes(caseKeys, 0, 48);
//            var encryptKey = caseKeys.AsSpan().Slice(0, 16).ToArray();
//            var decryptKey = caseKeys.AsSpan().Slice(16, 16).ToArray();
//            var attestationKey = caseKeys.AsSpan().Slice(32, 16).ToArray();
//            var udpConnection = _unsecureSession.CreateNewConnection();
//            var caseSession = new SecureSession(udpConnection, BitConverter.ToUInt16(spake1SessionId), sigma2ResponderSessionId, encryptKey, decryptKey);

//            return caseSession;
//        }
    }
}
