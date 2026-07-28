namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public sealed class OPCAEProtocolDriver : IProtocolDriver
    {
        public string Scheme => "opc.ae";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/opcae";

        public IEnumerable<string> Discover()
        {
            // Classic OPC A&E server enumeration requires the Core Components
            // RCW runtime. Operators normally provide the ProgID explicitly in
            // the Thing Description, so discovery intentionally stays empty.
            return Array.Empty<string>();
        }

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            ValidateEndpoint(assetEndpoint, out _);

            return new ThingDescription
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1"],
                Id = "urn:" + assetName,
                SecurityDefinitions = new SecurityDefinitions { NosecSc = new NosecSc { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Title = assetName,
                Base = assetEndpoint,
                Description = "Read-only OPC Classic Alarms & Events subscription.",
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>(),
                Events = new Dictionary<string, TDEvent>
                {
                    ["alarms"] = new TDEvent
                    {
                        Description = "Read-only OPC A&E condition transitions.",
                        Forms = [new OpcAeEventForm { Href = assetEndpoint }]
                    }
                }
            };
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            ArgumentNullException.ThrowIfNull(td);
            ValidateEndpoint(td.Base, out Uri endpoint);
            unitId = 1;

            OpcAeEventForm form = td.Events?
                .Values
                .SelectMany(value => value.Forms ?? Array.Empty<object>())
                .Select(value => JsonConvert.DeserializeObject<OpcAeEventForm>(value.ToString()))
                .FirstOrDefault(value => value != null)
                ?? new OpcAeEventForm { Href = td.Base };

            var asset = new OPCAEAsset();
            asset.Configure(endpoint, form);
            asset.Connect(endpoint.Host, 0);
            return asset;
        }

        public AssetTag CreateTag(ThingDescription td, object form, string assetId, byte unitId, string variableId, string mappedUAExpandedNodeId, string mappedUAFieldPath)
        {
            throw new NotSupportedException("OPC A&E is event-driven and does not create polling tags.");
        }

        internal static void ValidateEndpoint(string endpoint, out Uri uri)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri)
                || !string.Equals(uri.Scheme, "opc.ae", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(uri.Host)
                || string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')))
            {
                throw new ArgumentException("Expected an OPC A&E endpoint in the form opc.ae://localhost/<ProgID>.", nameof(endpoint));
            }

            string localMachine = Environment.MachineName;
            bool isLocal = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, localMachine, StringComparison.OrdinalIgnoreCase);

            if (!isLocal)
            {
                throw new ArgumentException("Remote DCOM OPC A&E endpoints are not supported. Use localhost or the local machine name.", nameof(endpoint));
            }
        }
    }
}