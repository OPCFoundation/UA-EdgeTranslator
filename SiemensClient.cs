
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using Sharp7;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public class SiemensClient : IAsset
    {
        private S7Client _S7 = null;

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

            // read the first 100 blocks until an error is encountered
            for (int i = 0; i < 100; i++)
            {
                int sizeRead = 0;
                byte[] buffer = new byte[65536]; // Maximum size for a DB
                try
                {
                    int dbResult = _S7.DBGet(1, buffer, ref sizeRead);
                    if (dbResult != 0)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);
                    break;
                }

                string reference = "DB" + i.ToString() + "?0";

                S7Form form = new()
                {
                    Href = reference,
                    Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                    PollingTime = 1000,
                    S7DBNumber = i,
                    S7Start = 0,
                    S7Size = sizeRead,
                    Type = TypeString.String,
                };

                Log.Logger.Information("S7 DB" + i.ToString() + ": " + BitConverter.ToString(buffer, 0, sizeRead));

                Property property = new()
                {
                    Type = TypeEnum.String,
                    ReadOnly = true,
                    Observable = true,
                    Forms = new object[1] { form }
                };

                if (!td.Properties.ContainsKey(reference))
                {
                    td.Properties.Add(reference, property);
                }
            }

            return td;
        }

        public void Connect(string ipAddress, int port)
        {
            try
            {
                _endpoint = ipAddress;

                _S7 = new();

                // assume rack 0
                int result = _S7.ConnectTo(ipAddress, 0, port);

                if (result == 0)
                {
                    Log.Logger.Information("Connected to Siemens S7");
                }
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
                _S7.Disconnect();
                _S7 = null;
            }
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            byte[] buffer = new byte[count];
            _S7.DBRead(unitID, int.Parse(addressWithinAsset), count, buffer);
            return Task.FromResult(buffer);
        }

        public Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            _S7.DBWrite(unitID, int.Parse(addressWithinAsset), values.Length, values);
            return Task.CompletedTask;
        }
    }
}
