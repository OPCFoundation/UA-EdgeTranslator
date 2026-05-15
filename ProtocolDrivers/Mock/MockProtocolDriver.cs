namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Test-only protocol driver. Backs every IProtocolDriver / IAsset method with
    /// deterministic in-memory state so unit tests can exercise the entire driver
    /// contract without any network I/O, hardware, or background polling.
    ///
    /// The scheme is deliberately distinct from any real-world protocol so that
    /// the driver registry routes <c>mock://...</c> base URIs to this driver
    /// without ambiguity, even if a real driver ever gets a similar prefix.
    /// </summary>
    public class MockProtocolDriver : IProtocolDriver
    {
        public const string MockScheme = "mock";

        public const string MockBindingUri = "https://opcfoundation.org/UA-EdgeTranslator/mock";

        // Deterministic discovery payload that tests can assert against.
        private static readonly string[] _discoveredEndpoints =
        {
            "mock://device-1:1502/1",
            "mock://device-2:1502/2"
        };

        public string Scheme => MockScheme;

        public string WoTBindingUri => MockBindingUri;

        public IEnumerable<string> Discover() => _discoveredEndpoints;

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            if (string.IsNullOrWhiteSpace(assetName))
            {
                throw new ArgumentException("Asset name must be provided.", nameof(assetName));
            }

            if (string.IsNullOrWhiteSpace(assetEndpoint))
            {
                throw new ArgumentException("Asset endpoint must be provided.", nameof(assetEndpoint));
            }

            return new ThingDescription
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1"],
                Id = "urn:" + assetName,
                SecurityDefinitions = new SecurityDefinitions { NosecSc = new NosecSc { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Base = assetEndpoint,
                Title = assetName,
                Description = "Mock Thing Description used by the UA Edge Translator test suite.",
                Properties = new Dictionary<string, Property>
                {
                    ["temperature"] = new Property
                    {
                        Type = TypeEnum.Number,
                        ReadOnly = true,
                        Forms = new object[]
                        {
                            new
                            {
                                href = "/temperature",
                                type = "xsd:float",
                                pollingTime = 1000L
                            }
                        }
                    }
                },
                Actions = new Dictionary<string, TDAction>
                {
                    ["reset"] = new TDAction
                    {
                        Forms = new object[]
                        {
                            new
                            {
                                href = "/reset",
                                type = "xsd:string"
                            }
                        }
                    }
                }
            };
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            ArgumentNullException.ThrowIfNull(td);

            if (string.IsNullOrWhiteSpace(td.Base))
            {
                throw new ArgumentException("Thing Description must specify a base URI.", nameof(td));
            }

            if (!Uri.TryCreate(td.Base, UriKind.Absolute, out Uri uri))
            {
                throw new ArgumentException($"Invalid base URI '{td.Base}'.", nameof(td));
            }

            if (!string.Equals(uri.Scheme, MockScheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Expected '{MockScheme}' scheme but got '{uri.Scheme}'.",
                    nameof(td));
            }

            // Last path segment, if numeric, is treated as the unit id; default to 1.
            unitId = 1;
            if (uri.Segments.Length > 0)
            {
                string lastSegment = uri.Segments[^1].Trim('/');
                if (byte.TryParse(lastSegment, out byte parsed))
                {
                    unitId = parsed;
                }
            }

            MockAsset asset = new();
            asset.Connect(uri.Host, uri.Port);
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
            ArgumentNullException.ThrowIfNull(form);

            MockForm mockForm = JsonConvert.DeserializeObject<MockForm>(form.ToString());
            if (mockForm == null)
            {
                throw new ArgumentException("Form payload could not be parsed as a MockForm.", nameof(form));
            }

            return new AssetTag
            {
                Name = variableId,
                Address = mockForm.Href ?? string.Empty,
                UnitID = unitId,
                Type = mockForm.Type ?? "xsd:float",
                PollingInterval = (int)Math.Max(1L, mockForm.PollingTime),
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }

        // Minimal form schema understood by the mock driver. Property names match
        // the JSON the BrowseAndGenerateTD method emits so the round-trip works.
        private sealed class MockForm
        {
            [JsonProperty("href")]
            public string Href { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("pollingTime")]
            public long PollingTime { get; set; } = 1000L;
        }
    }
}
