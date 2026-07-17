using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WotOpcUaMapper.Services;

namespace WotOpcUaMapper.Services
{
    /// <summary>
    /// Represents a nodeset entry available in the UA Cloud Library.
    /// </summary>
    public class CloudLibNodeset
    {
        public string Identifier { get; set; } = string.Empty;
        public string NamespaceUri { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Version { get; set; }
    }

    /// <summary>
    /// Thin HTTP client for the UA Cloud Library REST API. Uses the configurable
    /// base URL and basic-auth credentials from <see cref="SettingsService"/>.
    /// </summary>
    public class CloudLibraryClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SettingsService _settings;

        public CloudLibraryClient(IHttpClientFactory httpClientFactory, SettingsService settings)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings;
        }

        private HttpClient CreateClient()
        {
            var cfg = _settings.Current;
            var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(cfg.CloudLibraryUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(2);

            if (!string.IsNullOrEmpty(cfg.UserName))
            {
                var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{cfg.UserName}:{cfg.Password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            return client;
        }

        /// <summary>
        /// Returns the information models available in the Cloud Library. The friendly names come
        /// from the lightweight /infomodel/names endpoint and the namespace URIs (used for the
        /// drop-down tooltip) from /infomodel/namespaces, joined by identifier. When
        /// <paramref name="enrichMetadata"/> is true and one or more names appear more than once,
        /// the heavier /infomodel/find endpoint is called only then to attach the version needed
        /// to tell the duplicates apart.
        /// </summary>
        public async Task<List<CloudLibNodeset>> GetNamespacesAsync(bool enrichMetadata = false)
        {
            // Fetch the friendly names and namespace URIs in parallel (both are cheap string lists).
            var namesTask = GetFromNamesAsync();
            var namespacesTask = GetFromNamespacesAsync();
            await Task.WhenAll(namesTask, namespacesTask).ConfigureAwait(false);

            var result = namesTask.Result;

            // Join the namespace URI onto each entry by identifier so it can be shown as a tooltip.
            var uriById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ns in namespacesTask.Result)
            {
                uriById[ns.Identifier] = ns.NamespaceUri;
            }
            foreach (var entry in result)
            {
                if (uriById.TryGetValue(entry.Identifier, out var uri))
                {
                    entry.NamespaceUri = uri;
                }
            }

            // Only pay for the richer /infomodel/find call when there is actually an ambiguity to
            // resolve, i.e. at least one name is listed more than once.
            var hasDuplicates = result
                .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1);

            if (enrichMetadata && hasDuplicates)
            {
                await TryEnrichVersionsAsync(result).ConfigureAwait(false);
            }

            // Build a friendly, unambiguous label for every entry.
            foreach (var group in result.GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
            {
                var isAmbiguous = group.Count() > 1;
                foreach (var entry in group)
                {
                    entry.Title = BuildLabel(entry, isAmbiguous);
                }
            }

            return result
                .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Retrieves the lightweight name listing from /infomodel/names. The endpoint returns an
        /// array of strings formatted as "name,identifier".
        /// </summary>
        private async Task<List<CloudLibNodeset>> GetFromNamesAsync()
        {
            var result = new List<CloudLibNodeset>();

            try
            {
                using var client = CreateClient();
                var lines = await client.GetFromJsonAsync<string[]>("infomodel/names").ConfigureAwait(false);
                if (lines != null)
                {
                    foreach (var line in lines)
                    {
                        // Format: "name,identifier". The name itself may contain commas, so split
                        // on the last comma to isolate the trailing identifier.
                        var sep = line.LastIndexOf(',');
                        if (sep <= 0 || sep >= line.Length - 1)
                        {
                            continue;
                        }

                        var name = line[..sep].Trim();
                        var id = line[(sep + 1)..].Trim();
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id))
                        {
                            continue;
                        }

                        result.Add(new CloudLibNodeset
                        {
                            Identifier = id,
                            Name = name,
                            Title = name
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetFromNamesAsync failed: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Retrieves the lightweight namespace listing from /infomodel/namespaces. The endpoint
        /// returns an array of strings formatted as "namespaceUri,identifier". Used only for
        /// dependency resolution (namespace URI -&gt; identifier).
        /// </summary>
        private async Task<List<CloudLibNodeset>> GetFromNamespacesAsync()
        {
            var result = new List<CloudLibNodeset>();

            try
            {
                using var client = CreateClient();
                var lines = await client.GetFromJsonAsync<string[]>("infomodel/namespaces").ConfigureAwait(false);
                if (lines != null)
                {
                    foreach (var line in lines)
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var uri = parts[0].Trim();
                            var id = parts[1].Trim();
                            result.Add(new CloudLibNodeset
                            {
                                NamespaceUri = uri,
                                Identifier = id,
                                Title = DeriveTitle(uri)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetFromNamespacesAsync failed: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Composes the drop-down label from the friendly information-model name. When several
        /// entries share the same name, the version is appended so they are distinguishable
        /// (falling back to the identifier if the version is unknown).
        /// </summary>
        private static string BuildLabel(CloudLibNodeset entry, bool isAmbiguous)
        {
            var label = string.IsNullOrWhiteSpace(entry.Name) ? entry.Title : entry.Name!;

            if (!isAmbiguous)
            {
                return label;
            }

            // Prefer the version as the disambiguator; fall back to the identifier if unknown.
            var qualifier = !string.IsNullOrWhiteSpace(entry.Version)
                ? "v" + entry.Version
                : "#" + entry.Identifier;

            return $"{label} — {qualifier}";
        }

        /// <summary>
        /// Enriches the supplied entries with their version (and friendly name) using the
        /// /infomodel/find endpoint, which returns UANodesetResult objects carrying the title,
        /// version and identifier for each nodeset. Values are matched back to the entries by
        /// identifier. Paginated so the full catalogue is retrieved. Failures are swallowed so the
        /// plain namespace listing continues to work.
        /// </summary>
        private async Task TryEnrichVersionsAsync(List<CloudLibNodeset> entries)
        {
            try
            {
                using var client = CreateClient();

                var metadataById = new Dictionary<string, (string? Name, string? Version)>(StringComparer.OrdinalIgnoreCase);
                const int pageSize = 100;
                var offset = 0;

                while (true)
                {
                    // "*" returns every information model with its metadata; page through the
                    // results so the full list is retrieved regardless of the server's default limit.
                    var body = await client
                        .GetStringAsync($"infomodel/find?keywords=*&offset={offset}&limit={pageSize}")
                        .ConfigureAwait(false);

                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        break;
                    }

                    var count = 0;
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        count++;

                        var id = ReadIdentifier(item);
                        if (string.IsNullOrEmpty(id))
                        {
                            continue;
                        }

                        var name = ReadString(item, "title");
                        var version = ReadString(item, "version")
                                      ?? ReadNestedString(item, "nodeset", "version");

                        metadataById[id!] = (name, version);
                    }

                    if (count < pageSize)
                    {
                        break; // last page reached
                    }

                    offset += pageSize;
                }

                foreach (var entry in entries)
                {
                    if (metadataById.TryGetValue(entry.Identifier, out var meta))
                    {
                        entry.Name = meta.Name;
                        entry.Version = meta.Version;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("TryEnrichVersionsAsync failed (ignored): " + ex.Message);
            }
        }

        private static string? ReadString(JsonElement item, string property) =>
            item.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;

        private static string? ReadNestedString(JsonElement item, string parent, string property) =>
            item.TryGetProperty(parent, out var parentEl) && parentEl.ValueKind == JsonValueKind.Object
                ? ReadString(parentEl, property)
                : null;

        /// <summary>
        /// Reads the Cloud Library identifier from a UANodesetResult element, which exposes it as
        /// the top-level "nodesetId" and also as the nested "nodeset.identifier".
        /// </summary>
        private static string? ReadIdentifier(JsonElement item)
        {
            if (item.TryGetProperty("nodesetId", out var idEl))
            {
                return idEl.ValueKind == JsonValueKind.Number
                    ? idEl.GetInt64().ToString()
                    : idEl.GetString();
            }

            if (item.TryGetProperty("nodeset", out var nodesetEl) &&
                nodesetEl.ValueKind == JsonValueKind.Object &&
                nodesetEl.TryGetProperty("identifier", out var nestedIdEl))
            {
                return nestedIdEl.ValueKind == JsonValueKind.Number
                    ? nestedIdEl.GetInt64().ToString()
                    : nestedIdEl.GetString();
            }

            return null;
        }

        /// <summary>
        /// Finds the identifier for a nodeset by its namespace URI (used for dependency resolution).
        /// </summary>
        public async Task<string?> FindIdentifierByNamespaceAsync(string namespaceUri)
        {
            var all = await GetFromNamespacesAsync().ConfigureAwait(false);
            var match = all.FirstOrDefault(n => string.Equals(n.NamespaceUri.TrimEnd('/'), namespaceUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            return match?.Identifier;
        }

        /// <summary>
        /// Downloads the raw NodeSet2 XML for the given Cloud Library identifier.
        /// </summary>
        public async Task<string?> DownloadNodesetXmlAsync(string identifier)
        {
            try
            {
                using var client = CreateClient();
                // Try the raw-XML-only variant first.
                var response = await client.GetAsync($"infomodel/download/{identifier}?nodesetXMLOnly=true").ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var xml = ExtractXml(content);
                    if (!string.IsNullOrEmpty(xml))
                    {
                        return xml;
                    }
                }

                // Fall back to the full metadata payload and extract the embedded XML.
                var full = await client.GetStringAsync($"infomodel/download/{identifier}").ConfigureAwait(false);
                return ExtractXml(full);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DownloadNodesetXmlAsync({identifier}) failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The download endpoint may return either raw XML or a JSON UANameSpace
        /// object containing the XML under Nodeset.NodesetXml. Handle both.
        /// </summary>
        private static string? ExtractXml(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var trimmed = content.TrimStart();
            if (trimmed.StartsWith("<"))
            {
                return content;
            }

            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("nodeset", out var nodeset) &&
                    nodeset.TryGetProperty("nodesetXml", out var xmlElem))
                {
                    return xmlElem.GetString();
                }

                // Some servers return the raw XML wrapped in a JSON string.
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    return doc.RootElement.GetString();
                }
            }
            catch
            {
                // Not JSON: assume the content itself is XML.
                return content;
            }

            return null;
        }

        private static string DeriveTitle(string namespaceUri)
        {
            var uri = namespaceUri.TrimEnd('/');
            var idx = uri.LastIndexOf('/');
            var name = idx >= 0 && idx < uri.Length - 1 ? uri[(idx + 1)..] : uri;
            return $"{name} ({namespaceUri})";
        }
    }
}
