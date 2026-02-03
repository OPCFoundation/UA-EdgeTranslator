namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;

    public class SiemensProtocolDriver: IProtocolDriver
    {
        public string Scheme => "s7";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/s7";

        public IEnumerable<string> Discover()
        {
            // S7Comm does not support discovery
            return new List<string>();
        }

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            ThingDescription td = new()
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1"],
                Id = "urn:" + assetName,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Base = assetEndpoint,
                Title = assetName,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            SiemensAsset asset = new();
            var endpointParts = assetEndpoint.Split(':');
            asset.Connect(endpointParts[1], int.Parse(endpointParts[2]));

            // read the first 100 blocks until an error is encountered
            for (var i = 0; i < 100; i++)
            {
                var sizeRead = 0;
                var buffer = new byte[65536]; // Maximum size for a DB
                try
                {
                    var dbResult = asset.S7.DBGet(1, buffer, ref sizeRead);
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

                var propertyName = "DB" + i.ToString() + "?0";

                S7Form form = new()
                {
                    Href = propertyName,
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

                if (!td.Properties.ContainsKey(propertyName))
                {
                    td.Properties.Add(propertyName, property);
                }
            }

            return td;
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            unitId = 1; // S7 does not use unit IDs

            string[] address = td.Base.Split([':', '/']);
            if ((address.Length != 5) || (address[0] != "s7"))
            {
                throw new Exception("Expected S7 PLC address in the format s7://ipaddress:port!");
            }

            // check if we can reach the Siemens asset
            SiemensAsset asset = new();
            asset.Connect(address[3], int.Parse(address[4]));

            return asset;
        }

        public AssetTag CreateTag(
            ThingDescription td,
            object form,
            string assetId,
            byte unitId,
            string variableId,
            string mappedUAExpandedNodeId,
            string mappedUAFieldPath)
        {
            S7Form s7Form = JsonConvert.DeserializeObject<S7Form>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = s7Form.Href,
                UnitID = unitId,
                Type = s7Form.Type.ToString(),
                PollingInterval = (int)s7Form.PollingTime,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }
    }
}
