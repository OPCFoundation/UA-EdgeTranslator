namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.Net.Http;

    /// <summary>
    /// Protocol driver for DMTF Redfish (REST over HTTPS) used for out-of-band
    /// hardware management of servers, chassis and Baseboard Management Controllers
    /// (BMCs). Redfish is the management protocol acknowledged in O-PAS Part 7
    /// (Physical Platform) for surfacing Physical Platform (PP) attributes such
    /// as Secure Boot status (CRQ-P7-010), Clock Stability (CRQ-P7-005), Non-Volatile
    /// Storage (CRQ-P7-007/008) and Network Hardware (PP-F-004) into the O-PAS
    /// Connectivity Framework (OCF) via OPC UA.
    ///
    /// WoT Thing Descriptions consumed by this driver use the URI scheme
    /// <c>redfish://host[:port]</c> in <c>base</c>. The driver always
    /// negotiates HTTPS to the Redfish service (BMCs only expose HTTPS in any
    /// hardened deployment); authentication is read from the environment
    /// (REDFISH_USERNAME / REDFISH_PASSWORD) so credentials never appear in
    /// the Thing Description.
    ///
    /// Forms address Redfish resources by their service-root-relative path and
    /// optionally extract a single field via a JSON Pointer fragment, e.g.
    ///   "/redfish/v1/Systems/1#/PowerState"
    ///   "/redfish/v1/Managers/1#/DateTime"
    ///   "/redfish/v1/Systems/1/SecureBoot#/SecureBootEnable"
    /// Actions map to Redfish Action POSTs (e.g.
    ///   "/redfish/v1/Systems/1/Actions/ComputerSystem.Reset").
    /// </summary>
    public class RedfishProtocolDriver : IProtocolDriver
    {
        public string Scheme => "redfish";

        // Redfish is JSON-over-HTTP; reuse the W3C HTTP WoT binding template URI.
        public string WoTBindingUri => "https://www.w3.org/2011/http";

        public IEnumerable<string> Discover()
        {
            // Redfish discovery via SSDP (UPnP search target
            // urn:dmtf-org:service:redfish-rest:1) requires UDP multicast and
            // is out of scope of the in-process driver. Onboard a Redfish
            // service by providing its base URL explicitly.
            return new List<string>();
        }

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            ThingDescription td = new()
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1"],
                Id = "urn:" + assetName,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Base = assetEndpoint,
                Title = assetName,
                Description = "Redfish managed asset (DMTF Redfish, mapped per O-PAS Part 7).",
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            string httpsBase = NormalizeBase(assetEndpoint);

            using var client = new HttpClient
            {
                BaseAddress = new Uri(httpsBase.TrimEnd('/') + "/")
            };

            // Apply env-based auth so we can browse protected service roots.
            var asset = new RedfishAsset();
            asset.ApplyAuthFromEnvironment(client);

            // 1) Service root.
            JObject serviceRoot = SafeGetJson(client, "redfish/v1/");
            if (serviceRoot == null)
            {
                return td;
            }

            // 2) Walk Systems collection and emit one Property per common O-PAS
            //    PP attribute exposed by the first System member.
            string systemsHref = serviceRoot["Systems"]?["@odata.id"]?.ToString();
            string firstSystemHref = ResolveFirstMemberHref(client, systemsHref);
            if (!string.IsNullOrWhiteSpace(firstSystemHref))
            {
                AddProperty(td, "PowerState", TypeEnum.String, TypeString.String, true, firstSystemHref + "#/PowerState");
                AddProperty(td, "BootProgress", TypeEnum.String, TypeString.String, true, firstSystemHref + "#/BootProgress/LastState");
                AddProperty(td, "ManufacturerName", TypeEnum.String, TypeString.String, true, firstSystemHref + "#/Manufacturer");
                AddProperty(td, "ModelName", TypeEnum.String, TypeString.String, true, firstSystemHref + "#/Model");
                AddProperty(td, "SerialNumber", TypeEnum.String, TypeString.String, true, firstSystemHref + "#/SerialNumber");
                AddProperty(td, "SecureBootEnable", TypeEnum.Boolean, TypeString.Boolean, true, firstSystemHref + "/SecureBoot#/SecureBootEnable");

                // Reset action (Redfish Actions live under the resource).
                td.Actions["Reset"] = new TDAction
                {
                    Input = new TDArguments
                    {
                        Type = TypeEnum.Object,
                        Properties = new Dictionary<string, Property>
                        {
                            ["ResetType"] = new Property { Type = TypeEnum.String }
                        }
                    },
                    Output = new TDArguments
                    {
                        Type = TypeEnum.Object,
                        Properties = new Dictionary<string, Property>
                        {
                            ["Result"] = new Property { Type = TypeEnum.String }
                        }
                    },
                    Forms =
                    [
                        new GenericForm
                        {
                            Href = firstSystemHref + "/Actions/ComputerSystem.Reset",
                            Type = TypeString.String,
                            PollingTime = 0
                        }
                    ]
                };
            }

            // 3) Managers collection: clock and firmware version (PP-F-003).
            string managersHref = serviceRoot["Managers"]?["@odata.id"]?.ToString();
            string firstManagerHref = ResolveFirstMemberHref(client, managersHref);
            if (!string.IsNullOrWhiteSpace(firstManagerHref))
            {
                AddProperty(td, "ManagerFirmwareVersion", TypeEnum.String, TypeString.String, true, firstManagerHref + "#/FirmwareVersion");
                AddProperty(td, "ManagerDateTime", TypeEnum.String, TypeString.DateTime, true, firstManagerHref + "#/DateTime");
                AddProperty(td, "ManagerHealth", TypeEnum.String, TypeString.String, true, firstManagerHref + "#/Status/Health");
            }

            // 4) Chassis collection: overall hardware health.
            string chassisHref = serviceRoot["Chassis"]?["@odata.id"]?.ToString();
            string firstChassisHref = ResolveFirstMemberHref(client, chassisHref);
            if (!string.IsNullOrWhiteSpace(firstChassisHref))
            {
                AddProperty(td, "ChassisHealth", TypeEnum.String, TypeString.String, true, firstChassisHref + "#/Status/Health");
                AddProperty(td, "ChassisType", TypeEnum.String, TypeString.String, true, firstChassisHref + "#/ChassisType");
            }

            return td;
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            unitId = 1; // not used for Redfish

            Uri uri;
            try
            {
                uri = new Uri(td.Base);
            }
            catch (Exception)
            {
                throw new Exception("Expected Redfish endpoint in the format redfish://host[:port] or https://host[:port]!");
            }

            string scheme = uri.Scheme?.ToLowerInvariant();
            if (scheme != "redfish" && scheme != "https" && scheme != "http")
            {
                throw new Exception("Expected redfish, https or http scheme in the endpoint address!");
            }

            string normalized = NormalizeBase(td.Base);

            RedfishAsset asset = new();
            asset.Connect(normalized, 0);

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
            GenericForm redfishForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = redfishForm.Href,
                UnitID = unitId,
                Type = redfishForm.Type.ToString(),
                PollingInterval = (int)redfishForm.PollingTime,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Normalize a Thing Description <c>base</c> value into a usable HTTP(S)
        /// base URL. The <c>redfish://</c> scheme always maps to HTTPS because
        /// Redfish-over-HTTP is explicitly discouraged by DMTF DSP0266 and
        /// would expose BMC credentials in the clear.
        /// </summary>
        public static string NormalizeBase(string assetEndpoint)
        {
            if (string.IsNullOrWhiteSpace(assetEndpoint))
            {
                throw new ArgumentException("Empty Redfish endpoint.", nameof(assetEndpoint));
            }

            var uri = new Uri(assetEndpoint);
            string httpScheme = uri.Scheme?.ToLowerInvariant() switch
            {
                "http" => "http",
                "https" => "https",
                "redfish" => "https",
                _ => "https"
            };

            string authority = uri.IsDefaultPort ? uri.Host : uri.Authority;
            return $"{httpScheme}://{authority}";
        }

        private static string ResolveFirstMemberHref(HttpClient client, string collectionHref)
        {
            if (string.IsNullOrWhiteSpace(collectionHref))
            {
                return null;
            }

            JObject collection = SafeGetJson(client, collectionHref.TrimStart('/'));
            var members = collection?["Members"] as JArray;
            if (members == null || members.Count == 0)
            {
                return null;
            }

            return members[0]?["@odata.id"]?.ToString();
        }

        private static JObject SafeGetJson(HttpClient client, string relativeUrl)
        {
            try
            {
                var response = client.GetAsync(relativeUrl).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JObject.Parse(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void AddProperty(
            ThingDescription td,
            string name,
            TypeEnum wotType,
            TypeString xsdType,
            bool readOnly,
            string href)
        {
            if (td.Properties.ContainsKey(name))
            {
                return;
            }

            td.Properties[name] = new Property
            {
                Type = wotType,
                ReadOnly = readOnly,
                Observable = true,
                Forms =
                [
                    new GenericForm
                    {
                        Href = href,
                        Op = readOnly
                            ? [Op.Readproperty, Op.Observeproperty]
                            : [Op.Readproperty, Op.Observeproperty, Op.Writeproperty],
                        Type = xsdType,
                        PollingTime = 10000
                    }
                ]
            };
        }
    }
}
