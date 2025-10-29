using Makaretu.Dns;
using Matter.Core;
using Opc.Ua.Edge.Translator.Interfaces;
using Opc.Ua.Edge.Translator.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    public class MatterController : IAsset
    {
        private readonly FabricDiskStorage _storageProvider = new();
        private readonly MulticastService _mDNSService = new();
        public readonly Fabric _fabric;
        private readonly ServiceDiscovery _serviceDiscovery;
        private readonly BluetoothCommissioner _commissioner;

        public MatterController()
        {
            // Load Fabric from storage, if it exists
            if (_storageProvider.FabricExists())
            {
                _fabric = _storageProvider.LoadFabric();
            }

            if (_fabric == null)
            {
                // create a new fabric
                _fabric = new Fabric();
                _storageProvider.SaveFabric(_fabric);
            }

            _mDNSService.NetworkInterfaceDiscovered += (s, e) =>
            {
                _mDNSService.SendQuery("_matter._tcp.local");
            };

            _mDNSService.AnswerReceived += _mDNSService_AnswerReceived;

            _serviceDiscovery = new ServiceDiscovery(_mDNSService);
            _serviceDiscovery.ServiceDiscovered += _serviceDiscovery_ServiceDiscovered;
            _serviceDiscovery.ServiceInstanceDiscovered += _serviceDiscovery_ServiceInstanceDiscovered;

            _commissioner = new BluetoothCommissioner(_fabric);

            _mDNSService.Start();
        }

        public void Connect(string ipAddress, int port)
        {
            string[] ipParts = ipAddress.Split(['/']);

            try
            {
                CommissioningPayload commissioningPayload = ParseManualSetupCode(ipParts[2], ipParts[3]);

                // check if the node is already commissioned into our Fabric
                if (!_fabric.Nodes.Values.Any(n => n.SetupCode == commissioningPayload.Passcode.ToString() && n.Discriminator == commissioningPayload.Discriminator.ToString()))
                {
                    Console.WriteLine($"Matter Node '{commissioningPayload.Passcode}' is not commissioned. Starting commissioning process.");

                    _commissioner.StartBluetoothDiscovery(commissioningPayload).GetAwaiter().GetResult();

                    // wait 60 seconds or until we have an IP address for the node
                    uint numRetries = 60;
                    while ((numRetries > 0) && !_fabric.Nodes.Values.Any(n => n.SetupCode == commissioningPayload.Passcode.ToString() && n.Discriminator == commissioningPayload.Discriminator.ToString() && n.LastKnownIpAddress != null))
                    {
                        Task.Delay(1000).GetAwaiter().GetResult();
                        numRetries--;
                    }

                    // persist the entire fabric
                    if (numRetries > 0)
                    {
                        _storageProvider.SaveFabric(_fabric);
                    }
                    else
                    {
                        Console.WriteLine("Commissioning process timed out waiting for IP address.");
                    }
                }

                Matter.Core.Node node = _fabric.Nodes.Values.FirstOrDefault(n => n.SetupCode == commissioningPayload.Passcode.ToString() && n.Discriminator == commissioningPayload.Discriminator.ToString() && n.LastKnownIpAddress != null);
                if ((node != null) && !node.IsConnected)
                {
                    node.Connect(_fabric);
                    node.FetchDescriptionsAsync().GetAwaiter().GetResult();
                }

            }
            catch (Exception ex)
            {
                throw new Exception("Failed to connect to Matter device: " + ex.Message);
            }
        }

        public List<string> Discover()
        {
            return _fabric.Nodes.Select(n => n.Key.ToString()).ToList();
        }

        public ThingDescription BrowseAndGenerateTD(string name, string endpoint)
        {
            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + name,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "Thing" },
                Name = name,
                Base = endpoint,
                Title = name,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            return td;
        }

        public void Disconnect()
        {
            // nothing to do
        }

        public string GetRemoteEndpoint()
        {
            return string.Empty;
        }

        public object Read(AssetTag tag)
        {
            object value = null;

            string[] addressParts = tag.Address.Split(['?', '/']);

            // TODO: Implement the read logic from the Matter device

            return value;
        }

        public void Write(AssetTag tag, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "Float")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "Boolean")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "Integer")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by LoRaWAN.");
            }

            // TODO: Implement the write logic to the Matter device
        }

        public string ExecuteAction(MethodState method, string[] inputArgs, ref string[] outputArgs)
        {
            throw new NotImplementedException();
        }

        private CommissioningPayload ParseManualSetupCode(string hexDataset, string manualSetupCode)
        {
            byte[] data = Decode(manualSetupCode);
            ushort discriminator = (ushort)readBits(data, 45, 12);
            uint passcode = readBits(data, 57, 27);
            uint padding = readBits(data, 84, 4);

            if (padding != 0)
            {
                throw new ArgumentException("Invalid QR Code");
            }

            byte[] bytes = Array.Empty<byte>();
            try
            {
                if (hexDataset.Length % 2 != 0)
                {
                    throw new ArgumentException("Hex string must have an even length.");
                }

                bytes = new byte[hexDataset.Length / 2];
                for (int i = 0; i < hexDataset.Length; i += 2)
                {
                    bytes[i / 2] = Convert.ToByte(hexDataset.Substring(i, 2), 16);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error converting hex string to byte array: " + ex.Message);
            }

            return new CommissioningPayload()
            {
                Discriminator = discriminator,
                Passcode = passcode,
                ThreadDataset = bytes
            };
        }

        private static byte[] Decode(string str)
        {
            List<byte> data = new List<byte>();

            for (int i = 0; i < str.Length; i += 5)
            {
                data.AddRange(Unpack(str.Substring(i, Math.Min(5, str.Length - i))));
            }

            return data.ToArray();
        }

        private static byte[] Unpack(string str)
        {
            uint digit = DecodeBase38(str);

            if (str.Length == 5)
            {
                byte[] result = new byte[3];
                result[0] = (byte)digit;
                result[1] = (byte)(digit >> 8);
                result[2] = (byte)(digit >> 16);
                return result;
            }
            else if (str.Length == 4)
            {
                return [(byte)digit, (byte)(digit >> 8)];
            }
            else if (str.Length == 2)
            {
                return [(byte)(digit & 0xFF)];
            }
            else
            {
                throw new ArgumentException("Invalid QR String");
            }
        }

        private static uint DecodeBase38(string sIn)
        {
            const string map = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-.";
            uint ret = 0;

            for (int i = sIn.Length - 1; i >= 0; i--)
            {
                ret = (uint)(ret * 38 + map.IndexOf(sIn[i]));
            }

            return ret;
        }


        private static uint readBits(byte[] buf, int index, int numberOfBitsToRead)
        {
            uint dest = 0;

            int currentIndex = index;
            for (int bitsRead = 0; bitsRead < numberOfBitsToRead; bitsRead++)
            {
                if ((buf[currentIndex / 8] & 1 << currentIndex % 8) != 0)
                {
                    dest |= (uint)(1 << bitsRead);
                }

                currentIndex++;
            }

            return dest;
        }

        private void _mDNSService_AnswerReceived(object sender, MessageEventArgs e)
        {
            var servers = e.Message.Answers.OfType<SRVRecord>();

            foreach (var server in servers)
            {
                var instanceName = server.Name.ToString();

                if (instanceName.EndsWith("_matter._tcp.local"))
                {
                    Console.WriteLine($"Discovered Commissioned Node '{instanceName}'");

                    var addresses = e.Message.AdditionalRecords.OfType<AddressRecord>();

                    if (!addresses.Any())
                    {
                        Console.WriteLine("No IP address received from Matter device: " + instanceName);
                        continue;
                    }

                    string[] parts = instanceName.Replace("._matter._tcp.local", "").Split('-');

                    if (parts.Length < 2)
                    {
                        Console.WriteLine("Invalid Matter ID format: " + instanceName);
                        continue;
                    }

                    _fabric.AddOrUpdateNode(parts[1], null, null, addresses.FirstOrDefault()?.Address.ToString(), server.Port);
                }
            }
        }

        private void _serviceDiscovery_ServiceInstanceDiscovered(object sender, ServiceInstanceDiscoveryEventArgs e)
        {
            _mDNSService.SendQuery(e.ServiceInstanceName, type: DnsType.SRV);
        }

        private void _serviceDiscovery_ServiceDiscovered(object sender, DomainName serviceName)
        {
            _mDNSService.SendQuery(serviceName, type: DnsType.PTR);
        }
    }
}
