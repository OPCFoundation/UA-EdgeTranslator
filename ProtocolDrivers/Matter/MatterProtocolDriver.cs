namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class MatterProtocolDriver: IProtocolDriver
    {
        public string Scheme => "matter";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/matter";

        private readonly MatterControllerAsset _matterController = new();

        public IEnumerable<string> Discover()
        {
            return _matterController._fabric.Nodes.Select(n => n.Key.ToString()).ToList();
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

            string[] address = td.Base.Split(['/']);
            if ((address.Length != 5) || (address[0] != "matter:"))
            {
                throw new Exception("Expected Matter device address in the format matter://DeviceName/ThreadNetworkDataset/MatterDeviceCommissioningQRCode!");
            }

            // check if we can reach the Matter asset
            _matterController.Connect(td.Base, 0);


            return _matterController;
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
            GenericForm matterForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = matterForm.Href,
                UnitID = unitId,
                Type = matterForm.Type.ToString(),
                PollingInterval = (int)matterForm.PollingTime,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };

        }
    }
}
