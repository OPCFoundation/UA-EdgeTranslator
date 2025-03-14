
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using S7.Net;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public class SiemensClient : IAsset
    {
        private Plc _S7 = null;

        private string _endpoint = string.Empty;

        public List<string> Discover()
        {
            // S7Comm does not support discovery
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
                _endpoint = ipAddress;

                _S7 = new Plc(CpuType.S71500, _endpoint, 0, (short)port);
                _S7.Open();

                byte result = _S7.ReadStatus();
                if (result != 0x8)
                {
                    throw new Exception("S7 status error: " + result.ToString());
                }

                Log.Logger.Information("Connected to Siemens S7: " + result.ToString());
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public void Disconnect()
        {
            if (_S7 != null)
            {
                _S7.Close();
                _S7 = null;
            }
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            return Task.FromResult(_S7.ReadBytes(DataType.DataBlock, unitID, int.Parse(addressWithinAsset), count));
        }

        public Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            _S7.WriteBytes(DataType.DataBlock, unitID, int.Parse(addressWithinAsset), values);
            return Task.CompletedTask;
        }
    }
}
