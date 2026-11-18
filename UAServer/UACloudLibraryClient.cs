namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Opc.Ua.Cloud.Library.Models;
    using Opc.Ua.Export;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;

    public class UACloudLibraryClient
    {
        private static readonly TimeSpan _httpTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _client = new HttpClient { Timeout = _httpTimeout };

        private readonly Dictionary<string, Tuple<string, string>> _namespacesInCloudLibrary = new();

        // Cache of (namespaceUrl -> nodeset XML) so repeated DownloadNodesetAsync
        // calls during onboarding don't re-scan every file under nodesets/ on disk
        // for every dependency lookup.
        private readonly Dictionary<string, string> _nodesetCache = new(StringComparer.OrdinalIgnoreCase);

        private readonly SemaphoreSlim _loginLock = new SemaphoreSlim(1, 1);

        private string _uaCloudLibraryUrl = Environment.GetEnvironmentVariable("UACLURL");

        private AuthenticationHeaderValue _authHeader;

        private async Task LoginAsync()
        {
            if (string.IsNullOrEmpty(_uaCloudLibraryUrl))
            {
                Log.Logger.Warning("UACLURL environment variable is not set.");
                return;
            }

            if (!_uaCloudLibraryUrl.EndsWith('/'))
            {
                _uaCloudLibraryUrl += "/";
            }

            string clientId = Environment.GetEnvironmentVariable("UACLUsername");
            if (string.IsNullOrEmpty(clientId))
            {
                Log.Logger.Warning("UACLUsername environment variable is not set.");
                return;
            }

            string secret = Environment.GetEnvironmentVariable("UACLPassword");
            if (string.IsNullOrEmpty(secret))
            {
                Log.Logger.Warning("UACLPassword environment variable is not set.");
                return;
            }

            // build a per-instance Authorization header rather than mutating
            // the shared DefaultRequestHeaders, which is not thread-safe.
            _authHeader = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + secret)));

            // get namespaces
            string address = _uaCloudLibraryUrl + "infomodel/namespaces";
            using HttpRequestMessage request = new(HttpMethod.Get, address) { Headers = { Authorization = _authHeader } };
            using HttpResponseMessage response = await _client.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Logger.Error(
                    "Cloud Library namespace listing failed with HTTP {StatusCode} ({ReasonPhrase}). URL: {Url}",
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    address);
                return;
            }

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            string[] identifiers;
            try
            {
                identifiers = JsonConvert.DeserializeObject<string[]>(body);
            }
            catch (JsonException ex)
            {
                Log.Logger.Error(ex, "Cloud Library returned a malformed namespace listing.");
                return;
            }

            if (identifiers != null)
            {
                foreach (string nodeset in identifiers)
                {
                    string[] tuple = nodeset.Split(",");

                    if (tuple.Length < 3)
                    {
                        continue;
                    }

                    if (_namespacesInCloudLibrary.TryGetValue(tuple[0], out Tuple<string, string> existing))
                    {
                        // only store the latest version of a given nodeset, using
                        // proper Version comparison instead of stripping dots.
                        if (TryParseVersion(existing.Item2, out Version existingVersion)
                         && TryParseVersion(tuple[2], out Version newVersion)
                         && existingVersion < newVersion)
                        {
                            _namespacesInCloudLibrary[tuple[0]] = new Tuple<string, string>(tuple[1], tuple[2]);
                        }
                    }
                    else
                    {
                        _namespacesInCloudLibrary.Add(tuple[0], new Tuple<string, string>(tuple[1], tuple[2]));
                    }
                }
            }
        }

        private bool TryParseVersion(string raw, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            // Version requires at least major.minor; pad single-segment versions.
            string normalized = raw.Contains('.') ? raw : raw + ".0";
            return Version.TryParse(normalized, out version);
        }

        public async Task<string> DownloadNodesetAsync(string namespaceUrl)
        {
            if (string.IsNullOrEmpty(namespaceUrl))
            {
                Log.Logger.Error("Namespace URL is null or empty.");
                return string.Empty;
            }

            // Fast-path: serve from in-memory cache before doing any I/O. The
            // onboarding workflow walks every namespace dependency at least
            // once, so without this each dependency would re-scan every file
            // under nodesets/.
            lock (_nodesetCache)
            {
                if (_nodesetCache.TryGetValue(namespaceUrl, out string cached))
                {
                    return cached;
                }
            }

            await EnsureLoggedInAsync().ConfigureAwait(false);

            string filePath = null;

            // check if we have the nodeset already downloaded
            string nodesetXml = GetDownloadedNodesetXml(namespaceUrl);
            if (string.IsNullOrEmpty(nodesetXml))
            {
                Log.Logger.Information("Nodeset {NamespaceUrl} not available locally, trying download from cloud library.", namespaceUrl);

                // check the cloud library if we have the nodeset available
                if (_namespacesInCloudLibrary.ContainsKey(namespaceUrl))
                {
                    try
                    {
                        string address = _uaCloudLibraryUrl + "infomodel/download/" + Uri.EscapeDataString(_namespacesInCloudLibrary[namespaceUrl].Item1);
                        using HttpRequestMessage request = new(HttpMethod.Get, address) { Headers = { Authorization = _authHeader } };
                        using HttpResponseMessage response = await _client.SendAsync(request).ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            Log.Logger.Error(
                                "Cloud Library download failed with HTTP {StatusCode} ({ReasonPhrase}). Namespace: {NamespaceUrl}",
                                (int)response.StatusCode,
                                response.ReasonPhrase,
                                namespaceUrl);
                            return string.Empty;
                        }

                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        UANameSpace nameSpace;
                        try
                        {
                            nameSpace = JsonConvert.DeserializeObject<UANameSpace>(body);
                        }
                        catch (JsonException ex)
                        {
                            Log.Logger.Error(ex, "Cloud Library returned a malformed nodeset payload for {NamespaceUrl}.", namespaceUrl);
                            return string.Empty;
                        }

                        if (!string.IsNullOrEmpty(nameSpace?.Nodeset?.NodesetXml))
                        {
                            filePath = await SaveLocalNodesetAsync(namespaceUrl, nameSpace.Nodeset.NodesetXml).ConfigureAwait(false);
                            Log.Logger.Information("Downloaded nodeset {NamespaceUrl} from cloud library.", namespaceUrl);

                            lock (_nodesetCache)
                            {
                                _nodesetCache[namespaceUrl] = nameSpace.Nodeset.NodesetXml;
                            }

                            return nameSpace.Nodeset.NodesetXml;
                        }
                        else
                        {
                            Log.Logger.Error("Nodeset {NamespaceUrl} not found in cloud library.", namespaceUrl);
                            return string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error(ex, "Could not download nodeset: {NamespaceUrl}", namespaceUrl);
                        return string.Empty;
                    }
                }
                else
                {
                    Log.Logger.Error("Nodeset {NamespaceUrl} not available in cloud library.", namespaceUrl);
                    return string.Empty;
                }
            }
            else
            {
                Log.Logger.Information("Nodeset {NamespaceUrl} already downloaded.", namespaceUrl);

                lock (_nodesetCache)
                {
                    _nodesetCache[namespaceUrl] = nodesetXml;
                }

                return nodesetXml;
            }
        }

        /// <summary>
        /// The namespace URIs of the nodesets available in the UA Cloud Library (latest
        /// version each), sorted. Empty when the Cloud Library is not configured
        /// (UACLURL, UACLUsername, UACLPassword) or cannot be reached.
        /// </summary>
        public async Task<IReadOnlyList<string>> GetNamespaceUrisAsync()
        {
            if (string.IsNullOrEmpty(_uaCloudLibraryUrl))
            {
                return [];
            }

            await EnsureLoggedInAsync().ConfigureAwait(false);

            // LoginAsync fills the dictionary under the login lock; copy it under the same lock.
            List<string> uris;
            await _loginLock.WaitAsync().ConfigureAwait(false);
            try
            {
                uris = new(_namespacesInCloudLibrary.Keys);
            }
            finally
            {
                _loginLock.Release();
            }

            uris.Sort(StringComparer.Ordinal);
            return uris;
        }

        /// <summary>
        /// The model URIs of the nodesets in the local nodesets folder, sorted. Only each
        /// file's model table is read, so large nodesets are not parsed in full.
        /// </summary>
        public static IReadOnlyList<string> GetLocalNamespaceUris()
        {
            SortedSet<string> uris = new(StringComparer.Ordinal);

            string nodesetsDir = Path.Combine(Directory.GetCurrentDirectory(), "nodesets");
            if (!Directory.Exists(nodesetsDir))
            {
                return [];
            }

            foreach (string file in Directory.GetFiles(nodesetsDir))
            {
                try
                {
                    using XmlReader reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Model")
                        {
                            string uri = reader.GetAttribute("ModelUri");
                            if (!string.IsNullOrEmpty(uri) && reader.Depth == 2)
                            {
                                uris.Add(uri);
                            }
                        }
                        else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Models")
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Debug(ex, "Could not read the model table of nodeset candidate file {File}; skipping.", file);
                }
            }

            return [.. uris];
        }

        /// <summary>
        /// The companion models that can be loaded: the ones in the local nodesets folder and,
        /// when <paramref name="cloudLibrary"/> is given and configured, the ones in the UA
        /// Cloud Library, sorted and without the OPC UA core namespace.
        /// </summary>
        public static async Task<IReadOnlyList<string>> GetCompanionModelUrisAsync(UACloudLibraryClient cloudLibrary)
        {
            SortedSet<string> uris = new(GetLocalNamespaceUris(), StringComparer.Ordinal);

            if (cloudLibrary != null)
            {
                foreach (string uri in await cloudLibrary.GetNamespaceUrisAsync().ConfigureAwait(false))
                {
                    uris.Add(uri);
                }
            }

            uris.Remove(Namespaces.OpcUa);
            return [.. uris];
        }

        /// <summary>
        /// Stores an uploaded NodeSet2 document in the local nodesets folder, named after the
        /// first model it defines, and returns the model URIs it defines. Throws when the
        /// document is not a NodeSet2 document or declares no model.
        /// </summary>
        public static async Task<IReadOnlyList<string>> SaveLocalNodesetAsync(byte[] nodeSetXml)
        {
            ArgumentNullException.ThrowIfNull(nodeSetXml);

            UANodeSet nodeSet;
            using (MemoryStream read = new(nodeSetXml, writable: false))
            {
                nodeSet = UANodeSet.Read(read);
            }

            List<string> models = [];
            foreach (ModelTableEntry model in nodeSet.Models ?? [])
            {
                if (!string.IsNullOrEmpty(model.ModelUri))
                {
                    models.Add(model.ModelUri);
                }
            }

            if (models.Count == 0)
            {
                throw new InvalidOperationException("The NodeSet2 document does not declare a model.");
            }

            string xml;
            using (StreamReader reader = new(new MemoryStream(nodeSetXml, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                xml = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            await SaveLocalNodesetAsync(models[0], xml).ConfigureAwait(false);
            return models;
        }

        /// <summary>
        /// Stores a nodeset in the local nodesets folder, where <see cref="DownloadNodesetAsync"/>
        /// finds it before asking the Cloud Library. The file is named after the namespace, so
        /// storing a namespace again replaces its file. Returns the file path.
        /// </summary>
        public static async Task<string> SaveLocalNodesetAsync(string namespaceUrl, string nodesetXml)
        {
            Uri nodeSetUri = new Uri(namespaceUrl);
            string fileName = (nodeSetUri.Host + nodeSetUri.PathAndQuery).TrimEnd('/').Replace('/', '.');
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            string nodesetsDir = Path.Combine(Directory.GetCurrentDirectory(), "nodesets");
            Directory.CreateDirectory(nodesetsDir);

            string filePath = Path.Combine(nodesetsDir, fileName + ".nodeset2.xml");
            await File.WriteAllTextAsync(filePath, nodesetXml).ConfigureAwait(false);
            return filePath;
        }

        private async Task EnsureLoggedInAsync()
        {
            if (_namespacesInCloudLibrary.Count > 0)
            {
                return;
            }

            await _loginLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_namespacesInCloudLibrary.Count == 0)
                {
                    await LoginAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _loginLock.Release();
            }
        }

        public string GetDownloadedNodesetXml(string namespaceUrl)
        {
            if (string.IsNullOrEmpty(namespaceUrl))
            {
                Log.Logger.Error("Namespace URL is null or empty.");
                return string.Empty;
            }

            string nodesetsDir = Path.Combine(Directory.GetCurrentDirectory(), "nodesets");
            if (!Directory.Exists(nodesetsDir))
            {
                return string.Empty;
            }

            foreach (string file in Directory.GetFiles(nodesetsDir))
            {
                try
                {
                    using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    UANodeSet nodeSet = UANodeSet.Read(stream);
                    if ((nodeSet.Models != null) && (nodeSet.Models.Length > 0))
                    {
                        foreach (ModelTableEntry te in nodeSet.Models)
                        {
                            if (te.ModelUri == namespaceUrl)
                            {
                                return File.ReadAllText(file);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Skip non-nodeset files (or corrupted ones) instead of
                    // tearing down the whole onboarding pipeline because of a
                    // single bad file in the directory.
                    Log.Logger.Warning(ex, "Could not parse nodeset candidate file {File}; skipping.", file);
                }
            }

            Log.Logger.Error("Nodeset {NamespaceUrl} not found.", namespaceUrl);
            return string.Empty;
        }
    }
}
