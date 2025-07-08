namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using LoRaWan;
    using LoRaWan.NetworkServer;
    using LoRaWan.NetworkServer.BasicsStation;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using static LoRaWan.NetworkServer.LoRaDevice;

    public class LoRaWANNetworkServer : IAsset
    {
        public LoRaWANNetworkServer()
        {
            _ = Task.Run(() => BasicsStationNetworkServer.RunServerAsync(new NetworkServerConfiguration(), new CancellationTokenSource().Token));
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
                var appKey = AppKey.Parse(addressParts[3]);

                SearchDevicesResult.AddDevice(devEui, appKey);
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
                tagBytes = Read(addressParts[0], addressParts[1], addressParts[2], ushort.Parse(addressParts[4]));
            }
            else if (addressParts.Length == 4)
            {
                tagBytes = Read(addressParts[0], addressParts[1], null, ushort.Parse(addressParts[3]));
            }

            if ((tagBytes != null) && !string.IsNullOrEmpty(tag.BitMask))
            {
                // apply the bitmask to the tagBytes, depending on the length of the bitmask
                if (tag.BitMask.Length == 4) // "0xNN"
                {
                    // apply a byte mask
                    tagBytes[0] = (byte)(tagBytes[0] & byte.Parse(tag.BitMask.Substring(2), System.Globalization.NumberStyles.HexNumber));
                }
                else if (tag.BitMask.Length == 6) // "0xNNNN"
                {
                    // apply a short mask
                    tagBytes = BitConverter.GetBytes(BitConverter.ToInt16(tagBytes) & short.Parse(tag.BitMask.Substring(2), System.Globalization.NumberStyles.HexNumber));
                }
                else if (tag.BitMask.Length == 10) // "0xNNNNNNNN"
                {
                    // apply an int mask
                    tagBytes = BitConverter.GetBytes(BitConverter.ToInt32(tagBytes) & int.Parse(tag.BitMask.Substring(2), System.Globalization.NumberStyles.HexNumber));
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

        public void Write(AssetTag tag, string value)
        {
            // Writing sensor values is not supported by LoRaWAN.
        }

        private byte[] Read(string devEUI, string channelId, string typeId, ushort count)
        {
            try
            {
                foreach (KeyValuePair<string, GatewayConnection> gateways in WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways)
                {
                    foreach (KeyValuePair<DevAddr, LoRaDevice> device in gateways.Value.Devices)
                    {
                        if (device.Value.DevEUI == DevEui.Parse(devEUI))
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
