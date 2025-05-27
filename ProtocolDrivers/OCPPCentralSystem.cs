namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using global::OCPPCentralSystem;
    using global::OCPPCentralSystem.Models;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    public class OCPPCentralSystem : IAsset
    {
        public static ConcurrentDictionary<string, ChargePoint> ChargePoints { get; set; } = new();

        public OCPPCentralSystem()
        {
            _ = Task.Run(() => CentralSystemServer.RunServerAsync());
        }

        public List<string> Discover()
        {
            List<string> deviceList = new List<string>();
            try
            {
                // Discover all charge points
                foreach (var chargePoint in ChargePoints)
                {
                    deviceList.Add(chargePoint.Key);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Error during discovery of OCPP devices.");
            }

            return deviceList;
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
                Properties = new Dictionary<string, Property>()
            };

            // add properties for the requested charge point
            if (ChargePoints.TryGetValue(name, out ChargePoint chargePoint))
            {
                foreach (Connector connector in chargePoint.Connectors.Values)
                {
                    td.Properties.Add("Connector" + connector.ID.ToString(), new Property
                    {
                        Type = TypeEnum.Number,
                        ReadOnly = true,
                        Observable = true,
                    });
                }
            }

            return td;
        }

        public void Connect(string ipAddress, int port)
        {
            // nothing to do, since we are the server in OCPP and the charging stations connect to us
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

            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if (addressParts.Length == 2)
            {
                byte[] tagBytes = Read(addressParts[0], 0, null, ushort.Parse(addressParts[1])).GetAwaiter().GetResult();

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
                    else
                    {
                        throw new ArgumentException("Type not supported by OCPP.");
                    }
                }
            }

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

            Write(addressParts[0], 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }

        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            try
            {
                // TODO

                return Task.FromResult((byte[])null);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message);
                return Task.FromResult((byte[])null);
            }
        }

        private Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            try
            {
                // TODO
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message);
            }

            return Task.CompletedTask;
        }
    }
}
