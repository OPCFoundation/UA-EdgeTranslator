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

        private bool _started = false;

        public string WoTBindingUri => "http://www.w3.org/2022/bacnet";

        public IEnumerable<string> Discover()
        {
            // Reset previous discovery results and avoid accumulating handlers
            // when Discover() is called more than once.
            _discoverdAssets.Clear();
            _client.OnIam -= OnIAm;
            _client.OnIam += OnIAm;

            if (!_started)
            {
                _client.Start();
                _started = true;
            }

            _client.WhoIs();

            Thread.Sleep(10000);

            return _discoverdAssets;
        }

        private void OnIAm(BacnetClient sender, BacnetAddress adr, uint deviceId, uint maxAPDU, BacnetSegmentations segmentation, ushort vendorId)
        {
            // BacnetAddress.ToString() already contains the IP and port (e.g. "192.168.1.5:47808").
            // Include the BACnet device instance so the resulting URI uniquely identifies the device
            // and matches the format expected by CreateAndConnectAsset (bacnet://host[:port]/deviceId).
            var newAddress = "bacnet://" + adr.ToString() + "/" + deviceId.ToString();

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
            if (td == null)
            {
                throw new ArgumentNullException(nameof(td));
            }

            unitId = 1;

            if (string.IsNullOrWhiteSpace(td.Base)
                || !Uri.TryCreate(td.Base, UriKind.Absolute, out Uri uri)
                || !string.Equals(uri.Scheme, "bacnet", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(uri.Host)
                || string.IsNullOrWhiteSpace(uri.AbsolutePath)
                || uri.AbsolutePath == "/")
            {
                throw new Exception("Expected BACNet device address in the format bacnet://ipaddress[:port]/deviceId!");
            }

            string deviceId = uri.AbsolutePath.Trim('/');
            int port = uri.IsDefaultPort ? 0xBAC0 : uri.Port;

            // BACNetAsset.Connect expects "<host>/<deviceId>"; BACnet IP always uses the
            // UDP port configured on the BacnetClient transport (0xBAC0 by default).
            BACNetAsset asset = new();
            asset.Connect(uri.Host + "/" + deviceId, port);

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
            if (form == null)
            {
                throw new ArgumentNullException(nameof(form));
            }

            GenericForm bacnetForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());
            if (bacnetForm == null)
            {
                throw new ArgumentException("Form payload could not be parsed as a BACNet form.", nameof(form));
            }

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
