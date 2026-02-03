namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.IO.BACnet;
    using System.Threading;

    public class BACNetProtocolDriver: IProtocolDriver
    {
        public string Scheme => "bacnet";

        private List<string> _discoverdAssets = new();

        private BacnetClient _client = new(new BacnetIpUdpProtocolTransport(0xBAC0, false));

        public string WoTBindingUri => "http://www.w3.org/2022/bacnet";

        public IEnumerable<string> Discover()
        {
            _client.OnIam += OnIAm;
            _client.Start();
            _client.WhoIs();

            Thread.Sleep(10000);

            return _discoverdAssets;
        }

        private void OnIAm(BacnetClient sender, BacnetAddress adr, uint deviceId, uint maxAPDU, BacnetSegmentations segmentation, ushort vendorId)
        {
            var newAddress = "bacnet://" + adr.ToString() + ":" + 0xBAC0.ToString();

            if (!adr.IsMyRouter(adr) && !_discoverdAssets.Contains(newAddress))
            {
                _discoverdAssets.Add(newAddress);
            }
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
            if ((address.Length != 5) || (address[0] != "bacnet"))
            {
                throw new Exception("Expected BACNet device address in the format bacnet://ipaddress/deviceId!");
            }

            // check if we can reach the BACNet asset
            BACNetAsset asset = new();
            asset.Connect(address[3] + "/" + address[4], 0);

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
            GenericForm bacnetForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = bacnetForm.Href,
                UnitID = unitId,
                Type = bacnetForm.Type.ToString(),
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }
    }
}
