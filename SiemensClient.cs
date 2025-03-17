
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
        public class DataBlock
        {
            public int Number { get; set; }

            public List<DataTag> DataTags { get; set; }

            public DataBlock(int number)
            {
                Number = number;
                DataTags = new List<DataTag>();
            }
        }

        public class DataTag
        {
            public string Address { get; set; }

            public object Value { get; set; }
        }

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

            string[] endpointParts = endpoint.Split(':');
            Connect(endpointParts[1], int.Parse(endpointParts[2]));

            List<DataBlock> dataBlocks = EnumerateDataBlocks();
            foreach (DataBlock dataBlock in dataBlocks)
            {
                foreach (DataTag tag in dataBlock.DataTags)
                {
                    string reference = "DB" + dataBlock.Number + "?" + tag.Address;

                    S7Form form = new()
                    {
                        Href = reference,
                        Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                        PollingTime = 1000
                    };

                    Property property = new()
                    {
                        Type = TypeEnum.Number,
                        ReadOnly = true,
                        Observable = true,
                        Forms = new object[1] { form }
                    };

                    if (!td.Properties.ContainsKey(reference))
                    {
                        td.Properties.Add(reference, property);
                    }
                }
            }

            return td;
        }

        public List<DataBlock> EnumerateDataBlocks()
        {
            List<DataBlock> dataBlocks = new List<DataBlock>();

            // read first 100 data blocks and stop on error
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    // assume data block is 256 bytes long
                    DataBlock db = new DataBlock(i);
                    db.DataTags = new List<DataTag>();
                    db.DataTags[0].Address = "DB" + i.ToString();
                    db.DataTags[0].Value = _S7.Read(DataType.DataBlock, i, 0, VarType.Byte, 256);
                    dataBlocks.Add(db);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);
                    break;
                }
            }

            return dataBlocks;
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
