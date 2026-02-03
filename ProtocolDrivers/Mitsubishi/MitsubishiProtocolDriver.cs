namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;

    public class MitsubishiProtocolDriver: IProtocolDriver
    {
        public string Scheme => "mcp";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/mcp";

        public IEnumerable<string> Discover()
        {
            // MCP does not support discovery
            return new List<string>();
        }

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            return new ThingDescription()
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
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            unitId = 1; // Mitsubishi PLCs do not use unit IDs

            string[] address = td.Base.Split([':', '/']);
            if ((address.Length != 5) || (address[0] != "mcp"))
            {
                throw new Exception("Expected Mitsubishi PLC address in the format mcp://ipaddress:port!");
            }

            // check if we can reach the Mitsubishi asset
            MitsubishiAsset asset = new();
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
            GenericForm mitsubishiForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = mitsubishiForm.Href,
                UnitID = unitId,
                Type = mitsubishiForm.Type.ToString(),
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }
    }
}
