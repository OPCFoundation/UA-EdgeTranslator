namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;

    public class LoRaWANProtocolDriver: IProtocolDriver
    {
        public string Scheme => "lorawan";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/lorawan";

        private readonly LoRaWANNetworkServerAsset _lorawanNetworkServer = new();

        public IEnumerable<string> Discover()
        {
            // LoRaWAN does not support discovery
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
            if ((address.Length != 6) || (address[0] != "lorawan"))
            {
                throw new Exception("Expected LoRaWAN address in the format lorawan://deviceeui/appkey/device or lorawan://deviceeui/gatewaymodel/routerconfig!");
            }

            _lorawanNetworkServer.Connect(td.Base, 0);

            return _lorawanNetworkServer;
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
            AssetTag tag;
            if (td.Base.ToLower().EndsWith("routerconfig"))
            {
                tag = new()
                {
                    Name = variableId,
                    Address = td.Base.ToLower(),
                    UnitID = unitId,
                    Type = TypeString.String.ToString(),
                    MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                    MappedUAFieldPath = mappedUAFieldPath
                };
            }
            else
            {
                LoRaWANForm lorawanForm = JsonConvert.DeserializeObject<LoRaWANForm>(form.ToString());
                tag = new()
                {
                    Name = variableId,
                    Address = lorawanForm.Href,
                    UnitID = unitId,
                    Type = lorawanForm.Type.ToString(),
                    IsBigEndian = lorawanForm.MostSignificantByte || lorawanForm.MostSignificantWord,
                    SwapPerWord = lorawanForm.MostSignificantWord,
                    Multiplier = lorawanForm.Multiplier ?? 1.0f,
                    BitMask = lorawanForm.BitMask,
                    MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                    MappedUAFieldPath = mappedUAFieldPath
                };
            }

            return tag;
        }
    }
}
