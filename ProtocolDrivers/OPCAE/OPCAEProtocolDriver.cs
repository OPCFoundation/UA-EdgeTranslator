namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

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

        public async Task<AssetConnection> CreateAndConnectAssetAsync(ThingDescription td, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(td);
            ValidateEndpoint(td.Base, out Uri endpoint);
            byte unitId = 1;

            OpcAeEventForm form = td.Events?
                .Values
                .SelectMany(value => value.Forms ?? Array.Empty<object>())
                .Select(TryReadEventForm)
                .FirstOrDefault(value => value != null)
                ?? new OpcAeEventForm { Href = td.Base };

            var asset = new OPCAEAsset();
            asset.Configure(endpoint, form);
            await asset.ConnectAsync(endpoint.Host, 0, cancellationToken).ConfigureAwait(false);
            return new AssetConnection(asset, unitId);
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

        private static OpcAeEventForm TryReadEventForm(object value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is OpcAeEventForm typed)
            {
                return typed;
            }

            if (value is JObject jsonObject)
            {
                return jsonObject.ToObject<OpcAeEventForm>();
            }

            if (value is JToken token)
            {
                if (token.Type == JTokenType.Object)
                {
                    return token.ToObject<OpcAeEventForm>();
                }

                if (token.Type == JTokenType.String)
                {
                    return TryDeserializeJsonString(token.Value<string>());
                }

                return null;
            }

            if (value is string json)
            {
                return TryDeserializeJsonString(json);
            }

            return null;
        }

        private static OpcAeEventForm TryDeserializeJsonString(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<OpcAeEventForm>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
