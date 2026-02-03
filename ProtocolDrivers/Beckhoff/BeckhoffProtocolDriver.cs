namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;

    public class BeckhoffProtocolDriver: IProtocolDriver
    {
        public string Scheme => "ads";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/ads";

        public IEnumerable<string> Discover()
        {
            // ADS does not support discovery
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

            string[] address = td.Base.Split([':', '/']);
            if ((address.Length != 6) || (address[0] != "ads"))
            {
                throw new Exception("Expected Beckhoff PLC address in the format ads://ipaddress:port!");
            }

            // check if we can reach the Beckhoff asset
            BeckhoffAsset asset = new();
            asset.Connect(address[3] + ":" + address[4], int.Parse(address[5]));

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
            GenericForm adsForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = adsForm.Href,
                UnitID = unitId,
                Type = adsForm.Type.ToString(),
                PollingInterval = (int)adsForm.PollingTime,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };

        }
    }
}
