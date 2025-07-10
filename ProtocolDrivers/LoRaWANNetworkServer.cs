namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using LoRaWan;
    using LoRaWan.NetworkServer;
    using LoRaWan.NetworkServer.BasicsStation;
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using static LoRaWan.NetworkServer.LoRaDevice;

    public class LoRaWANNetworkServer : IAsset
    {
        public LoRaWANNetworkServer()
        {
            _ = Task.Run(() => BasicsStationNetworkServer.RunServerAsync(new CancellationTokenSource().Token));
        }

        public List<string> Discover()
        {
            // LoRaWAN does not support discovery
            return new List<string>();
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

        public void Connect(string ipAddress, int port)
        {
            string[] addressParts = ipAddress.Split('/');
            try
            {
                // register the device with the LoRaWAN Network Server
                var devEui = DevEui.Parse(addressParts[2]);

                if (addressParts[4] == "routerconfig")
                {
                    // parse the router configuration from the WoT Thing Description
                    ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(
                        File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings") + "/" + addressParts[3] + ".jsonld"));

                    string payload = td.Properties["routerConfig"].Forms[0].ToString();
                    SearchDevicesResult.AddDevice(devEui, payload);
                }
                else
                {
                    SearchDevicesResult.AddDevice(devEui, addressParts[3]);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
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
            byte[] tagBytes = null;

            string[] addressParts = tag.Address.Split(['?', '&', '=', '/']);
            if (addressParts.Length == 5)
            {
                if (addressParts[4] == "routerconfig")
                {
                    if (SearchDevicesResult.DeviceList.ContainsKey(addressParts[2].ToUpper()))
                    {
                        // read the router configuration from the stored WoT Thing Description
                        value = SearchDevicesResult.DeviceList[addressParts[2].ToUpper()];
                    }
                }
                else
                {
                    tagBytes = Read(addressParts[0], addressParts[1], addressParts[2], ushort.Parse(addressParts[4]));
                }
            }
            else if (addressParts.Length == 4)
            {
                tagBytes = Read(addressParts[0], addressParts[1], null, ushort.Parse(addressParts[3]));
            }

            if ((tagBytes != null) && !string.IsNullOrEmpty(tag.BitMask))
            {
                byte[] bitMaskBytes = HexToBytes(tag.BitMask);

                if (bitMaskBytes.Length != tagBytes.Length)
                {
                    throw new ArgumentException($"Bitmask length {bitMaskBytes.Length} does not match tag bytes length {tagBytes.Length}.");
                }

                for (int i = 0; i < tagBytes.Length; i++)
                {
                    tagBytes[i] = (byte)(tagBytes[i] & bitMaskBytes[i]);
                }
            }

            if ((tagBytes != null) && tag.IsBigEndian)
            {
                tagBytes = ByteSwapper.Swap(tagBytes);
            }

            if ((tagBytes != null) && (tagBytes.Length > 0))
            {
                if (tag.Type == "Float")
                {
                    value = BitConverter.ToSingle(tagBytes);
                }
                else if (tag.Type == "Boolean")
                {
                    value = BitConverter.ToBoolean(tagBytes);
                }
                else if (tag.Type == "Integer")
                {
                    value = BitConverter.ToInt32(tagBytes);
                }
                else if (tag.Type == "String")
                {
                    value = Encoding.UTF8.GetString(tagBytes);
                }
                else if (tag.Type == "Short")
                {
                    value = BitConverter.ToInt16(tagBytes);
                }
                else if (tag.Type == "Byte")
                {
                    value = tagBytes[0];
                }
                else
                {
                    throw new ArgumentException("Type not supported by LoRaWAN.");
                }
            }

            return value;
        }

        private byte[] HexToBytes(string hex)
        {
            // Remove the "0x" prefix if present
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex.Substring(2);
            }

            // Ensure even length
            if (hex.Length % 2 != 0)
            {
                hex = "0" + hex;
            }

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

        public void Write(AssetTag tag, string value)
        {
            // Writing sensor values is not supported by LoRaWAN.
        }

        private byte[] Read(string devEUI, string channelId, string typeId, ushort count)
        {
            try
            {
                foreach (KeyValuePair<StationEui, GatewayConnection> gateway in WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways)
                {
                    foreach (KeyValuePair<DevEui, LoRaDevice> device in gateway.Value.Devices)
                    {
                        if (device.Key == DevEui.Parse(devEUI))
                        {
                            // track best match in case we find mutiple entries in different payloads
                            DateTime latestTimestamp = DateTime.MinValue;
                            byte[] bestMatch = null;

                            foreach (KeyValuePair<int, ReceivedPayload> payloads in device.Value.LastKnownDecodedPayloads)
                            {
                                byte[] payload = payloads.Value.Payload;
                                for (int i = 0; i < payload.Length - 2; i++)
                                {
                                    // if typeId is null, the channelId is a simply an offset into the payload to read the value from
                                    if (typeId == null)
                                    {
                                        bestMatch = payload.AsSpan(byte.Parse(channelId), count).ToArray();
                                        latestTimestamp = payloads.Value.Timestamp;
                                        break;
                                    }
                                    else
                                    {
                                        // check if the payload matches the requested channelId and typeId and the timestamp is the latest one
                                        if ((payload[i] == byte.Parse(channelId))
                                         && (payload[i + 1] == byte.Parse(typeId))
                                         && (latestTimestamp < payloads.Value.Timestamp))
                                        {
                                            bestMatch = payload.AsSpan(i + 2, count).ToArray();
                                            latestTimestamp = payloads.Value.Timestamp;
                                        }
                                    }
                                }
                            }

                            if (bestMatch != null)
                            {
                                // if we found a match, return it now
                                return bestMatch;
                            }
                            else
                            {
                                // save some time as we already know we will not find a match in this gateway
                                break;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message);
                return null;
            }
        }

        public string ExecuteAction(string address, string actionName, string[] inputArgs, string[] outputArgs)
        {
            throw new NotImplementedException();
        }
    }
}
