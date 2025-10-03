using Matter.Core.Certificates;
using Matter.Core.Fabrics;
using Matter.Core.TLV;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using System;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

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

        internal async Task<ISession> EstablishSessionAsync()
        {
            Console.WriteLine("┌───────────────────────┐");
            Console.WriteLine("| SENDING CASE - Sigma1 |");
            Console.WriteLine("└───────────────────────┘");

            var caseExchange = _unsecureSession.CreateExchange();

            // Exchange CASE Messages, starting with Sigma1
            //
            var spake1InitiatorRandomBytes = RandomNumberGenerator.GetBytes(32);
            var spake1SessionId = RandomNumberGenerator.GetBytes(16);

            //Console.WriteLine("Spake1InitiatorRandomBytes: {0}", BitConverter.ToString(spake1InitiatorRandomBytes).Replace("-", ""));

            var ephermeralKeys = CertificateAuthority.GenerateKeyPair();
            var ephermeralPublicKey = ephermeralKeys.Public as ECPublicKeyParameters;
            var ephermeralPrivateKey = ephermeralKeys.Private as ECPrivateKeyParameters;
            var ephermeralPublicKeysBytes = ephermeralPublicKey.Q.GetEncoded(false);

            Console.WriteLine("spake1InitiatorRandomBytes: {0}", BitConverter.ToString(spake1InitiatorRandomBytes));
            Console.WriteLine("RootPublicKeyBytes: {0}", BitConverter.ToString(_fabric.RootPublicKeyBytes));
            Console.WriteLine("FabricId: {0}", BitConverter.ToUInt64(_fabric.FabricId.ToByteArrayUnsigned()));
            Console.WriteLine("NodeId: {0}", BitConverter.ToUInt64(_node.NodeId.ToByteArrayUnsigned()));

            // Destination identifier is a composite.
            //
            MemoryStream ms = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(ms);
            writer.Write(spake1InitiatorRandomBytes);
            writer.Write(_fabric.RootPublicKeyBytes);
            writer.Write(_fabric.FabricId.ToByteArrayUnsigned());
            writer.Write(_node.NodeId.ToByteArrayUnsigned());

            var destinationId = ms.ToArray();

            Console.WriteLine("DestinationId: {0}", BitConverter.ToString(destinationId));

            var hmac = new HMACSHA256(_fabric.OperationalIPK);
            byte[] hashedDestinationId = hmac.ComputeHash(destinationId);

            Console.WriteLine("Hashed DestinationId: {0}", BitConverter.ToString(hashedDestinationId));

            var sigma1Payload = new MatterTLV();
            sigma1Payload.AddStructure();

            sigma1Payload.AddOctetString(1, spake1InitiatorRandomBytes); // initiatorRandom
            sigma1Payload.AddUInt16(2, BitConverter.ToUInt16(spake1SessionId)); // initiatorSessionId
            sigma1Payload.AddOctetString(3, hashedDestinationId); // destinationId
            sigma1Payload.AddOctetString(4, ephermeralPublicKeysBytes); // initiatorEphPubKey

            sigma1Payload.EndContainer();

            var sigma1MessagePayload = new MessagePayload(sigma1Payload);

            sigma1MessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;

            sigma1MessagePayload.ProtocolId = 0x00;
            sigma1MessagePayload.ProtocolOpCode = 0x30; // Sigma1

            var sigma1MessageFrame = new MessageFrame(sigma1MessagePayload);

            sigma1MessageFrame.MessageFlags |= MessageFlags.S;
            sigma1MessageFrame.SecurityFlags = 0x00;
            sigma1MessageFrame.SourceNodeID = 0x00;

            await caseExchange.SendAsync(sigma1MessageFrame);

            var sigma2MessageFrame = await caseExchange.WaitForNextMessageAsync();

            Console.WriteLine("┌───────────────────────┐");
            Console.WriteLine("| SENDING CASE - Sigma2 |");
            Console.WriteLine("└───────────────────────┘");

            var sigma2Payload = sigma2MessageFrame.MessagePayload.ApplicationPayload;

            sigma2Payload.OpenStructure();

            var sigma2ResponderRandom = sigma2Payload.GetOctetString(1);
            var sigma2ResponderSessionId = sigma2Payload.GetUnsignedInt16(2);
            var sigma2ResponderEphPublicKey = sigma2Payload.GetOctetString(3);
            var sigma2EncryptedPayload = sigma2Payload.GetOctetString(4);

            // Generate the shared secret.
            //
            var sigmaKeyAgreement = AgreementUtilities.GetBasicAgreement("ECDH");
            sigmaKeyAgreement.Init(ephermeralPrivateKey);

            var curve = ECNamedCurveTable.GetByName("P-256");
            var ecPoint = curve.Curve.DecodePoint(sigma2ResponderEphPublicKey);
            var ephPublicKey = new ECPublicKeyParameters(ecPoint, new ECDomainParameters(curve));

            var sharedSecretResult = sigmaKeyAgreement.CalculateAgreement(ephPublicKey);
            var sharedSecret = sharedSecretResult.ToByteArrayUnsigned();

            Console.WriteLine("CASE SharedSecret: {0}", BitConverter.ToString(sharedSecret).Replace("-", ""));

            // Generate the shared key using HKDF
            //
            // Step 1 - the TranscriptHash
            //
            var transcriptHash = SHA256.HashData(sigma1Payload.GetBytes());

            // Step 2 - SALT
            ms = new MemoryStream();
            BinaryWriter saltWriter = new BinaryWriter(ms);
            saltWriter.Write(_fabric.OperationalIPK);
            saltWriter.Write(sigma2ResponderRandom);
            saltWriter.Write(sigma2ResponderEphPublicKey);
            saltWriter.Write(transcriptHash);

            var salt = ms.ToArray();

            // Step 3 - Compute the S2K (the shared key)
            //
            var info = Encoding.ASCII.GetBytes("Sigma2");

            var hkdf = new HkdfBytesGenerator(new Sha256Digest());
            hkdf.Init(new HkdfParameters(sharedSecret, salt, info));

            var sigma2Key = new byte[16];
            hkdf.GenerateBytes(sigma2Key, 0, 16);

            Console.WriteLine(format: "S2K: {0}", BitConverter.ToString(sigma2Key).Replace("-", ""));

            // Step 4 - Use the S2K to decrypt the payload
            //
            var nonce = Encoding.ASCII.GetBytes("NCASE_Sigma2N");

            IBlockCipher cipher = new AesEngine();
            int macSize = 8 * cipher.GetBlockSize();

            AeadParameters keyParamAead = new AeadParameters(new KeyParameter(sigma2Key), macSize, nonce);
            CcmBlockCipher cipherMode = new CcmBlockCipher(cipher);
            cipherMode.Init(false, keyParamAead);

            var outputSize = cipherMode.GetOutputSize(sigma2EncryptedPayload.Length);
            var plainTextData = new byte[outputSize];
            var result = cipherMode.ProcessBytes(sigma2EncryptedPayload, 0, sigma2EncryptedPayload.Length, plainTextData, 0);
            cipherMode.DoFinal(plainTextData, result);

            var TBEData2 = new MatterTLV(plainTextData);

            //Console.WriteLine(TBEData2);

            // TODO Verify this!

            Console.WriteLine("┌───────────────────────┐");
            Console.WriteLine("| SENDING CASE - Sigma3 |");
            Console.WriteLine("└───────────────────────┘");

            // First, generate a signature for our NOC
            //
            var nocSignature = _fabric.OperationalCertificate.GetSignature();

            // We need to convert this signature into a TLV format.
            //
            AsnDecoder.ReadSequence(nocSignature.AsSpan(), AsnEncodingRules.DER, out var offset, out var length, out _);

            var source = nocSignature.AsSpan().Slice(offset, length).ToArray();

            var r = AsnDecoder.ReadInteger(source, AsnEncodingRules.DER, out var bytesConsumed);
            var s = AsnDecoder.ReadInteger(source.AsSpan().Slice(bytesConsumed), AsnEncodingRules.DER, out bytesConsumed);

            var encodedNocCertificateSignature = r.ToByteArray(isUnsigned: true, isBigEndian: true).Concat(s.ToByteArray(isUnsigned: true, isBigEndian: true)).ToArray();

            // Encode the certificate.
            var encodedNocCertificate = new MatterTLV();
            encodedNocCertificate.AddStructure();

            encodedNocCertificate.AddOctetString(1, _fabric.OperationalCertificate.SerialNumber.ToByteArrayUnsigned()); // SerialNumber
            encodedNocCertificate.AddUInt8(2, 1); // signature-algorithm

            encodedNocCertificate.AddList(3); // Issuer
            encodedNocCertificate.AddUInt64(20, _fabric.RootCACertificateId.ToByteArrayUnsigned());
            encodedNocCertificate.EndContainer(); // Close List

            var notBefore = new DateTimeOffset(_fabric.OperationalCertificate.NotBefore).ToEpochTime();
            var notAfter = new DateTimeOffset(_fabric.OperationalCertificate.NotAfter).ToEpochTime();

            encodedNocCertificate.AddUInt32(4, (uint)notBefore); // NotBefore
            encodedNocCertificate.AddUInt32(5, (uint)notAfter); // NotAfter

            encodedNocCertificate.AddList(6); // Subject

            encodedNocCertificate.AddUInt64(17, _fabric.RootNodeId.ToByteArrayUnsigned()); // NodeId
            encodedNocCertificate.AddUInt64(21, _fabric.FabricId.ToByteArrayUnsigned()); // FabricId

            encodedNocCertificate.EndContainer(); // Close List

            encodedNocCertificate.AddUInt8(7, 1); // public-key-algorithm
            encodedNocCertificate.AddUInt8(8, 1); // elliptic-curve-id

            var nocPublicKey = _fabric.OperationalCertificate.GetPublicKey() as ECPublicKeyParameters;
            var nocPublicKeyBytes = nocPublicKey.Q.GetEncoded(false);
            encodedNocCertificate.AddOctetString(9, nocPublicKeyBytes); // PublicKey

            encodedNocCertificate.AddList(10); // Extensions

            encodedNocCertificate.AddStructure(1); // Basic Constraints
            encodedNocCertificate.AddBool(1, false); // is-ca
            encodedNocCertificate.EndContainer(); // Close Basic Constraints

            encodedNocCertificate.AddUInt8(2, 0x1);

            encodedNocCertificate.AddArray(3); // Extended Key Usage
            encodedNocCertificate.AddUInt8(0x02);
            encodedNocCertificate.AddUInt8(0x01);
            encodedNocCertificate.EndContainer();

#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            var nocKeyIdentifier = SHA1.HashData(nocPublicKeyBytes).AsSpan().Slice(0, 20).ToArray();
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

            encodedNocCertificate.AddOctetString(4, nocKeyIdentifier); // subject-key-id
            encodedNocCertificate.AddOctetString(5, _fabric.RootKeyIdentifier); // authority-key-id

            encodedNocCertificate.EndContainer(); // Close Extensions

            encodedNocCertificate.AddOctetString(11, encodedNocCertificateSignature);

            encodedNocCertificate.EndContainer(); // Close Structure

            //Console.WriteLine("───────────────────────────────────────────────────");
            //Console.WriteLine(encodedNocCertificate);
            //Console.WriteLine("───────────────────────────────────────────────────");

            // Build sigma-3-tbsdata
            //
            var sigma3tbs = new MatterTLV();

            sigma3tbs.AddStructure();

            sigma3tbs.AddOctetString(1, encodedNocCertificate.GetBytes()); // initiatorNOC
            sigma3tbs.AddOctetString(3, ephermeralPublicKeysBytes); // initiatorEphPubKey
            sigma3tbs.AddOctetString(4, sigma2ResponderEphPublicKey); // responderEphPubKey

            sigma3tbs.EndContainer();

            var sigma3tbsBytes = sigma3tbs.GetBytes();

            //Console.WriteLine("sigma3tbsBytes {0}", BitConverter.ToString(sigma3tbsBytes).Replace("-", ""));

            // Sign this tbsData3.
            //
            var signer = SignerUtilities.GetSigner("SHA256WITHECDSA");
            signer.Init(true, _fabric.OperationalCertificateKeyPair.Private as ECPrivateKeyParameters);
            signer.BlockUpdate(sigma3tbsBytes, 0, sigma3tbsBytes.Length);
            byte[] sigma3tbsSignature = signer.GenerateSignature();

            //Console.WriteLine("sigma3tbsSignature {0}", BitConverter.ToString(sigma3tbsSignature).Replace("-", ""));

            // Convert from an ASN.1 signature to a TLV encoded one.
            //
            AsnDecoder.ReadSequence(sigma3tbsSignature.AsSpan(), AsnEncodingRules.DER, out offset, out length, out _);

            source = sigma3tbsSignature.AsSpan().Slice(offset, length).ToArray();

            r = AsnDecoder.ReadInteger(source, AsnEncodingRules.DER, out bytesConsumed);
            s = AsnDecoder.ReadInteger(source.AsSpan().Slice(bytesConsumed), AsnEncodingRules.DER, out bytesConsumed);

            var encodedSigma3TbsSignature = r.ToByteArray(isUnsigned: true, isBigEndian: true).Concat(s.ToByteArray(isUnsigned: true, isBigEndian: true)).ToArray();

            // Construct the sigma-3-tbe payload, which will be encrypted.
            //
            var sigma3tbe = new MatterTLV();
            sigma3tbe.AddStructure();
            sigma3tbe.AddOctetString(1, encodedNocCertificate.GetBytes());
            sigma3tbe.AddOctetString(3, encodedSigma3TbsSignature);
            sigma3tbe.EndContainer();

            //Console.WriteLine("sigma1Bytes {0}", BitConverter.ToString(sigma1Payload.GetBytes()).Replace("-", ""));
            //Console.WriteLine("sigma2Bytes {0}", BitConverter.ToString(sigma2Payload.GetBytes()).Replace("-", ""));

            var sigma3tbeTranscriptHash = SHA256.HashData(sigma1Payload.GetBytes().Concat(sigma2Payload.GetBytes()).ToArray());

            Console.WriteLine("S3 TranscriptHash {0}", BitConverter.ToString(sigma3tbeTranscriptHash).Replace("-", ""));

            ms = new MemoryStream();
            saltWriter = new BinaryWriter(ms);
            saltWriter.Write(_fabric.OperationalIPK);
            saltWriter.Write(sigma3tbeTranscriptHash);

            salt = ms.ToArray();

            Console.WriteLine("S3 Salt {0}", BitConverter.ToString(salt).Replace("-", ""));

            // Step 3 - Compute the S3K (the shared key)
            //
            info = Encoding.ASCII.GetBytes("Sigma3");

            hkdf = new HkdfBytesGenerator(new Sha256Digest());
            hkdf.Init(new HkdfParameters(sharedSecret, salt, info));

            var sigma3Key = new byte[16];
            hkdf.GenerateBytes(sigma3Key, 0, 16);

            Console.WriteLine(format: "S3K: {0}", BitConverter.ToString(sigma3Key).Replace("-", ""));

            nonce = Encoding.ASCII.GetBytes("NCASE_Sigma3N");

            keyParamAead = new AeadParameters(new KeyParameter(sigma3Key), macSize, nonce);
            cipherMode = new CcmBlockCipher(cipher);
            cipherMode.Init(true, keyParamAead);

            var sigma3tbeBytes = sigma3tbe.GetBytes();

            outputSize = cipherMode.GetOutputSize(sigma3tbeBytes.Length);
            var encryptedData = new byte[outputSize];
            result = cipherMode.ProcessBytes(sigma3tbeBytes, 0, sigma3tbeBytes.Length, encryptedData, 0);
            cipherMode.DoFinal(encryptedData, result);

            Console.WriteLine("-");
            Console.WriteLine(format: "NocPublicKey: {0}", BitConverter.ToString(nocPublicKeyBytes).Replace("-", ""));
            Console.WriteLine("-");
            Console.WriteLine(format: "Noc: {0}", BitConverter.ToString(encodedNocCertificate.GetBytes()).Replace("-", ""));
            Console.WriteLine("-");
            Console.WriteLine(format: "SignatureData: {0}", BitConverter.ToString(sigma3tbsBytes).Replace("-", ""));
            Console.WriteLine("-");
            Console.WriteLine(format: "Signature: {0}", BitConverter.ToString(encodedNocCertificateSignature).Replace("-", ""));
            Console.WriteLine("-");
            Console.WriteLine(format: "S3 Data: {0}", BitConverter.ToString(sigma3tbeBytes).Replace("-", ""));
            Console.WriteLine("-");
            Console.WriteLine(format: "S3 Encrypted: {0}", BitConverter.ToString(encryptedData).Replace("-", ""));
            Console.WriteLine("-");

            var sigma3Payload = new MatterTLV();
            sigma3Payload.AddStructure();
            sigma3Payload.AddOctetString(1, encryptedData); // sigma3EncryptedPayload
            sigma3Payload.EndContainer();

            var sigma3MessagePayload = new MessagePayload(sigma3Payload);

            sigma3MessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;

            sigma3MessagePayload.ProtocolId = 0x00;
            sigma3MessagePayload.ProtocolOpCode = 0x32; // Sigma3

            var sigma3MessageFrame = new MessageFrame(sigma3MessagePayload);

            sigma3MessageFrame.MessageFlags |= MessageFlags.S;
            sigma3MessageFrame.SecurityFlags = 0x00;
            sigma3MessageFrame.SourceNodeID = 0x00;

            await caseExchange.SendAsync(sigma3MessageFrame);

            var successMessageFrame = await caseExchange.WaitForNextMessageAsync();

            await caseExchange.AcknowledgeMessageAsync(successMessageFrame.MessageCounter);

            //Console.WriteLine("operationalIdentityProtectionKey: {0}", BitConverter.ToString(fabric.OperationalIPK).Replace("-", ""));
            //Console.WriteLine("sigma1Bytes: {0}", BitConverter.ToString(sigma1Payload.GetBytes()).Replace("-", ""));
            //Console.WriteLine("sigma2Bytes: {0}", BitConverter.ToString(sigma2Payload.GetBytes()).Replace("-", ""));
            //Console.WriteLine("sigma3Bytes: {0}", BitConverter.ToString(sigma3Payload.GetBytes()).Replace("-", ""));

            byte[] caseInfo = Encoding.ASCII.GetBytes("SessionKeys");

            ms = new MemoryStream();
            var transcriptWriter = new BinaryWriter(ms);
            transcriptWriter.Write(sigma1Payload.GetBytes());
            transcriptWriter.Write(sigma2Payload.GetBytes());
            transcriptWriter.Write(sigma3Payload.GetBytes());

            transcriptHash = SHA256.HashData(ms.ToArray());

            //Console.WriteLine(format: "hash: {0}", BitConverter.ToString(transcriptHash).Replace("-", ""));

            ms = new MemoryStream();
            saltWriter = new BinaryWriter(ms);
            saltWriter.Write(_fabric.OperationalIPK);
            saltWriter.Write(transcriptHash);

            var secureSessionSalt = ms.ToArray();

            //Console.WriteLine("sharedSecret: {0}", BitConverter.ToString(sharedSecret).Replace("-", ""));
            //Console.WriteLine("salt: {0}", BitConverter.ToString(secureSessionSalt).Replace("-", ""));

            hkdf = new HkdfBytesGenerator(new Sha256Digest());
            hkdf.Init(new HkdfParameters(sharedSecret, secureSessionSalt, caseInfo));

            var caseKeys = new byte[48];
            hkdf.GenerateBytes(caseKeys, 0, 48);

            var encryptKey = caseKeys.AsSpan().Slice(0, 16).ToArray();
            var decryptKey = caseKeys.AsSpan().Slice(16, 16).ToArray();
            var attestationKey = caseKeys.AsSpan().Slice(32, 16).ToArray();

            Console.WriteLine("decryptKey: {0}", BitConverter.ToString(decryptKey).Replace("-", ""));
            Console.WriteLine("encryptKey: {0}", BitConverter.ToString(encryptKey).Replace("-", ""));
            Console.WriteLine("attestationKey: {0}", BitConverter.ToString(attestationKey).Replace("-", ""));

            var udpConnection = _unsecureSession.CreateNewConnection();

            var caseSession = new CaseSecureSession(udpConnection,
                                                    BitConverter.ToUInt64(_fabric.RootNodeId.ToByteArrayUnsigned()),
                                                    BitConverter.ToUInt64(_node.NodeId.ToByteArrayUnsigned()),
                                                    BitConverter.ToUInt16(spake1SessionId),
                                                    sigma2ResponderSessionId,
                                                    encryptKey,
                                                    decryptKey);

            return caseSession;
        }
    }
}
