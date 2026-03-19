namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.Net.Http;

    public class PIProtocolDriver : IProtocolDriver
    {
        public string Scheme => "piwebapi";

        // HTTP-based, so the HTTP WoT binding template URI is appropriate.
        public string WoTBindingUri => "https://www.w3.org/2011/http";

        public IEnumerable<string> Discover()
        {
            // PI Web API does not support discovery
            return new List<string>();
        }

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            // assetEndpoint example:
            // https://piwebapi.company.com/piwebapi#\\AFSERVER\\MyDb\\PlantA

            var (baseUrl, rootAfPath) = ParseEndpoint(assetEndpoint);

            if (string.IsNullOrWhiteSpace(rootAfPath))
            {
                throw new Exception("PI Web API Browse requires AF root path in endpoint fragment.");
            }

            var td = new ThingDescription()
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1"],
                Id = "urn:" + assetName,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Base = baseUrl,
                Title = assetName,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            using var client = new HttpClient();
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

            // Apply authentication from environment
            var asset = new PIAsset();
            asset.ApplyAuthFromEnvironment(client);

            // 1) Resolve root element WebId
            var rootElement = GetJson(client, $"elements?path={Uri.EscapeDataString(rootAfPath)}");

            string rootWebId = rootElement["WebId"]?.ToString() ?? throw new Exception("Failed to resolve AF root element.");

            // 2) Walk AF hierarchy recursively
            WalkElement(client, rootWebId, prefix: null, td.Properties);

            return td;
        }

        private void WalkElement(
            HttpClient client,
            string elementWebId,
            string prefix,
            IDictionary<string, Property> properties)
        {
            // Get element details (name)
            var element = GetJson(client, $"elements/{elementWebId}");
            var elementName = element["Name"]?.ToString();

            var currentPrefix = string.IsNullOrEmpty(prefix) ? elementName : $"{prefix}.{elementName}";

            // 1) Get attributes of this element
            var attrs = GetJson(client, $"elements/{elementWebId}/attributes?searchFullHierarchy=false");
            if ((attrs != null) && (attrs["Items"] != null))
            {
                foreach (var attr in attrs["Items"])
                {
                    var attrName = attr["Name"]?.ToString();
                    var attrWebId = attr["WebId"]?.ToString();

                    if (string.IsNullOrWhiteSpace(attrName) || string.IsNullOrWhiteSpace(attrWebId))
                        continue;

                    var dotName = SanitizeDotName($"{currentPrefix}.{attrName}");

                    if (!properties.ContainsKey(dotName))
                    {
                        properties[dotName] = new Property()
                        {
                            Type = TypeEnum.Number, // default; UA mapping will refine
                            Forms =
                            [
                                new GenericForm()
                        {
                            Href = $"streams/{attrWebId}/value",
                            Type = TypeString.Float,
                            PollingTime = 1000
                        }
                            ]
                        };
                    }
                }
            }

            // 2) Recurse into child elements
            var children = GetJson(client, $"elements/{elementWebId}/elements");
            if ((children != null) && (children["Items"] != null))
            {
                foreach (var child in children["Items"])
                {
                    var childWebId = child["WebId"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(childWebId))
                    {
                        WalkElement(client, childWebId, currentPrefix, properties);
                    }
                }
            }
        }

        private static (string baseUrl, string afPath) ParseEndpoint(string endpoint)
        {
            var uri = new Uri(endpoint);
            var baseUrl = uri.GetLeftPart(UriPartial.Path);
            var afPath = uri.Fragment?.TrimStart('#');
            return (baseUrl, afPath);
        }

        private static JObject GetJson(HttpClient client, string relativeUrl)
        {
            var response = client.GetAsync(relativeUrl).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return JObject.Parse(json);
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
                throw new Exception("Expected PI Web API endpoint in the format piwebapi(s)://host:port/piwebapi or https://host:port/piwebapi !");
            }

            // Accept piwebapi/piwebapis as explicit scheme; optionally accept http/https too.
            string scheme = uri.Scheme?.ToLowerInvariant();
            if (scheme != "piwebapi" && scheme != "piwebapis" && scheme != "http" && scheme != "https")
            {
                throw new Exception("Expected piwebapi, piwebapis, http or https scheme in the endpoint address!");
            }

            // Normalize to an actual HTTP base URL inside the asset.
            // piwebapis -> https, piwebapi -> http (but many will still use https; you can choose)
            string httpScheme = scheme == "piwebapis" ? "https" :
                                scheme == "piwebapi" ? "http" :
                                scheme;

            // Rebuild a normalized base for PI Web API (ensure /piwebapi is present)
            string basePath = uri.AbsolutePath;
            if (string.IsNullOrEmpty(basePath) || basePath == "/")
            {
                basePath = "/piwebapi";
            }
            else if (!basePath.TrimEnd('/').EndsWith("/piwebapi", StringComparison.OrdinalIgnoreCase))
            {
                // if someone points to server root, append /piwebapi
                basePath = basePath.TrimEnd('/') + "/piwebapi";
            }

            string normalizedBase = $"{httpScheme}://{uri.Authority}{basePath.TrimEnd('/')}";

            PIAsset asset = new();
            asset.Connect(normalizedBase, 0);

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

            // Default to the UA variableId coming from the mapping layer
            var name = variableId;

            // If the address is an AF path, derive dot-notation name from it
            if (!string.IsNullOrWhiteSpace(httpForm?.Href))
            {
                var derived = DotNameFromAddress(httpForm.Href);
                if (!string.IsNullOrWhiteSpace(derived))
                {
                    name = derived;
                }
            }

            return new AssetTag()
            {
                Name = name,
                Address = httpForm.Href,
                UnitID = unitId,
                Type = httpForm.Type.ToString(),
                PollingInterval = (int)httpForm.PollingTime,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }

        private static string DotNameFromAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            address = address.Trim();

            // Case A: AF Attribute path: \\Server\Db\Folder1\Folder2\Element|Attribute
            // Convert to: Folder1.Folder2.Element.Attribute
            if (address.StartsWith(@"\\") && address.Contains('|'))
            {
                // split "left|right"
                var parts = address.Split('|', 2);
                var left = parts[0];              // \\Server\Db\...\Element
                var attr = parts.Length > 1 ? parts[1] : null;

                // Remove leading '\\' and split by '\'
                var leftParts = left.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);

                // leftParts[0] = Server, leftParts[1] = Database, leftParts[2..] = AF element path
                if (leftParts.Length >= 3)
                {
                    var elementPathParts = leftParts[2..]; // drop server+db
                    var dot = string.Join('.', elementPathParts);

                    if (!string.IsNullOrWhiteSpace(attr))
                    {
                        dot = dot + "." + attr;
                    }

                    return SanitizeDotName(dot);
                }

                // If not enough segments, fall back
                return SanitizeDotName(attr ?? address);
            }

            // Case B: PI Point path: \\PIServer\Tag
            // Convert to: Tag (or optionally include some hierarchy if you ever use it)
            if (address.StartsWith(@"\\") && !address.Contains('|'))
            {
                var parts = address.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    // parts[0] = Server, parts[1] = Tag (sometimes more segments)
                    var tagParts = parts[1..];
                    return SanitizeDotName(string.Join('.', tagParts));
                }

                return SanitizeDotName(address);
            }

            // Case C: already a PI Web API relative endpoint like "streams/{webId}/value"
            // There's no hierarchy to derive reliably. Return null to keep variableId.
            if (address.StartsWith("streams/", StringComparison.OrdinalIgnoreCase) ||
                address.StartsWith("streamsets/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Case D: WebId (opaque) - cannot derive a nice name; keep variableId
            return null;
        }

        private static string SanitizeDotName(string dotName)
        {
            if (string.IsNullOrWhiteSpace(dotName))
            {
                return dotName;
            }

            // Keep dots, letters, digits, underscore, dash.
            // Convert everything else to underscore.
            var sb = new System.Text.StringBuilder(dotName.Length);
            foreach (var ch in dotName)
            {
                if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-')
                {
                    sb.Append(ch);
                }
                else
                {
                    sb.Append('_');
                }
            }

            // Avoid leading/trailing dots and multiple dots
            var cleaned = sb.ToString().Trim('.');
            while (cleaned.Contains(".."))
            {
                cleaned = cleaned.Replace("..", ".");
            }

            return cleaned;
        }
    }
}
