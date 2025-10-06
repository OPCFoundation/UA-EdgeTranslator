using InTheHand.Bluetooth;
using Matter.Core.BTP;
using Matter.Core.Cryptography;
using Matter.Core.Fabrics;
using Matter.Core.Sessions;
using Matter.Core.TLV;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Matter.Core.Commissioning
{
    public class BluetoothCommissioner
    {
        private readonly List<string> _receivedAdvertisments = new();
        private Fabric _fabric;
        private IBluetooth _bluetooth;
        private CommissioningPayload _payload;

        public BluetoothCommissioner(Fabric fabric)
        {
            _fabric = fabric;

#if WINDOWS10_0_26100_0_OR_GREATER
            Console.WriteLine("Running on Windows");
            _bluetooth = new BluetoothWindows();
#else
            Console.WriteLine("Running on Linux");
            _bluetooth = new BluetoothLinux();
#endif
        }

        public async Task StartBluetoothDiscovery(CommissioningPayload payload)
        {
            _payload = payload;

            BluetoothLEScanOptions options = new BluetoothLEScanOptions();
            BluetoothLEScanFilter filter = new BluetoothLEScanFilter();
            filter.Services.Add(BTPConnection.MATTER_UUID);
            options.Filters.Add(filter);
            options.AcceptAllAdvertisements = false;
            options.KeepRepeatedDevices = false;

            // scan for 15 seconds
            _bluetooth.AdvertisementReceived += Bluetooth_AdvertisementReceived;

            await _bluetooth.StartLEScanAsync(options).ConfigureAwait(false);
            await Task.Delay(15000).ConfigureAwait(false);
            await _bluetooth.StopLEScanAsync().ConfigureAwait(false);

            _bluetooth.AdvertisementReceived -= Bluetooth_AdvertisementReceived;
        }

        void Bluetooth_AdvertisementReceived(object sender, IBluetoothAdvertisingEvent e)
        {
            // If we got this advertisment already, just ignore it.
            if (_receivedAdvertisments.Contains(e.Device.Id))
            {
                return;
            }

            _receivedAdvertisments.Add(e.Device.Id);

            if (e.ServiceData().ContainsKey(BTPConnection.MATTER_UUID))
            {
                try
                {
                    byte[] data = e.ServiceData()[BTPConnection.MATTER_UUID];
                    var discriminator = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(((ReadOnlySpan<byte>)data).Slice(1, 2)) & 0xFFF);
                    Console.WriteLine("Matter device advertisment received from {0} with a discriminator of {1}", e.Device.Id, discriminator);

                    if (discriminator != _payload.Discriminator)
                    {
                        return;
                    }

                    _bluetooth.StopLEScanAsync().ConfigureAwait(false).GetAwaiter().GetResult();

                    Console.WriteLine("Matter device discovered with the specified discriminator of {0}", discriminator);

                    // Initial handshake
                    Console.WriteLine("Starting BTPSession");
                    BTPConnection _btpSession = new(e.Device);
                    var connection = _btpSession.OpenConnection();

                    Console.WriteLine("BTPSession has been established. Starting PASE Exchange....");

                    var unsecureSession = new UnsecureSession(_btpSession);
                    var unsecureExchange = unsecureSession.CreateExchange();
                    var initiatorSessionId = BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(16));

                    // We need a control octet, the tag, the length and the value.
                    var PBKDFParamRequest = new MatterTLV();
                    PBKDFParamRequest.AddStructure();
                    PBKDFParamRequest.AddOctetString(1, RandomNumberGenerator.GetBytes(32));
                    PBKDFParamRequest.AddUInt16(2, initiatorSessionId);
                    PBKDFParamRequest.AddUInt16(3, 0);
                    PBKDFParamRequest.AddBool(4, false);
                    PBKDFParamRequest.EndContainer();

                    var messagePayload = new MessagePayload(PBKDFParamRequest);
                    messagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
                    messagePayload.ProtocolId = 0x00;
                    messagePayload.ProtocolOpCode = 0x20; // PBKDFParamRequest

                    var messageFrame = new MessageFrame(messagePayload);
                    messageFrame.MessageFlags |= MessageFlags.S;
                    messageFrame.SessionID = 0x00;
                    messageFrame.SecurityFlags = 0x00;
                    unsecureExchange.SendAsync(messageFrame).GetAwaiter().GetResult();

                    var responseMessageFrame = unsecureExchange.WaitForNextMessageAsync().GetAwaiter().GetResult();
                    if (MessageFrame.IsStatusReport(responseMessageFrame))
                    {
                        return;
                    }

                    var PBKDFParamResponse = responseMessageFrame.MessagePayload.ApplicationPayload;

                    PBKDFParamResponse.OpenStructure();
                    var initiatorRandomBytes2 = PBKDFParamResponse.GetOctetString(1);
                    var responderRandomBytes = PBKDFParamResponse.GetOctetString(2);
                    var responderSessionId = PBKDFParamResponse.GetUnsignedInt16(3);
                    var peerSessionId = responderSessionId;

                    PBKDFParamResponse.OpenStructure(4);
                    var iterations = PBKDFParamResponse.GetUnsignedInt16(1);
                    var salt = PBKDFParamResponse.GetOctetString(2);
                    PBKDFParamResponse.CloseContainer();

                    Console.WriteLine("Iterations: {0}\nSalt: {1}\nSalt Base64: {2}", iterations, Encoding.ASCII.GetString(salt), Convert.ToBase64String(salt));

                    var spakeContext = Encoding.ASCII.GetBytes("CHIP PAKE V1 Commissioning");
                    var contextToHash = new List<byte>();
                    contextToHash.AddRange(spakeContext);
                    contextToHash.AddRange(PBKDFParamRequest.GetBytes());
                    contextToHash.AddRange(PBKDFParamResponse.GetBytes());

                    var sessionContextHash = SHA256.HashData(contextToHash.ToArray());

                    Console.WriteLine(string.Join(",", sessionContextHash));
                    Console.WriteLine("Context: {0}", BitConverter.ToString(spakeContext));
                    Console.WriteLine("Request: {0}", BitConverter.ToString(PBKDFParamRequest.GetBytes()));
                    Console.WriteLine("Response: {0}", BitConverter.ToString(PBKDFParamResponse.GetBytes()));
                    Console.WriteLine("Hash: {0}", BitConverter.ToString(sessionContextHash));

                    var pake1 = new MatterTLV();
                    pake1.AddStructure();
                    var (w0, w1, x, X) = CryptographyMethods.Crypto_PAKEValues_Initiator(20202021, iterations, salt);
                    var byteString = X.GetEncoded(false).ToArray();
                    pake1.AddOctetString(1, byteString);
                    pake1.EndContainer();

                    var pake1MessagePayload = new MessagePayload(pake1);
                    pake1MessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
                    pake1MessagePayload.ProtocolId = 0x00;
                    pake1MessagePayload.ProtocolOpCode = 0x22; //PASE Pake1

                    var pake1MessageFrame = new MessageFrame(pake1MessagePayload);
                    pake1MessageFrame.MessageFlags |= MessageFlags.S;
                    pake1MessageFrame.SessionID = 0x00;
                    pake1MessageFrame.SecurityFlags = 0x00;
                    unsecureExchange.SendAsync(pake1MessageFrame).GetAwaiter().GetResult();

                    var pake2MessageFrame = unsecureExchange.WaitForNextMessageAsync().GetAwaiter().GetResult();
                    var pake2 = pake2MessageFrame.MessagePayload.ApplicationPayload;
                    pake2.OpenStructure();
                    var Y = pake2.GetOctetString(1);
                    var Verifier = pake2.GetOctetString(2);
                    pake2.CloseContainer();

                    var (Ke, hAY, hBX) = CryptographyMethods.Crypto_P2(sessionContextHash, w0, w1, x, X, Y);
                    if (!hBX.SequenceEqual(Verifier))
                    {
                        throw new Exception("Verifier doesn't match!");
                    }

                    var pake3 = new MatterTLV();
                    pake3.AddStructure();
                    pake3.AddOctetString(1, hAY);
                    pake3.EndContainer();

                    var pake3MessagePayload = new MessagePayload(pake3);
                    pake3MessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
                    pake3MessagePayload.ProtocolId = 0x00;
                    pake3MessagePayload.ProtocolOpCode = 0x24; //PASE Pake3

                    var pake3MessageFrame = new MessageFrame(pake3MessagePayload);
                    pake3MessageFrame.MessageFlags |= MessageFlags.S;
                    pake3MessageFrame.SessionID = 0x00;
                    pake3MessageFrame.SecurityFlags = 0x00;
                    unsecureExchange.SendAsync(pake3MessageFrame).GetAwaiter().GetResult();

                    var pakeFinishedMessageFrame = unsecureExchange.WaitForNextMessageAsync().GetAwaiter().GetResult();
                    unsecureExchange.AcknowledgeMessageAsync(pakeFinishedMessageFrame.MessageCounter).GetAwaiter().GetResult();
                    unsecureExchange.Close();

                    byte[] info = Encoding.ASCII.GetBytes("SessionKeys");
                    var emptySalt = new byte[0];
                    var hkdf = new HkdfBytesGenerator(new Sha256Digest());
                    hkdf.Init(new HkdfParameters(Ke, emptySalt, info));

                    var keys = new byte[48];
                    hkdf.GenerateBytes(keys, 0, 48);
                    var encryptKey = keys.AsSpan().Slice(0, 16).ToArray();
                    var decryptKey = keys.AsSpan().Slice(16, 16).ToArray();
                    var attestationKey = keys.AsSpan().Slice(32, 16).ToArray();
                    var paseSession = new PaseSecureSession(_btpSession, initiatorSessionId, peerSessionId, encryptKey, decryptKey);
                    var paseExchange = paseSession.CreateExchange();

                    var armFailsafeRequest = new MatterTLV();
                    armFailsafeRequest.AddStructure();
                    armFailsafeRequest.AddArray(tagNumber: 2);
                    armFailsafeRequest.AddList();
                    armFailsafeRequest.AddUInt16(tagNumber: 0, 0x00); // Endpoint 0x00
                    armFailsafeRequest.AddUInt32(tagNumber: 1, 0x3E); // ClusterId 0x3E - Operational Credentials
                    armFailsafeRequest.AddUInt16(tagNumber: 2, 0x04); // 11.18.6. Commands CSRRequest
                    armFailsafeRequest.EndContainer(); // Close the list
                    armFailsafeRequest.EndContainer(); // Close the array
                    armFailsafeRequest.EndContainer(); // Close the structure

                    var armFailsafeMessagePayload = new MessagePayload(armFailsafeRequest);
                    armFailsafeMessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
                    armFailsafeMessagePayload.ProtocolId = 0x01; // IM Protocol Messages
                    armFailsafeMessagePayload.ProtocolOpCode = 0x09; // InvokeCommand

                    var armFailsafeMessageFrame = new MessageFrame(armFailsafeMessagePayload);
                    armFailsafeMessageFrame.MessageFlags |= MessageFlags.S;
                    armFailsafeMessageFrame.SecurityFlags = 0x00;
                    armFailsafeMessageFrame.SourceNodeID = 0x00;
                    paseExchange.SendAsync(armFailsafeMessageFrame).GetAwaiter().GetResult();
                    paseExchange.ReceiveAsync().GetAwaiter().GetResult();

                    var csrRequest = new MatterTLV();
                    csrRequest.AddStructure();
                    csrRequest.AddBool(0, false);
                    csrRequest.AddBool(1, false);
                    csrRequest.AddArray(tagNumber: 2); // InvokeRequests
                    csrRequest.AddStructure();
                    csrRequest.AddList(tagNumber: 0); // CommandPath
                    csrRequest.AddUInt16(tagNumber: 0, 0x00); // Endpoint 0x00
                    csrRequest.AddUInt32(tagNumber: 1, 0x3E); // ClusterId 0x3E - Operational Credentials
                    csrRequest.AddUInt16(tagNumber: 2, 0x04); // 11.18.6. Commands CSRRequest
                    csrRequest.EndContainer();
                    csrRequest.AddStructure(1); // CommandFields
                    csrRequest.AddOctetString(0, RandomNumberGenerator.GetBytes(32)); // CSRNonce
                    csrRequest.EndContainer(); // Close the CommandFields
                    csrRequest.EndContainer(); // Close the structure
                    csrRequest.EndContainer(); // Close the array
                    csrRequest.AddUInt8(255, 12); // interactionModelRevision
                    csrRequest.EndContainer(); // Close the structure

                    var csrRequestMessagePayload = new MessagePayload(csrRequest);
                    csrRequestMessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
                    csrRequestMessagePayload.ProtocolId = 0x01; // IM Protocol Messages
                    csrRequestMessagePayload.ProtocolOpCode = 0x08; // InvokeRequest

                    var csrRequestMessageFrame = new MessageFrame(csrRequestMessagePayload);
                    csrRequestMessageFrame.MessageFlags |= MessageFlags.S;
                    csrRequestMessageFrame.SecurityFlags = 0x00;
                    csrRequestMessageFrame.SourceNodeID = 0x00;
                    paseExchange.SendAsync(csrRequestMessageFrame).GetAwaiter().GetResult();

                    var csrResponseMessageFrame = paseExchange.WaitForNextMessageAsync().GetAwaiter().GetResult();

                    var csrResponsePayload = csrResponseMessageFrame.MessagePayload.ApplicationPayload;
                    csrResponsePayload.OpenStructure();
                    csrResponsePayload.GetBoolean(0);
                    csrResponsePayload.OpenArray(1);
                    csrResponsePayload.OpenStructure();
                    csrResponsePayload.OpenStructure(0);
                    csrResponsePayload.OpenList(0);
                    csrResponsePayload.GetUnsignedInt8(0);
                    csrResponsePayload.GetUnsignedInt8(1);
                    csrResponsePayload.GetUnsignedInt8(2);
                    csrResponsePayload.CloseContainer(); // Close list.
                    csrResponsePayload.OpenStructure(1);
                    var nocsrBytes = csrResponsePayload.GetOctetString(0);
                    var nocsrString = Encoding.ASCII.GetString(nocsrBytes.ToArray());

                    var nocPayload = new MatterTLV(nocsrBytes);
                    nocPayload.OpenStructure();
                    var derBytes = nocPayload.GetOctetString(1);
                    var certificateRequest = new Pkcs10CertificationRequest(derBytes);
                    var peerPublicKey = certificateRequest.GetPublicKey();
                    var peerNocPublicKey = peerPublicKey as ECPublicKeyParameters;
                    var peerNocPublicKeyBytes = peerNocPublicKey.Q.GetEncoded(false);

#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
                    var peerNocKeyIdentifier = SHA1.HashData(peerNocPublicKeyBytes).AsSpan().Slice(0, 20).ToArray();
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

                    var csrInfo = certificateRequest.GetCertificationRequestInfo();
                    var certGenerator = new X509V3CertificateGenerator();
                    var randomGenerator = new CryptoApiRandomGenerator();
                    var random = new SecureRandom(randomGenerator);
                    var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
                    var operationalId = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
                    certGenerator.SetSerialNumber(serialNumber);
                    var subjectOids = new List<DerObjectIdentifier>();
                    var subjectValues = new List<string>();
                    subjectOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.1")); // NodeId
                    subjectOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.5")); // FabricId
                    var t = Encoding.UTF8.GetBytes(e.Device.Id);
                    t.Reverse();
                    var s1 = BitConverter.ToString(t).Replace("-", "");
                    subjectValues.Add(s1);
                    subjectValues.Add("FAB000000000001D");
                    X509Name subjectDN = new X509Name(subjectOids, subjectValues);
                    certGenerator.SetSubjectDN(subjectDN);
                    var issuerOids = new List<DerObjectIdentifier>();
                    var issuerValues = new List<string>();
                    issuerOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.4"));
                    issuerValues.Add($"CACACACA00000001");
                    X509Name issuerDN = new X509Name(issuerOids, issuerValues);
                    certGenerator.SetIssuerDN(issuerDN); // The root certificate is the issuer.
                    certGenerator.SetNotBefore(DateTime.UtcNow.AddYears(-10));
                    certGenerator.SetNotAfter(DateTime.UtcNow.AddYears(10));
                    certGenerator.SetPublicKey(certificateRequest.GetPublicKey() as ECPublicKeyParameters);
                    certGenerator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
                    certGenerator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature));
                    certGenerator.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(KeyPurposeID.id_kp_clientAuth, KeyPurposeID.id_kp_serverAuth));
                    certGenerator.AddExtension(X509Extensions.SubjectKeyIdentifier, false, new SubjectKeyIdentifier(peerNocKeyIdentifier));
                    certGenerator.AddExtension(X509Extensions.AuthorityKeyIdentifier, false, new AuthorityKeyIdentifier(_fabric.RootKeyIdentifier));
                    ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA256WITHECDSA", _fabric.RootCAKeyPair.Private as ECPrivateKeyParameters);
                    var peerNoc = certGenerator.Generate(signatureFactory);
                    peerNoc.CheckValidity();
                    paseExchange.AcknowledgeMessageAsync(csrResponseMessageFrame.MessageCounter).GetAwaiter().GetResult();

                    paseExchange = paseSession.CreateExchange();

                    var encodedRootCertificate = new MatterTLV();
                    encodedRootCertificate.AddStructure();
                    encodedRootCertificate.AddOctetString(1, _fabric.RootCACertificate.SerialNumber.ToByteArrayUnsigned()); // SerialNumber
                    encodedRootCertificate.AddUInt8(2, 1); // signature-algorithm
                    encodedRootCertificate.AddList(3); // Issuer
                    encodedRootCertificate.AddUInt64(20, _fabric.RootCACertificateId.ToByteArrayUnsigned());
                    encodedRootCertificate.EndContainer(); // Close List
                    var notBefore = new DateTimeOffset(_fabric.RootCACertificate.NotBefore).ToEpochTime();
                    var notAfter = new DateTimeOffset(_fabric.RootCACertificate.NotAfter).ToEpochTime();
                    encodedRootCertificate.AddUInt32(4, (uint)notBefore); // NotBefore
                    encodedRootCertificate.AddUInt32(5, (uint)notAfter); // NotAfter
                    encodedRootCertificate.AddList(6); // Subject
                    encodedRootCertificate.AddUInt64(20, _fabric.RootCACertificateId.ToByteArrayUnsigned());
                    encodedRootCertificate.EndContainer(); // Close List
                    encodedRootCertificate.AddUInt8(7, 1); // public-key-algorithm
                    encodedRootCertificate.AddUInt8(8, 1); // elliptic-curve-id
                    var rootPublicKey = _fabric.RootCACertificate.GetPublicKey() as ECPublicKeyParameters;
                    var rootPublicKeyBytes = rootPublicKey!.Q.GetEncoded(false);
                    encodedRootCertificate.AddOctetString(9, rootPublicKeyBytes); // PublicKey
                    encodedRootCertificate.AddList(10); // Extensions
                    encodedRootCertificate.AddStructure(1); // Basic Constraints
                    encodedRootCertificate.AddBool(1, true); // is-ca
                    encodedRootCertificate.EndContainer(); // Close Basic Constraints
                    encodedRootCertificate.AddUInt8(2, 0x60);
                    encodedRootCertificate.AddOctetString(4, _fabric.RootKeyIdentifier); // subject-key-id
                    encodedRootCertificate.AddOctetString(5, _fabric.RootKeyIdentifier); // authority-key-id
                    encodedRootCertificate.EndContainer(); // Close Extensions
                    var signature = _fabric.RootCACertificate.GetSignature();
                    AsnDecoder.ReadSequence(signature.AsSpan(), AsnEncodingRules.DER, out var offset, out var length, out _);
                    var source = signature.AsSpan().Slice(offset, length).ToArray();
                    var r = AsnDecoder.ReadInteger(source, AsnEncodingRules.DER, out var bytesConsumed);
                    var s = AsnDecoder.ReadInteger(source.AsSpan().Slice(bytesConsumed), AsnEncodingRules.DER, out bytesConsumed);
                    var encodedRootCertificateSignature = r.ToByteArray(isUnsigned: true, isBigEndian: true).Concat(s.ToByteArray(isUnsigned: true, isBigEndian: true)).ToArray();
                    encodedRootCertificate.AddOctetString(11, encodedRootCertificateSignature);
                    encodedRootCertificate.EndContainer(); // Close Structure
                    var addTrustedRootCertificateRequest = new MatterTLV();
                    addTrustedRootCertificateRequest.AddStructure();
                    addTrustedRootCertificateRequest.AddBool(0, false);
                    addTrustedRootCertificateRequest.AddBool(1, false);
                    addTrustedRootCertificateRequest.AddArray(tagNumber: 2); // InvokeRequests
                    addTrustedRootCertificateRequest.AddStructure();
                    addTrustedRootCertificateRequest.AddList(tagNumber: 0); // CommandPath
                    addTrustedRootCertificateRequest.AddUInt16(tagNumber: 0, 0x00); // Endpoint 0x00
                    addTrustedRootCertificateRequest.AddUInt32(tagNumber: 1, 0x3E); // ClusterId 0x3E - Node Operational Credentials
                    addTrustedRootCertificateRequest.AddUInt16(tagNumber: 2, 0x0B); // 11.18.6. Command AddTrustedRootCertificate
                    addTrustedRootCertificateRequest.EndContainer();
                    addTrustedRootCertificateRequest.AddStructure(1); // CommandFields
                    addTrustedRootCertificateRequest.AddOctetString(0, encodedRootCertificate.GetBytes()); // RootCertificate
                    addTrustedRootCertificateRequest.EndContainer(); // Close the CommandFields
                    addTrustedRootCertificateRequest.EndContainer(); // Close the structure
                    addTrustedRootCertificateRequest.EndContainer(); // Close the array
                    addTrustedRootCertificateRequest.AddUInt8(255, 12); // interactionModelRevision
                    addTrustedRootCertificateRequest.EndContainer(); // Close the structure

                    var addTrustedRootCertificateRequestMessagePayload = new MessagePayload(addTrustedRootCertificateRequest);
                    addTrustedRootCertificateRequestMessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
                    addTrustedRootCertificateRequestMessagePayload.ProtocolId = 0x01; // IM Protocol Messages
                    addTrustedRootCertificateRequestMessagePayload.ProtocolOpCode = 0x08; // InvokeRequest

                    var addTrustedRootCerticateRequestMessageFrame = new MessageFrame(addTrustedRootCertificateRequestMessagePayload);
                    addTrustedRootCerticateRequestMessageFrame.MessageFlags |= MessageFlags.S;
                    addTrustedRootCerticateRequestMessageFrame.SecurityFlags = 0x00;
                    addTrustedRootCerticateRequestMessageFrame.SourceNodeID = 0x00;
                    paseExchange.SendAsync(addTrustedRootCerticateRequestMessageFrame).GetAwaiter().GetResult();

                    var addTrustedRootCertificateResponseMessageFrame = paseExchange.WaitForNextMessageAsync().GetAwaiter().GetResult();
                    paseExchange.AcknowledgeMessageAsync(addTrustedRootCerticateRequestMessageFrame.MessageCounter).GetAwaiter().GetResult();

                    paseExchange = paseSession.CreateExchange();
                    var encodedPeerNocCertificate = new MatterTLV();
                    encodedPeerNocCertificate.AddStructure();
                    encodedPeerNocCertificate.AddOctetString(1, peerNoc.SerialNumber.ToByteArrayUnsigned()); // SerialNumber
                    encodedPeerNocCertificate.AddUInt8(2, 1); // signature-algorithm
                    encodedPeerNocCertificate.AddList(3); // Issuer
                    encodedPeerNocCertificate.AddUInt64(20, _fabric.RootCACertificateId.ToByteArrayUnsigned());
                    encodedPeerNocCertificate.EndContainer(); // Close List
                    notBefore = new DateTimeOffset(peerNoc.NotBefore).ToEpochTime();
                    notAfter = new DateTimeOffset(peerNoc.NotAfter).ToEpochTime();
                    encodedPeerNocCertificate.AddUInt32(4, (uint)notBefore); // NotBefore
                    encodedPeerNocCertificate.AddUInt32(5, (uint)notAfter); // NotAfter
                    encodedPeerNocCertificate.AddList(6); // Subject
                    encodedPeerNocCertificate.AddUInt64(17,  Encoding.UTF8.GetBytes(e.Device.Id)); // NodeId
                    encodedPeerNocCertificate.AddUInt64(21, _fabric.FabricId.ToByteArrayUnsigned()); // FabricId
                    encodedPeerNocCertificate.EndContainer(); // Close List
                    encodedPeerNocCertificate.AddUInt8(7, 1); // public-key-algorithm
                    encodedPeerNocCertificate.AddUInt8(8, 1); // elliptic-curve-id
                    encodedPeerNocCertificate.AddOctetString(9, peerNocPublicKeyBytes); // PublicKey
                    encodedPeerNocCertificate.AddList(10); // Extensions
                    encodedPeerNocCertificate.AddStructure(1); // Basic Constraints
                    encodedPeerNocCertificate.AddBool(1, false); // is-ca
                    encodedPeerNocCertificate.EndContainer(); // Close Basic Constraints
                    encodedPeerNocCertificate.AddUInt8(2, 0x1);
                    encodedPeerNocCertificate.AddArray(3); // Extended Key Usage
                    encodedPeerNocCertificate.AddUInt8(0x02);
                    encodedPeerNocCertificate.AddUInt8(0x01);
                    encodedPeerNocCertificate.EndContainer();
                    encodedPeerNocCertificate.AddOctetString(4, peerNocKeyIdentifier); // subject-key-id
                    encodedPeerNocCertificate.AddOctetString(5, _fabric.RootKeyIdentifier); // authority-key-id
                    encodedPeerNocCertificate.EndContainer(); // Close Extensions
                    var peerNocSignature = peerNoc.GetSignature();
                    AsnDecoder.ReadSequence(peerNocSignature.AsSpan(), AsnEncodingRules.DER, out offset, out length, out _);
                    source = peerNocSignature.AsSpan().Slice(offset, length).ToArray();
                    r = AsnDecoder.ReadInteger(source, AsnEncodingRules.DER, out bytesConsumed);
                    s = AsnDecoder.ReadInteger(source.AsSpan().Slice(bytesConsumed), AsnEncodingRules.DER, out bytesConsumed);
                    var encodedPeerNocCertificateSignature = r.ToByteArray(isUnsigned: true, isBigEndian: true).Concat(s.ToByteArray(isUnsigned: true, isBigEndian: true)).ToArray();
                    encodedPeerNocCertificate.AddOctetString(11, encodedPeerNocCertificateSignature);
                    encodedPeerNocCertificate.EndContainer(); // Close Structure

                    var addNocRequest = new MatterTLV();
                    addNocRequest.AddStructure();
                    addNocRequest.AddBool(0, false);
                    addNocRequest.AddBool(1, false);
                    addNocRequest.AddArray(tagNumber: 2); // InvokeRequests
                    addNocRequest.AddStructure();
                    addNocRequest.AddList(tagNumber: 0); // CommandPath
                    addNocRequest.AddUInt16(tagNumber: 0, 0x00); // Endpoint 0x00
                    addNocRequest.AddUInt32(tagNumber: 1, 0x3E); // ClusterId 0x3E - Node Operational Credentials
                    addNocRequest.AddUInt16(tagNumber: 2, 0x06); // 11.18.6. Command AddNoc
                    addNocRequest.EndContainer();
                    addNocRequest.AddStructure(1); // CommandFields
                    addNocRequest.AddOctetString(0, encodedPeerNocCertificate.GetBytes()); // NOCValue
                    addNocRequest.AddOctetString(2, _fabric.IPK); // IPKValue
                    addNocRequest.AddUInt64(3, _fabric.RootNodeId.ToByteArrayUnsigned()); // CaseAdminSubject - In this case the RootNodeId.
                    addNocRequest.AddUInt16(4, _fabric.AdminVendorId); // AdminVendorId
                    addNocRequest.EndContainer(); // Close the CommandFields
                    addNocRequest.EndContainer(); // Close the structure
                    addNocRequest.EndContainer(); // Close the array
                    addNocRequest.AddUInt8(255, 12); // interactionModelRevision
                    addNocRequest.EndContainer(); // Close the structure

                    var addNocRequestMessagePayload = new MessagePayload(addNocRequest);

                    addNocRequestMessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;

                    // Table 14. Protocol IDs for the Matter Standard Vendor ID
                    addNocRequestMessagePayload.ProtocolId = 0x01; // IM Protocol Messages
                    addNocRequestMessagePayload.ProtocolOpCode = 0x08; // InvokeRequest

                    var addNocRequestMessageFrame = new MessageFrame(addNocRequestMessagePayload);
                    addNocRequestMessageFrame.MessageFlags |= MessageFlags.S;
                    addNocRequestMessageFrame.SecurityFlags = 0x00;
                    addNocRequestMessageFrame.SourceNodeID = 0x00;
                    paseExchange.SendAsync(addNocRequestMessageFrame).GetAwaiter().GetResult();

                    var addNocResponseMessageFrame = paseExchange.WaitForNextMessageAsync().GetAwaiter().GetResult();
                    paseExchange.AcknowledgeMessageAsync(addNocResponseMessageFrame.MessageCounter).GetAwaiter().GetResult();
                    paseExchange.Close();

                    var caseExchange = unsecureSession.CreateExchange();
                    var caseClient = new CASEClient(new Node() { NodeId = BigInteger.ValueOf(int.Parse(e.Device.Id)) }, _fabric, unsecureSession);
                    var caseSession = caseClient.EstablishSessionAsync().GetAwaiter().GetResult();
                    caseExchange.Close();

                    caseExchange = caseSession.CreateExchange();
                    var commissioningCompletePayload = new MatterTLV();
                    commissioningCompletePayload.AddStructure();
                    commissioningCompletePayload.AddBool(0, false);
                    commissioningCompletePayload.AddBool(1, false);
                    commissioningCompletePayload.AddArray(tagNumber: 2); // InvokeRequests
                    commissioningCompletePayload.AddStructure();
                    commissioningCompletePayload.AddList(tagNumber: 0); // CommandPath
                    commissioningCompletePayload.AddUInt16(tagNumber: 0, 0x00); // Endpoint 0x00
                    commissioningCompletePayload.AddUInt32(tagNumber: 1, 0x30); // ClusterId 0x30 - General Commissioning
                    commissioningCompletePayload.AddUInt16(tagNumber: 2, 0x04); // 11.18.6. Command CompleteCommissioning
                    commissioningCompletePayload.EndContainer();
                    commissioningCompletePayload.AddStructure(1); // CommandFields
                    commissioningCompletePayload.EndContainer(); // Close the CommandFields
                    commissioningCompletePayload.EndContainer(); // Close the structure
                    commissioningCompletePayload.EndContainer(); // Close the array
                    commissioningCompletePayload.AddUInt8(255, 12); // interactionModelRevision
                    commissioningCompletePayload.EndContainer(); // Close the structure

                    var commissioningCompleteMessagePayload = new MessagePayload(commissioningCompletePayload);
                    commissioningCompleteMessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
                    commissioningCompleteMessagePayload.ProtocolId = 0x01; // IM Protocol Messages
                    commissioningCompleteMessagePayload.ProtocolOpCode = 0x08; // InvokeRequest

                    var commissioningCompleteMessageFrame = new MessageFrame(commissioningCompleteMessagePayload);
                    commissioningCompleteMessageFrame.MessageFlags |= MessageFlags.S;
                    commissioningCompleteMessageFrame.SecurityFlags = 0x00;
                    commissioningCompleteMessageFrame.SourceNodeID = BitConverter.ToUInt64(_fabric.RootNodeId.ToByteArrayUnsigned());
                    commissioningCompleteMessageFrame.DestinationNodeId = BitConverter.ToUInt64(Encoding.UTF8.GetBytes(e.Device.Id));
                    caseExchange.SendAsync(commissioningCompleteMessageFrame).GetAwaiter().GetResult();

                    var commissioningCompleteResponseMessageFrame = caseExchange.WaitForNextMessageAsync().GetAwaiter().GetResult();
                    caseExchange.AcknowledgeMessageAsync(commissioningCompleteResponseMessageFrame.MessageCounter).GetAwaiter().GetResult();

                    Console.WriteLine("Commissioning of Node {0} is complete.", e.Device.Id);
                }
                catch (Exception exp)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: {0}", exp.Message);
                }
            }
        }
    }
}
