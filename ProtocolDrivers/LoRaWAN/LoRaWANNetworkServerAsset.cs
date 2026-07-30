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
    using System.Threading;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using static LoRaWan.NetworkServer.LoRaDevice;

    public class LoRaWANNetworkServerAsset : IAsset
    {
        public bool IsConnected { get; private set; } = false;

        public LoRaWANNetworkServerAsset()
        {
            _ = Task.Run(() => BasicsStationNetworkServer.RunServerAsync());
        }

        private void ConnectCore(string ipAddress, int port)
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

            IsConnected = true;
        }

        private void DisconnectCore()
        {
            IsConnected = false;
        }

        public string GetRemoteEndpoint()
        {
            return string.Empty;
        }

        private object ReadCore(AssetTag tag)
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
                tagBytes = ByteSwapper.Swap(tagBytes, tag.SwapPerWord);
            }

            if ((tagBytes != null) && (tagBytes.Length > 0))
            {
                if (tag.Type == "Float")
                {
                    value = BitConverter.ToSingle(tagBytes) * tag.Multiplier;
                }
                else if (tag.Type == "Boolean")
                {
                    value = BitConverter.ToBoolean(tagBytes);
                }
                else if (tag.Type == "Integer")
                {
                    value = BitConverter.ToInt32(tagBytes) * tag.Multiplier;
                }
                else if (tag.Type == "String")
                {
                    value = Encoding.UTF8.GetString(tagBytes);
                }
                else if (tag.Type == "Short")
                {
                    value = BitConverter.ToInt16(tagBytes) * tag.Multiplier;
                }
                else if (tag.Type == "Byte")
                {
                    value = tagBytes[0] * tag.Multiplier;
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

        private void WriteCore(AssetTag tag, object value)
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

        private string ExecuteActionCore(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            return null;
        }

        public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
        {
            ConnectCore(ipAddress, port);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCore();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisconnectCore();
            return ValueTask.CompletedTask;
        }

        public Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReadCore(tag));
        }

        public Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default)
        {
            WriteCore(tag, value);
            return Task.CompletedTask;
        }

        public Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default)
        {
            IList<object> outputArgs = null;
            string status = ExecuteActionCore(method, inputArgs, ref outputArgs);
            return Task.FromResult(AssetActionResult.FromOutputs(status, outputArgs));
        }
    }
}
