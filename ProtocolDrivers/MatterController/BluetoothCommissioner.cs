using InTheHand.Bluetooth;
using Org.BouncyCastle.Pkcs;
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

            Console.WriteLine("Waiting 45s for Debugger to be connected...");
            Task.Delay(45000).GetAwaiter().GetResult();

            _bluetooth.AdvertisementReceived += Bluetooth_AdvertisementReceived;

            // scan for 15 seconds
            await _bluetooth.StartLEScanAsync().ConfigureAwait(false);
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
                    IBluetoothAdvertisingEvent e = null;
                    foreach (var adv in _receivedAdvertisments)
                    {
                        if (adv.Value != null)
                        {
                            Console.WriteLine("Discovered Matter device: " + adv.Key);

                            if (adv.Value.ServiceData()[BTPConnection.MATTER_UUID].Length > 0)
                            {
                                var discriminator = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(((ReadOnlySpan<byte>)adv.Value.ServiceData()[BTPConnection.MATTER_UUID]).Slice(1, 2)) & 0xFFF);
                                if (discriminator == _payload.Discriminator)
                                {
                                    e = adv.Value;
                                    break;
                                }
                            }
                            else
                            {
                                if (adv.Key.Contains(_payload.Discriminator.ToString()))
                                {
                                    e = adv.Value;
                                    break;
                                }
                            }
                        }
                    }

                    if (e == null)
                    {
                        // our Matter device was not found yet
                        Task.Delay(1000).GetAwaiter().GetResult();
                        continue;
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

                    // Generate session keys and establish secure session
                    byte[] keys = _fabric.CA.KeyDerivationFunctionHMACSHA256(Z, Array.Empty<byte>(), Encoding.ASCII.GetBytes("SessionKeys"), 32);
                    var encryptKey = keys.AsSpan().Slice(0, 16).ToArray();
                    var decryptKey = keys.AsSpan().Slice(16, 16).ToArray();
                    var paseSession = new SecureSession(btpConnection, initiatorSessionId, peerSessionId, encryptKey, decryptKey);

                    var paseExchange = paseSession.CreateExchange(0, 0);

                    object[] parameters = [
                        (ushort)60, // 60 seconds expiration
                        (ulong)2222 // Breadcrumb
                    ];
                    MessageFrame armFailsafeMessageFrame = paseExchange.SendCommand(0, 0x30, 0, ProtocolOpCode.InvokeRequest, parameters).GetAwaiter().GetResult(); // Arm Failsafe
                    if (MessageFrame.IsError(armFailsafeMessageFrame))
                    {
                        Console.WriteLine("Received error status in response to Arm Failsafe message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV armFailsafeResultPayload = SkipHeader(armFailsafeMessageFrame.MessagePayload.ApplicationPayload);
                    armFailsafeResultPayload.OpenStructure(1);
                    ulong status = armFailsafeResultPayload.GetUnsignedInt(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"ArmFailsafe failed with status {status}");
                        return;
                    }

                    parameters = [
                        RandomNumberGenerator.GetBytes(32) // CSRNonce
                    ];
                    MessageFrame csrResponseMessageFrame = paseExchange.SendCommand(0, 0x3E, 4, ProtocolOpCode.InvokeRequest, parameters).GetAwaiter().GetResult(); // CSRRequest
                    if (MessageFrame.IsError(csrResponseMessageFrame))
                    {
                        Console.WriteLine("Received error status in response to CSRRequest message, abandoning commissioning!");
                        return;
                    }

                    var csrResponsePayload = SkipHeader(csrResponseMessageFrame.MessagePayload.ApplicationPayload);
                    csrResponsePayload.OpenStructure(1);
                    var innerCsrResponsePayload = new MatterTLV(csrResponsePayload.GetOctetString(0));
                    innerCsrResponsePayload.OpenStructure();
                    byte[] csrBytes = innerCsrResponsePayload.GetOctetString(1);

                    Pkcs10CertificationRequest bcCertRequest = new(csrBytes);
                    CertificateRequest certRequest = _fabric.CA.ConvertCSR(bcCertRequest);
                    if (certRequest == null)
                    {
                        Console.WriteLine("Failed to convert CSR from device, abandoning commissioning!");
                        return;
                    }

                    parameters = [
                        _fabric.CA.GenerateCertMessage(_fabric.CA.RootCertificate)
                    ];
                    MessageFrame addRootCertMessageFrame = paseExchange.SendCommand(0, 0x3E, 11, ProtocolOpCode.InvokeRequest, parameters).GetAwaiter().GetResult(); // AddTrustedRootCertificate
                    if (MessageFrame.IsError(addRootCertMessageFrame))
                    {
                        Console.WriteLine("Received error status in response to AddTrustedRootCertificate message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV addRootCertResultPayload = SkipHeader(addRootCertMessageFrame.MessagePayload.ApplicationPayload);
                    addRootCertResultPayload.OpenStructure(1);
                    status = addRootCertResultPayload.GetUnsignedInt(0);
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
                        _fabric.RootNodeId,
                        _fabric.VendorId
                    ];
                    MessageFrame addNocResult = paseExchange.SendCommand(0, 0x3E, 6, ProtocolOpCode.InvokeRequest, parameters).GetAwaiter().GetResult(); // AddNoc
                    if (MessageFrame.IsError(addNocResult))
                    {
                        Console.WriteLine("Received error status in response to AddNoc message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV addNocResultPayload = SkipHeader(addNocResult.MessagePayload.ApplicationPayload);
                    addNocResultPayload.OpenStructure(1);
                    status = addNocResultPayload.GetUnsignedInt(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"AddNoc failed with status {status}");
                        return;
                    }

                    parameters = [
                        null,
                        (ulong)2222 // Breadcrumb
                    ];
                    MessageFrame scanResult = paseExchange.SendCommand(0, 0x31, 0, ProtocolOpCode.InvokeRequest, parameters).GetAwaiter().GetResult(); // ScanNetworks
                    if (MessageFrame.IsError(scanResult))
                    {
                        Console.WriteLine("Received error status in response to ScanNetworks message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV scanResultPayload = SkipHeader(scanResult.MessagePayload.ApplicationPayload);
                    scanResultPayload.OpenStructure(1);
                    status = scanResultPayload.GetUnsignedInt(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"ScanNetworks failed with status {status}");
                        return;
                    }

                    scanResultPayload.OpenArray(3);
                    scanResultPayload.OpenStructure();
                    ulong panId = scanResultPayload.GetUnsignedInt(0);
                    ulong extendedPanId = scanResultPayload.GetUnsignedInt(1);
                    string networkName = scanResultPayload.GetUTF8String(2);
                    ulong channel = scanResultPayload.GetUnsignedInt(3);
                    ulong version = scanResultPayload.GetUnsignedInt(4);
                    byte[] extendedAddress = scanResultPayload.GetOctetString(5);
                    long rssi = scanResultPayload.GetSignedInt(6);
                    ulong lqi = scanResultPayload.GetUnsignedInt(7);

                    Console.WriteLine("Thread Network Scan Result from Device: ExtendedPANID={0:X16}, NetworkName={1}, ExtendedAddress={2}", extendedPanId, networkName, BitConverter.ToString(extendedAddress).Replace("-", ":"));

                    parameters = [
                        _payload.ThreadDataset,
                        (ulong)2222 // Breadcrumb
                    ];
                    MessageFrame addNetworkResult = paseExchange.SendCommand(0, 0x31, 3, ProtocolOpCode.InvokeRequest, parameters).GetAwaiter().GetResult(); // AddOrUpdateNetwork
                    if (MessageFrame.IsError(addNetworkResult))
                    {
                        Console.WriteLine("Received error status in response to AddOrUpdateNetwork message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV addNetworkResultPayload = SkipHeader(addNetworkResult.MessagePayload.ApplicationPayload);
                    addNetworkResultPayload.OpenStructure(1);
                    status = addNetworkResultPayload.GetUnsignedInt(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"AddOrUpdateNetwork failed with status {status}");
                        return;
                    }

                    parameters = [
                        BitConverter.GetBytes(extendedPanId).Reverse().ToArray(),
                        (ulong)2222 // Breadcrumb
                    ];
                    MessageFrame connectNetworkResult = paseExchange.SendCommand(0, 0x31, 6, ProtocolOpCode.InvokeRequest, parameters).GetAwaiter().GetResult(); // ConnectNetwork
                    if (MessageFrame.IsError(connectNetworkResult))
                    {
                        Console.WriteLine("Received error status in response to ConnectNetwork message, abandoning commissioning!");
                        return;
                    }

                    MatterTLV connectNetworkResultPayload = SkipHeader(connectNetworkResult.MessagePayload.ApplicationPayload);
                    connectNetworkResultPayload.OpenStructure(1);
                    status = connectNetworkResultPayload.GetUnsignedInt(0);
                    if (status != 0)
                    {
                        Console.WriteLine($"ConnectNetwork failed with status {status}");
                        return;
                    }

                    paseExchange.AcknowledgeMessageAsync(connectNetworkResult.MessageCounter).GetAwaiter().GetResult();
                    paseExchange.Close();
                    btpConnection.Close();

                    _fabric.AddNode(
                        _payload.Passcode.ToString(),
                        _payload.Discriminator.ToString(),
                        nodeId,
                        _fabric.CA.GenerateCertMessage(nodeCert),
                        certRequest.PublicKey.GetECDsaPublicKey());

                    // mark this advertisment as processed
                    _receivedAdvertisments.AddOrUpdate(e.Device.Id, (key) => null, (key, oldValue) => null);

                    Console.WriteLine("PAKE/PASE exchange completed successfully.");
                }
                catch (Exception exp)
                {
                    Console.WriteLine("Error: {0}", exp.Message);
                }
            }
        }

        private bool ExecutePAKE(BTPConnection btpConnection, out ushort initiatorSessionId, out ushort peerSessionId, out byte[] Ke)
            {
            peerSessionId = 0;
            Ke = null;

            UnsecureSession unsecureSession = new(btpConnection);
            MessageExchange unsecureExchange = unsecureSession.CreateExchange(0, 0);
            initiatorSessionId = BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(16));

            // Password-Based Key Derivation Function param request
            MatterTLV PBKDFParamRequest = new();
            PBKDFParamRequest.AddStructure();
            PBKDFParamRequest.AddOctetString(1, RandomNumberGenerator.GetBytes(32));
            PBKDFParamRequest.AddUInt16(2, initiatorSessionId);
            PBKDFParamRequest.AddUInt16(3, 0);
            PBKDFParamRequest.AddBool(4, false);
            PBKDFParamRequest.EndContainer();
            MessageFrame responseMessageFrame = unsecureExchange.SendAndReceiveMessageAsync(PBKDFParamRequest, 0, ProtocolOpCode.PBKDFParamRequest).GetAwaiter().GetResult();
            if (MessageFrame.IsError(responseMessageFrame))
            {
                Console.WriteLine("Received error status in response to PBKDF param request message, abandoning commissioning!");
                return false;
            }

            // Simple Password Authenticated Key Exchange (SPAKE)
            MatterTLV PBKDFParamResponse = responseMessageFrame.MessagePayload.ApplicationPayload;
            PBKDFParamResponse.OpenStructure();
            PBKDFParamResponse.GetOctetString(1);
            PBKDFParamResponse.GetOctetString(2);
            peerSessionId = (ushort)PBKDFParamResponse.GetUnsignedInt(3);
            PBKDFParamResponse.OpenStructure(4);
            ushort iterations = (ushort)PBKDFParamResponse.GetUnsignedInt(1);
            var salt = PBKDFParamResponse.GetOctetString(2);
            PBKDFParamResponse.CloseContainer();

            var spakeContext = Encoding.ASCII.GetBytes("CHIP PAKE V1 Commissioning");
            var contextToHash = new List<byte>();
            contextToHash.AddRange(spakeContext);
            contextToHash.AddRange(PBKDFParamRequest.Serialize());
            contextToHash.AddRange(PBKDFParamResponse.Serialize());
            var sessionContextHash = SHA256.HashData(contextToHash.ToArray());

            var pake1 = new MatterTLV();
            pake1.AddStructure();
            var (w0, w1, x, X) = CryptoPAKE.Crypto_PAKEValues_Initiator(_payload.Passcode, iterations, salt);
            var byteString = X.GetEncoded(false).ToArray();
            pake1.AddOctetString(1, byteString);
            pake1.EndContainer();
            MessageFrame pake2MessageFrame = unsecureExchange.SendAndReceiveMessageAsync(pake1, 0, ProtocolOpCode.PASEPake1).GetAwaiter().GetResult();
            if (MessageFrame.IsError(pake2MessageFrame))
            {
                Console.WriteLine("Received error status in response to PAKE1 message, abandoning commissioning!");
                return false;
            }

            var pake2 = pake2MessageFrame.MessagePayload.ApplicationPayload;
            pake2.OpenStructure();
            var Y = pake2.GetOctetString(1);
            var Verifier = pake2.GetOctetString(2);
            pake2.CloseContainer();

            var (Kee, hAY, hBX) = CryptoPAKE.Crypto_P2(sessionContextHash, w0, w1, x, X, Y);
            if (!hBX.SequenceEqual(Verifier))
            {
                throw new Exception("Verifier doesn't match!");
            }

            Ke = Kee;
            var pake3 = new MatterTLV();
            pake3.AddStructure();
            pake3.AddOctetString(1, hAY);
            pake3.EndContainer();
            MessageFrame pakeFinishedMessageFrame = unsecureExchange.SendAndReceiveMessageAsync(pake3, 0, ProtocolOpCode.PASEPake3).GetAwaiter().GetResult();
            if (!MessageFrame.IsError(pakeFinishedMessageFrame))
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
            data.GetUnsignedInt(0);
            data.GetUnsignedInt(1);
            data.GetUnsignedInt(2);
            data.CloseContainer();

            return data;
        }
    }
}
