namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;

    public class HTTPClientProtocolDriver : IProtocolDriver
    {
        public string Scheme => "http";

        public string WoTBindingUri => "https://www.w3.org/2011/http";

        public IEnumerable<string> Discover()
        {
            // HTTP REST endpoints do not support discovery
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
            unitId = 1;

            Uri uri;
            try
            {
                uri = new Uri(td.Base);
            }
            catch (Exception)
            {
                throw new Exception("Expected HTTP endpoint address in the format http://host:port/path!");
            }

            if (uri.Scheme != "http" && uri.Scheme != "https")
            {
                throw new Exception("Expected HTTP or HTTPS scheme in the endpoint address!");
            }

            HTTPClientAsset asset = new();
            asset.Connect(uri.GetLeftPart(UriPartial.Authority), 0);

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
            GenericForm httpForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = httpForm.Href,
                UnitID = unitId,
                Type = httpForm.Type.ToString(),
                PollingInterval = (int)httpForm.PollingTime,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }
    }
}
