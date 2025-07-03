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

    public class LoRaWANNetworkServer : IAsset
    {
        public LoRaWANNetworkServer()
        {
            using var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;

            var configuration = NetworkServerConfiguration.CreateFromEnvironmentVariables();
            _ = Task.Run(() => BasicsStationNetworkServer.RunServerAsync(configuration, cancellationToken));
        }

        public List<string> Discover()
        {
            List<string> deviceList = new List<string>();
            foreach (var device in SearchDevicesResult.DeviceList)
            {
                deviceList.Add("lorawan://" + device);
            }

            return deviceList;
        }

        public ThingDescription BrowseAndGenerateTD(string name, string endpoint)
        {
            string[] endpointParts = endpoint.Split(new char[] { ':', '/' });

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
            try
            {
                // register the device with the LoRaWAN Network Server
                var devEui = DevEui.Parse(ipAddress);
                SearchDevicesResult.AddDevice(devEui);
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
                        throw new ArgumentException("Type not supported by LoRaWAN.");
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

        public string ExecuteAction(string address, string actionName, string[] inputArgs, string[] outputArgs)
        {
            throw new NotImplementedException();
        }
    }
}
