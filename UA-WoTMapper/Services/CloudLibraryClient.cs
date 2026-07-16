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
        /// Returns all namespaces available in the Cloud Library as a map of
        /// namespaceUri -&gt; identifier via the /infomodel/namespaces endpoint.
        /// </summary>
        public async Task<List<CloudLibNodeset>> GetNamespacesAsync()
        {
            var result = new List<CloudLibNodeset>();

            try
            {
                using var client = CreateClient();
                // The endpoint returns an array of strings formatted as "namespaceUri,identifier".
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
                Console.WriteLine("GetNamespacesAsync failed: " + ex.Message);
            }

            return result.OrderBy(n => n.Title).ToList();
        }

        /// <summary>
        /// Finds the identifier for a nodeset by its namespace URI.
        /// </summary>
        public async Task<string?> FindIdentifierByNamespaceAsync(string namespaceUri)
        {
            var all = await GetNamespacesAsync().ConfigureAwait(false);
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
