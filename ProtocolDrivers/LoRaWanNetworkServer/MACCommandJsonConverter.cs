// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LoRaTools;

namespace LoRaWANContainer.LoRaWan.NetworkServer
{
    using System;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// Defines a <see cref="JsonConverter"/> capable of converting a JSON list of elements to concrete <see cref="MacCommand"/> objects.
    /// </summary>
    public class MacCommandJsonConverter : JsonConverter
    {
        public override bool CanRead => true;

        public override bool CanConvert(Type objectType)
        {
            return typeof(MacCommand).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            ArgumentNullException.ThrowIfNull(serializer);

            var item = JObject.Load(reader);
            var cidPropertyValue = item["cid"].Value<string>();
            if (string.IsNullOrEmpty(cidPropertyValue))
                throw new JsonReaderException("Undefined mac command identifier");

            if (Enum.TryParse<Cid>(cidPropertyValue, true, out var macCommandType))
            {
                switch (macCommandType)
                {
                    case Cid.DevStatusCmd:
                    {
                        var cmd = new DevStatusRequest();
                        serializer.Populate(item.CreateReader(), cmd);
                        return cmd;
                    }

                    case Cid.DutyCycleCmd:
                    {
                        var cmd = new DutyCycleRequest();
                        serializer.Populate(item.CreateReader(), cmd);
                        return cmd;
                    }

                    case Cid.NewChannelCmd:
                    {
                        var cmd = new NewChannelRequest();
                        serializer.Populate(item.CreateReader(), cmd);
                        return cmd;
                    }

                    case Cid.RXParamCmd:
                    {
                        var cmd = new RXParamSetupRequest();
                        serializer.Populate(item.CreateReader(), cmd);
                        return cmd;
                    }

                    case Cid.RXTimingCmd:
                    {
                        var cmd = new RXTimingSetupRequest();
                        serializer.Populate(item.CreateReader(), cmd);
                        return cmd;
                    }
                    case Cid.LinkCheckCmd:
                    case Cid.LinkADRCmd:
                    {
                        GetValue("dataRate", out var datarate);
                        GetValue("txPower", out var txpower);
                        GetValue("chMask", out var chmask);
                        GetValue("chMaskCntl", out var chmaskcntl);
                        GetValue("nbRep", out var nbrep);

                        void GetValue(string propertyName, out JToken value)
                        {
                            if (!item.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out value))
                                throw new JsonReaderException($"Property '{propertyName}' is missing");
                        }

                        var cmd = new LinkADRRequest((ushort)datarate, (ushort)txpower, (ushort)chmask, (byte)chmaskcntl, (byte)nbrep);
                        serializer.Populate(item.CreateReader(), cmd);
                        return cmd;
                    }
                    case Cid.TxParamSetupCmd:
                    default:
                        throw new JsonReaderException($"Unhandled command identifier: {macCommandType}");
                }
            }

            throw new JsonReaderException($"Unknown MAC command identifier: {cidPropertyValue}");
        }

        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotImplementedException();
    }
}
