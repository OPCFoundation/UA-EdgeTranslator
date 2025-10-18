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
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<string, IBluetoothAdvertisingEvent> _receivedAdvertisments = new();
        private Fabric _fabric;
        private IBluetooth _bluetooth;
        private CommissioningPayload _payload;

        public BluetoothCommissioner(Fabric fabric)
        {
            _fabric = fabric;

#if WINDOWS
            Console.WriteLine("Running on Windows");
            _bluetooth = new BluetoothWindows();
#else
            Console.WriteLine("Running on Linux");
            _bluetooth = new BluetoothLinux();
#endif
            Task.Run(CommissionDiscoveredDevices);
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
            if (e.ServiceData().ContainsKey(BTPConnection.MATTER_UUID))
            {
                // If we got this advertisment already, just ignore it.
                if (_receivedAdvertisments.ContainsKey(e.Device.Id))
                {
                    return;
                }
                else
                {
                    _receivedAdvertisments.TryAdd(e.Device.Id, e);
                }
            }
        }

        void CommissionDiscoveredDevices()
        {
            while (true)
            {
                if (_receivedAdvertisments.Count == 0)
                {
                    Task.Delay(1000).GetAwaiter().GetResult();
                    continue;
                }

                try
                {
                    IBluetoothAdvertisingEvent e = _receivedAdvertisments.First().Value;
                    _receivedAdvertisments.Remove(e.Device.Id, out e);

                    var discriminator = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(((ReadOnlySpan<byte>)e.ServiceData()[BTPConnection.MATTER_UUID]).Slice(1, 2)) & 0xFFF);
                    Console.WriteLine("Matter device advertisment received from {0} with a discriminator of {1}", e.Device.Id, discriminator);

                    if (discriminator != _payload.Discriminator)
                    {
                        Console.WriteLine("Discriminator {0} doesn't match expected discriminator {1}", discriminator, _payload.Discriminator);
                        return;
                    }

                    // parse Matter node ID from advertisment
                    string[] parts = e.Device.Id.Split('-');
                    if (parts.Length < 2)
                    {
                        Console.WriteLine("Invalid Bluetooth ID format: " + e.Device.Id);
                        return;
                    }

                    string matterPart = parts[1].Replace(":", "");
                    ulong matterNodeId = Convert.ToUInt64(matterPart, 16);

                    BTPConnection btpConnection = new(e.Device);
                    btpConnection.OpenConnection();
                    UnsecureSession unsecureSession = new(btpConnection);

                    Console.WriteLine("BTPSession has been established. Starting PASE Exchange...");

                    MessageExchange unsecureExchange = unsecureSession.CreateExchange();
                    ushort initiatorSessionId = BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(16));

                    // Password-Based Key Derivation Function param request
                    MatterTLV PBKDFParamRequest = new();
                    PBKDFParamRequest.AddStructure();
                    PBKDFParamRequest.AddOctetString(1, RandomNumberGenerator.GetBytes(32));
                    PBKDFParamRequest.AddUInt16(2, initiatorSessionId);
                    PBKDFParamRequest.AddUInt16(3, 0);
                    PBKDFParamRequest.AddBool(4, false);
                    PBKDFParamRequest.EndContainer();
                    MessageFrame responseMessageFrame = SendAndReceiveMessageAsync(unsecureExchange, PBKDFParamRequest, 0, 0x20).GetAwaiter().GetResult();
                    if (MessageFrame.IsStatusReport(responseMessageFrame))
                    {
                        Console.WriteLine("Received status report in response to PBKDF param request message, abandoning commissioning!");
                        return;
                    }

                    // Simple Password Authenticated Key Exchange (SPAKE)
                    MatterTLV PBKDFParamResponse = responseMessageFrame.MessagePayload.ApplicationPayload;
                    PBKDFParamResponse.OpenStructure();
                    var initiatorRandomBytes2 = PBKDFParamResponse.GetOctetString(1);
                    var responderRandomBytes = PBKDFParamResponse.GetOctetString(2);
                    var peerSessionId = PBKDFParamResponse.GetUnsignedInt16(3);

                    PBKDFParamResponse.OpenStructure(4);
                    var iterations = PBKDFParamResponse.GetUnsignedInt16(1);
                    var salt = PBKDFParamResponse.GetOctetString(2);
                    PBKDFParamResponse.CloseContainer();

                    var spakeContext = Encoding.ASCII.GetBytes("CHIP PAKE V1 Commissioning");
                    var contextToHash = new List<byte>();
                    contextToHash.AddRange(spakeContext);
                    contextToHash.AddRange(PBKDFParamRequest.GetBytes());
                    contextToHash.AddRange(PBKDFParamResponse.GetBytes());
                    var sessionContextHash = SHA256.HashData(contextToHash.ToArray());

                    var pake1 = new MatterTLV();
                    pake1.AddStructure();
                    var (w0, w1, x, X) = CryptographyMethods.Crypto_PAKEValues_Initiator(_payload.Passcode, iterations, salt);
                    var byteString = X.GetEncoded(false).ToArray();
                    pake1.AddOctetString(1, byteString);
                    pake1.EndContainer();
                    MessageFrame pake2MessageFrame = SendAndReceiveMessageAsync(unsecureExchange, pake1, 0, 0x22).GetAwaiter().GetResult();

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
                    MessageFrame pakeFinishedMessageFrame = SendAndReceiveMessageAsync(unsecureExchange, pake3, 0, 0x24).GetAwaiter().GetResult();

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

                    var paseSession = new PaseSecureSession(btpConnection, initiatorSessionId, peerSessionId, encryptKey, decryptKey);
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
                    SendAndReceiveMessageAsync(paseExchange, armFailsafeRequest, 1, 0x09).GetAwaiter().GetResult();

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
                    MessageFrame csrResponseMessageFrame = SendAndReceiveMessageAsync(paseExchange, csrRequest, 1, 0x08).GetAwaiter().GetResult();

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
                    subjectValues.Add(matterPart);
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
                    MessageFrame addTrustedRootCertificateResponseMessageFrame = SendAndReceiveMessageAsync(paseExchange, addTrustedRootCertificateRequest, 1, 0x08).GetAwaiter().GetResult();
                    paseExchange.AcknowledgeMessageAsync(addTrustedRootCertificateResponseMessageFrame.MessageCounter).GetAwaiter().GetResult();

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
                    encodedPeerNocCertificate.AddUInt64(17, matterNodeId); // NodeId
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
                    MessageFrame addNocResponseMessageFrame = SendAndReceiveMessageAsync(paseExchange, addNocRequest, 1, 0x08).GetAwaiter().GetResult();

                    paseExchange.AcknowledgeMessageAsync(addNocResponseMessageFrame.MessageCounter).GetAwaiter().GetResult();
                    paseExchange.Close();

                    var caseSession = new CaseSecureSession(
                        btpConnection,
                        BitConverter.ToUInt64(_fabric.RootNodeId.ToByteArrayUnsigned()),
                        matterNodeId,
                        initiatorSessionId,
                        peerSessionId,
                        encryptKey,
                        decryptKey
                    );
                    MessageExchange caseExchange = caseSession.CreateExchange();

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

                    //commissioningCompleteMessageFrame.SourceNodeID = BitConverter.ToUInt64(_fabric.RootNodeId.ToByteArrayUnsigned());
                    //commissioningCompleteMessageFrame.DestinationNodeId = matterNodeId);

                    MessageFrame commissioningCompleteResponseMessageFrame = SendAndReceiveMessageAsync(caseExchange, commissioningCompletePayload, 1, 0x08).GetAwaiter().GetResult();
                    caseExchange.AcknowledgeMessageAsync(commissioningCompleteResponseMessageFrame.MessageCounter).GetAwaiter().GetResult();

                    Console.WriteLine("Commissioning of Matter Device {0} is complete.", matterNodeId);
                }
                catch (Exception exp)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: {0}", exp.Message);
                }
            }
        }

        private async Task<MessageFrame> SendAndReceiveMessageAsync(MessageExchange exchange, MatterTLV payload, byte protocolId, byte opCode)
        {
            MessagePayload messagePayload = new(payload);
            messagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
            messagePayload.ProtocolId = protocolId;
            messagePayload.ProtocolOpCode = opCode;

            MessageFrame messageFrame = new(messagePayload);
            messageFrame.MessageFlags |= MessageFlags.S;
            messageFrame.SessionID = 0;
            messageFrame.SecurityFlags = 0;
            messageFrame.SourceNodeID = 0;

            await exchange.SendAsync(messageFrame).ConfigureAwait(false);
            return await exchange.WaitForNextMessageAsync().ConfigureAwait(false);
        }
    }
}
