namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using MCProtocol;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public class MitsubishiClient : IAsset
    {
        private string _endpoint = string.Empty;

        public List<string> Discover()
        {
            // MCP does not support discovery
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
                Properties = new Dictionary<string, Property>()
            };

            return td;
        }

        public void Connect(string ipAddress, int port)
        {
            try
            {
                _endpoint = ipAddress + ":" + port.ToString();

                PLCData.PLC = new Mitsubishi.McProtocolTcp(ipAddress, port, Mitsubishi.McFrame.MC3E);
                var result = PLCData.PLC.Open().GetAwaiter().GetResult();

                Log.Logger.Information("Connected to Mitsubishi PLC: " + result.ToString());
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public void Disconnect()
        {
            if (PLCData.PLC != null)
            {
                PLCData.PLC.Close();
                PLCData.PLC = null;
            }
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            var data = new PLCData<byte>((Mitsubishi.PlcDeviceType)unitID, int.Parse(addressWithinAsset), count);

            data.ReadData();

            var result = new byte[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = data[i];
            }

            return Task.FromResult(result);
        }

        public Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            var data = new PLCData<byte>((Mitsubishi.PlcDeviceType)unitID, int.Parse(addressWithinAsset), values.Length);

            for (var i = 0; i < values.Length; i++)
            {
                data[i] = values[i];
            }

            data.WriteData();

            return Task.CompletedTask;
        }
    }
}
