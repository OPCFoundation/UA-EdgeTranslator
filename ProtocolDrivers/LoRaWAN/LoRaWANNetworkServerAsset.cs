namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using LoRaWan;
    using LoRaWan.NetworkServer;
    using LoRaWan.NetworkServer.BasicsStation;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using static LoRaWan.NetworkServer.LoRaDevice;

    public class LoRaWANNetworkServerAsset : IAsset
    {
        public bool IsConnected { get; private set; } = false;

        /// <summary>
        /// Conditional-presence and value-mapping rules for this asset's fields,
        /// registered by the driver when each tag is created.
        /// </summary>
        public LoRaWANFieldRules FieldRules { get; } = new();

        public LoRaWANNetworkServerAsset()
        {
            // Router configurations are loaded up front rather than when an
            // asset is onboarded: a gateway may send its 'version' message as
            // soon as the server is listening, and it must be answered with a
            // router_config immediately or it will drop the connection.
            RouterConfigStore.LoadAll(Path.Combine(Directory.GetCurrentDirectory(), "settings"));

            _ = Task.Run(() => BasicsStationNetworkServer.RunServerAsync());
        }

        private void ConnectCore(string ipAddress, int port)
        {
            try
            {
                LoRaWANUri uri = LoRaWANUri.Parse(ipAddress);
                DevEui devEui = DevEui.Parse(uri.DevEUI);

                if (uri.IsRouterConfig)
                {
                    // The configuration itself was already loaded at startup;
                    // onboarding only binds it to this specific gateway so the
                    // gateway is served its own config rather than the default.
                    string routerConfig = RouterConfigStore.Get(devEui.ToString());

                    if (routerConfig == null)
                    {
                        throw new InvalidOperationException(
                            $"No Basic Station router configuration was found for gateway '{devEui}'. Place its router_config JSON in the settings folder, named either '{devEui}.json' or after the gateway model.");
                    }

                    RouterConfigStore.Add(devEui.ToString(), routerConfig);

                    SearchDevicesResult.AddDevice(devEui, routerConfig);
                }
                else
                {
                    // The OTAA root key is a secret and is therefore never taken
                    // from the Thing Description; it is injected at runtime.
                    SearchDevicesResult.AddDevice(devEui, ResolveAppKey(uri.DevEUI));
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }

            IsConnected = true;
        }

        /// <summary>
        /// Resolves a device's OTAA AppKey from the environment.
        /// <para>
        /// The W3C WoT LoRaWAN binding requires that root keys are declared as
        /// security schemes and their values "injected at runtime", never
        /// written into the Thing Description. The key is looked up per device
        /// as <c>LORAWAN_APPKEY_&lt;devEUI&gt;</c>, falling back to a single
        /// <c>LORAWAN_APPKEY</c> for one-device deployments.
        /// </para>
        /// </summary>
        private static string ResolveAppKey(string devEui)
        {
            string perDevice = Environment.GetEnvironmentVariable("LORAWAN_APPKEY_" + devEui.ToUpperInvariant());

            if (!string.IsNullOrWhiteSpace(perDevice))
            {
                return perDevice;
            }

            string shared = Environment.GetEnvironmentVariable("LORAWAN_APPKEY");

            if (!string.IsNullOrWhiteSpace(shared))
            {
                return shared;
            }

            // Failing loudly beats registering the device with a null key and
            // leaving the operator to work out why every uplink fails to decrypt.
            throw new InvalidOperationException(
                $"No OTAA AppKey was supplied for LoRaWAN device '{devEui}'. Set LORAWAN_APPKEY_{devEui.ToUpperInvariant()} (or LORAWAN_APPKEY) in the environment; the W3C WoT LoRaWAN binding forbids storing the key in the Thing Description.");
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
                        // read the router configuration cached at connect time
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
                LoRaWANFieldRule rule = FieldRules.Get(tag.Name);

                // A gated field is absent unless its discriminator says
                // otherwise. Returning null rather than a decoded value keeps an
                // absent field distinguishable from one that genuinely read
                // zero, which is the whole point of lorav:presentWhen.
                if ((rule?.PresentWhen != null) && !IsFieldPresent(tag, rule))
                {
                    return null;
                }

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

                if (rule?.ValueMap != null)
                {
                    value = LoRaWANFieldRules.ApplyValueMap(rule.ValueMap, value);
                }
            }

            return value;
        }

        /// <summary>
        /// Evaluates a <c>lorav:presentWhen</c> gate by re-reading the
        /// discriminator field from the same payload.
        /// <para>
        /// The discriminator's offset was resolved from its
        /// <c>lorav:alias</c> when the tag was created, so this only has to pull
        /// the bytes and compare.
        /// </para>
        /// </summary>
        private bool IsFieldPresent(AssetTag tag, LoRaWANFieldRule rule)
        {
            if (rule.DiscriminatorOffset is null)
            {
                // The gate names a field that does not exist in this Thing
                // Description. Treating the value as present would invent data,
                // so it is reported absent and the reason logged once per read.
                Log.Logger.Warning(
                    "LoRaWAN field '{Tag}' is gated on '{Field}', which no form publishes under that lorav:alias.",
                    tag.Name,
                    rule.PresentWhen.Field);

                return false;
            }

            string[] addressParts = tag.Address.Split(['?', '&', '=', '/']);

            if (addressParts.Length < 4)
            {
                return false;
            }

            // The discriminator is located by byte offset within the same
            // payload, which is the 4-part form of the address scheme
            // regardless of how the gated field itself is addressed.
            byte[] bytes = Read(
                addressParts[0],
                rule.DiscriminatorOffset.Value.ToString(CultureInfo.InvariantCulture),
                null,
                (ushort)rule.DiscriminatorLength);

            if ((bytes is null) || (bytes.Length == 0))
            {
                return false;
            }

            long discriminator = 0;

            // Big-endian: the binding's default byte order.
            foreach (byte b in bytes)
            {
                discriminator = (discriminator << 8) | b;
            }

            if (rule.PresentWhen.Bit.HasValue)
            {
                return ((discriminator >> rule.PresentWhen.Bit.Value) & 1) == 1;
            }

            if (rule.PresentWhen.Value.HasValue)
            {
                return discriminator == rule.PresentWhen.Value.Value;
            }

            // A condition with neither 'bit' nor 'value' constrains nothing.
            return true;
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
