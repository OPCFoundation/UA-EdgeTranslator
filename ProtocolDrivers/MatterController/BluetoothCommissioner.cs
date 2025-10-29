using InTheHand.Bluetooth;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Matter.Core
{
    public class BluetoothCommissioner
    {
        private readonly ConcurrentDictionary<string, IBluetoothAdvertisingEvent> _receivedAdvertisments = new();
        private readonly Fabric _fabric;
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

                    string nodeIdString = parts[1].Replace(":", "");
                    ulong nodeId = Convert.ToUInt64(nodeIdString, 16);

                    BTPConnection btpConnection = new(e.Device);
                    btpConnection.OpenConnection();

                    Console.WriteLine("BTP connection established. Starting PAKE/PASE exchange...");
                    if (!ExecutePAKE(btpConnection, out ushort initiatorSessionId, out ushort peerSessionId, out byte[] Z))
                    {
                        return;
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

                    var paseSession = new SecureSession(btpConnection, initiatorSessionId, peerSessionId, encryptKey, decryptKey);

                    var paseExchange = paseSession.CreateExchange();

                    object[] parameters = [
                        (ushort)10, // 10 seconds expiration
                        (ulong)2222 // Breadcrumb
                    ];
                    MessageFrame armFailsafeMessageFrame = paseExchange.SendCommand(0, 0x30, 0, 8, parameters).GetAwaiter().GetResult(); // Arm Failsafe
                    if (MessageFrame.IsStatusReport(armFailsafeMessageFrame))
                    {
                        Console.WriteLine("Received error status report in response to Arm Failsafe message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV armFailsafeResultPayload = SkipHeader(armFailsafeMessageFrame.MessagePayload.ApplicationPayload);
                    armFailsafeResultPayload.OpenStructure(1);
                    byte status = armFailsafeResultPayload.GetUnsignedInt8(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"ArmFailsafe failed with status {status}");
                        return;
                    }

                    parameters = [
                        RandomNumberGenerator.GetBytes(32) // CSRNonce
                    ];
                    MessageFrame csrResponseMessageFrame = paseExchange.SendCommand(0, 0x3E, 4, 8, parameters).GetAwaiter().GetResult(); // CSRRequest
                    if (MessageFrame.IsStatusReport(csrResponseMessageFrame))
                    {
                        Console.WriteLine("Received error status report in response to CSRRequest message, abandoning commissioning!");
                        return;
                    }

                    var csrResponsePayload = SkipHeader(csrResponseMessageFrame.MessagePayload.ApplicationPayload);
                    csrResponsePayload.OpenStructure(1);
                    var csrBytes = new MatterTLV(csrResponsePayload.GetOctetString(0));
                    csrBytes.OpenStructure();
                    CertificateRequest certRequest = CertificateRequest.LoadSigningRequest(csrBytes.GetOctetString(1), HashAlgorithmName.SHA256);

                    parameters = [
                        _fabric.CA.GenerateCertMessage(_fabric.CA.RootCertificate)
                    ];
                    MessageFrame addRootCertMessageFrame = paseExchange.SendCommand(0, 0x3E, 11, 8, parameters).GetAwaiter().GetResult(); // AddTrustedRootCertificate
                    if (MessageFrame.IsStatusReport(addRootCertMessageFrame))
                    {
                        Console.WriteLine("Received error status report in response to AddTrustedRootCertificate message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV addRootCertResultPayload = SkipHeader(addRootCertMessageFrame.MessagePayload.ApplicationPayload);
                    addRootCertResultPayload.OpenStructure(1);
                    status = addRootCertResultPayload.GetUnsignedInt8(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"AddRootCert failed with status {status}");
                        return;
                    }


                    X509Certificate2 nodeCert = _fabric.CA.SignCertRequest(certRequest, nodeId, _fabric.FabricId);
                    parameters = [
                        _fabric.CA.GenerateCertMessage(nodeCert),
                        null,
                        _fabric.IPK,
                        _fabric.FabricId,
                        _fabric.VendorId
                    ];
                    MessageFrame addNocResult = paseExchange.SendCommand(0, 0x3E, 6, 8, parameters).GetAwaiter().GetResult(); // AddNoc
                    if (MessageFrame.IsStatusReport(addNocResult))
                    {
                        Console.WriteLine("Received error status report in response to AddNoc message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV addNocResultPayload = SkipHeader(addNocResult.MessagePayload.ApplicationPayload);
                    addNocResultPayload.OpenStructure(1);
                    status = addNocResultPayload.GetUnsignedInt8(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"AddNoc failed with status {status}");
                        return;
                    }

                    parameters = [
                        null,
                        (ulong)2222 // Breadcrumb
                    ];
                    MessageFrame scanResult = paseExchange.SendCommand(0, 0x31, 0, 8, parameters).GetAwaiter().GetResult(); // ScanNetworks
                    if (MessageFrame.IsStatusReport(scanResult))
                    {
                        Console.WriteLine("Received error status report in response to ScanNetworks message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV scanResultPayload = SkipHeader(scanResult.MessagePayload.ApplicationPayload);
                    scanResultPayload.OpenStructure(1);
                    status = scanResultPayload.GetUnsignedInt8(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"ScanNetworks failed with status {status}");
                        return;
                    }

                    scanResultPayload.OpenArray(3);
                    scanResultPayload.OpenStructure();
                    ushort panId = scanResultPayload.GetUnsignedInt16(0);
                    ulong extendedPanId = scanResultPayload.GetUnsignedInt64(1);
                    string networkName = scanResultPayload.GetUTF8String(2);
                    byte channel = scanResultPayload.GetUnsignedInt8(3);
                    byte version = scanResultPayload.GetUnsignedInt8(4);
                    byte[] extendedAddress = scanResultPayload.GetOctetString(5);
                    sbyte rssi = scanResultPayload.GetSignedInt8(6);
                    byte lqi = scanResultPayload.GetUnsignedInt8(7);

                    Console.WriteLine("Thread Network Scan Result from Device: ExtendedPANID={0:X16}, NetworkName={1}, ExtendedAddress={2}", extendedPanId, networkName, BitConverter.ToString(extendedAddress).Replace("-", ":"));

                    parameters = [
                        _payload.ThreadDataset,
                        (ulong)2222 // Breadcrumb
                    ];
                    MessageFrame addNetworkResult = paseExchange.SendCommand(0, 0x31, 3, 8, parameters).GetAwaiter().GetResult(); // AddOrUpdateNetwork
                    if (MessageFrame.IsStatusReport(addNetworkResult))
                    {
                        Console.WriteLine("Received error status report in response to AddOrUpdateNetwork message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV addNetworkResultPayload = SkipHeader(addNetworkResult.MessagePayload.ApplicationPayload);
                    addNetworkResultPayload.OpenStructure(1);
                    status = addNetworkResultPayload.GetUnsignedInt8(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"AddOrUpdateNetwork failed with status {status}");
                        return;
                    }

                    parameters = [
                        BitConverter.GetBytes(extendedPanId).Reverse().ToArray(),
                        (ulong)2222 // Breadcrumb
                    ];
                    MessageFrame connectNetworkResult = paseExchange.SendCommand(0, 0x31, 6, 8, parameters).GetAwaiter().GetResult(); // ConnectNetwork
                    if (MessageFrame.IsStatusReport(connectNetworkResult))
                    {
                        Console.WriteLine("Received error status report in response to ConnectNetwork message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV connectNetworkResultPayload = SkipHeader(connectNetworkResult.MessagePayload.ApplicationPayload);
                    connectNetworkResultPayload.OpenStructure(1);
                    status = connectNetworkResultPayload.GetUnsignedInt8(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"ConnectNetwork failed with status {status}");
                        return;
                    }

                    MessageFrame completeCommissioningResult = paseExchange.SendCommand(0, 0x30, 4, 8).GetAwaiter().GetResult(); // CompleteCommissioning
                    if (MessageFrame.IsStatusReport(completeCommissioningResult))
                    {
                        Console.WriteLine("Received error status report in response to CompleteCommissioning message, abandoning commissioning!");
                        return;
                    }

                    paseExchange.AcknowledgeMessageAsync(completeCommissioningResult.MessageCounter).GetAwaiter().GetResult();
                    paseExchange.Close();

                    btpConnection.Close();

                    _receivedAdvertisments.Remove(e.Device.Id, out e);

                    _fabric.AddOrUpdateNode(nodeIdString, _payload.Passcode.ToString(), _payload.Discriminator.ToString(), null, 0);

                    Console.WriteLine("Commissioning of Matter Device {0} is complete.", nodeIdString);
                }
                catch (Exception exp)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: {0}", exp.Message);
                }
            }
        }

        private bool ExecutePAKE(BTPConnection btpConnection, out ushort initiatorSessionId, out ushort peerSessionId, out byte[] Ke)
            {
            peerSessionId = 0;
            Ke = null;

            UnsecureSession unsecureSession = new(btpConnection);
            MessageExchange unsecureExchange = unsecureSession.CreateExchange();
            initiatorSessionId = BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(16));

            // Password-Based Key Derivation Function param request
            MatterTLV PBKDFParamRequest = new();
            PBKDFParamRequest.AddStructure();
            PBKDFParamRequest.AddOctetString(1, RandomNumberGenerator.GetBytes(32));
            PBKDFParamRequest.AddUInt16(2, initiatorSessionId);
            PBKDFParamRequest.AddUInt16(3, 0);
            PBKDFParamRequest.AddBool(4, false);
            PBKDFParamRequest.EndContainer();
            MessageFrame responseMessageFrame = unsecureExchange.SendAndReceiveMessageAsync(PBKDFParamRequest, 0, 0x20).GetAwaiter().GetResult();
            if (MessageFrame.IsStatusReport(responseMessageFrame))
            {
                Console.WriteLine("Received error status report in response to PBKDF param request message, abandoning commissioning!");
                return false;
            }

            // Simple Password Authenticated Key Exchange (SPAKE)
            MatterTLV PBKDFParamResponse = responseMessageFrame.MessagePayload.ApplicationPayload;
            PBKDFParamResponse.OpenStructure();
            var initiatorRandomBytes2 = PBKDFParamResponse.GetOctetString(1);
            var responderRandomBytes = PBKDFParamResponse.GetOctetString(2);
            peerSessionId = PBKDFParamResponse.GetUnsignedInt16(3);
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
            MessageFrame pake2MessageFrame = unsecureExchange.SendAndReceiveMessageAsync(pake1, 0, 0x22).GetAwaiter().GetResult();
            if (MessageFrame.IsStatusReport(pake2MessageFrame))
            {
                Console.WriteLine("Received error status report in response to PAKE1 message, abandoning commissioning!");
                return false;
            }

            var pake2 = pake2MessageFrame.MessagePayload.ApplicationPayload;
            pake2.OpenStructure();
            var Y = pake2.GetOctetString(1);
            var Verifier = pake2.GetOctetString(2);
            pake2.CloseContainer();

            var (Kee, hAY, hBX) = CryptographyMethods.Crypto_P2(sessionContextHash, w0, w1, x, X, Y);
            if (!hBX.SequenceEqual(Verifier))
            {
                throw new Exception("Verifier doesn't match!");
            }

            Ke = Kee;
            var pake3 = new MatterTLV();
            pake3.AddStructure();
            pake3.AddOctetString(1, hAY);
            pake3.EndContainer();
            MessageFrame pakeFinishedMessageFrame = unsecureExchange.SendAndReceiveMessageAsync(pake3, 0, 0x24).GetAwaiter().GetResult();
            if (!MessageFrame.IsStatusReport(pakeFinishedMessageFrame))
            {
                Console.WriteLine("Didn't receive status report in response to PAKE3 message, abandoning commissioning!");
                return false;
            }

            unsecureExchange.AcknowledgeMessageAsync(pakeFinishedMessageFrame.MessageCounter).GetAwaiter().GetResult();
            unsecureExchange.Close();

            return true;
        }

        private MatterTLV SkipHeader(MatterTLV data)
        {
            data.OpenStructure();
            data.GetBoolean(0);
            data.OpenArray(1);
            data.OpenStructure();
            data.OpenStructure(null);
            data.OpenList(0);
            data.GetUnsignedInt8(0);
            data.GetUnsignedInt8(1);
            data.GetUnsignedInt8(2);
            data.CloseContainer();

            return data;
        }
    }
}
