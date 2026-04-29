namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;

    public class PIAsset : IAsset
    {
        private string _baseUrl = string.Empty;
        private readonly HttpClient _client = new();

        // Cache AF/PI path -> WebId to avoid repeated lookups
        private readonly ConcurrentDictionary<string, (string WebId, DateTimeOffset Expires)> _webIdCache = new();
        private static readonly TimeSpan _cacheTtl = TimeSpan.FromHours(12);

        public bool IsConnected { get; private set; } = false;

        public void Connect(string ipAddress, int port)
        {
            try
            {
                // Same pattern as HTTPClientAsset, but PIWebApiProtocolDriver will pass full Base already.
                if (port > 0)
                {
                    _baseUrl = $"https://{ipAddress}:{port}";
                }
                else
                {
                    _baseUrl = ipAddress;
                }

                _baseUrl = _baseUrl.TrimEnd('/');

                ApplyAuthFromEnvironment(_client);

                // PI Web API may not support HEAD everywhere; try HEAD then fallback to GET.
                var head = new HttpRequestMessage(HttpMethod.Head, _baseUrl);
                var headResp = _client.Send(head);

                if (!headResp.IsSuccessStatusCode)
                {
                    // Fallback GET (root usually returns a JSON document with links)
                    var get = new HttpRequestMessage(HttpMethod.Get, _baseUrl);
                    var getResp = _client.Send(get);
                    getResp.EnsureSuccessStatusCode();
                }

                IsConnected = true;
                Log.Logger.Information("Connected to PI Web API endpoint at " + _baseUrl);
            }
            catch (Exception ex)
            {
                IsConnected = false;
                Log.Logger.Error(ex.Message, ex);
                throw;
            }
        }

        public void Disconnect()
        {
            IsConnected = false;
        }

        public string GetRemoteEndpoint()
        {
            return _baseUrl;
        }

        public object Read(AssetTag tag)
        {
            try
            {
                // Resolve a PI “address” into a relative PI Web API URL to GET
                string relativeUrl = ResolveReadRelativeUrl(tag);

                string url = _baseUrl.TrimEnd('/') + "/" + relativeUrl.TrimStart('/');

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = _client.Send(request);
                response.EnsureSuccessStatusCode();

                string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                // Many PI Web API endpoints return JSON objects. For streams/{webId}/value,
                // the numeric value is in JSON field "Value".
                // If content is a raw scalar, we still support parsing like HTTPClientAsset.
                if (LooksLikeJson(content))
                {
                    var j = JObject.Parse(content);
                    var valueToken = j["Value"];
                    if (valueToken != null)
                    {
                        return ConvertTyped(valueToken.ToString(), tag.Type);
                    }

                    // If it’s JSON but not a "Value" object, return the full JSON string
                    return content;
                }

                return ConvertTyped(content, tag.Type);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return null;
            }
        }

        public void Write(AssetTag tag, object value)
        {
            try
            {
                // For PI Web API, writing a value is POST to /streams/{webId}/value
                // Payload: { "Timestamp":"*", "Value": <value> }
                string webId = ResolveWebId(tag.Address);

                string url = _baseUrl.TrimEnd('/') + "/streams/" + Uri.EscapeDataString(webId) + "/value";

                string payload = BuildPiValuePayload(value.ToString(), tag.Type);

                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                var response = _client.Send(request);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            try
            {
                // PI Web API doesn’t have a universal “action” concept like generic HTTP endpoints,
                // but we can keep the same pattern: POST to {Base}/{ActionName}.
                string actionName = method.BrowseName.Name;
                string url = _baseUrl.TrimEnd('/') + "/" + actionName.TrimStart('/');

                string body = string.Empty;
                if (inputArgs != null && inputArgs.Count > 0)
                {
                    body = inputArgs[0]?.ToString() ?? string.Empty;
                }

                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                var response = _client.Send(request);
                response.EnsureSuccessStatusCode();

                string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                outputArgs = new List<object> { result };

                return result;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return null;
            }
        }

        // ---------------------------
        // Helpers
        // ---------------------------

        private string ResolveReadRelativeUrl(AssetTag tag)
        {
            // If tag.Address is already a PI Web API relative URL, use it as-is.
            // e.g. "streams/{webId}/value", "streamsets/{webId}", etc.
            if (!string.IsNullOrWhiteSpace(tag.Address) &&
                (tag.Address.StartsWith("streams/", StringComparison.OrdinalIgnoreCase) ||
                 tag.Address.StartsWith("streamsets/", StringComparison.OrdinalIgnoreCase) ||
                 tag.Address.StartsWith("attributes?", StringComparison.OrdinalIgnoreCase) ||
                 tag.Address.StartsWith("points?", StringComparison.OrdinalIgnoreCase)))
            {
                // If it’s a direct streams/... endpoint, great.
                // If it’s attributes?path=... caller will get a WebId JSON, not a scalar "Value" response.
                // Recommended: use AF/PI path instead of raw attributes?... in tag.Address.
                return tag.Address;
            }

            // Otherwise treat as AF attribute path or PI point path or WebId
            string webId = ResolveWebId(tag.Address);

            // Default to snapshot/current value
            return $"streams/{Uri.EscapeDataString(webId)}/value";
        }

        private string ResolveWebId(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new Exception("PI address is empty.");
            }

            // If it looks like a WebId (heuristic), accept it
            if (address.Length > 25 && !address.Contains("\\") && !address.Contains("|") && !address.Contains("/"))
            {
                return address;
            }

            // Cached?
            var now = DateTimeOffset.UtcNow;
            if (_webIdCache.TryGetValue(address, out var cached) && cached.Expires > now)
            {
                return cached.WebId;
            }

            // AF attribute path: contains '|'
            if (address.Contains("|", StringComparison.Ordinal))
            {
                string rel = "attributes?path=" + Uri.EscapeDataString(address);
                string json = _client.GetStringAsync(_baseUrl.TrimEnd('/') + "/" + rel).GetAwaiter().GetResult();
                string webId = JObject.Parse(json)["WebId"]?.ToString();
                if (string.IsNullOrWhiteSpace(webId))
                    throw new Exception("PI Web API did not return WebId for attribute path: " + address);

                _webIdCache[address] = (webId, now.Add(_cacheTtl));
                return webId;
            }

            // PI point path: contains '\'
            if (address.Contains("\\", StringComparison.Ordinal))
            {
                string rel = "points?path=" + Uri.EscapeDataString(address);
                string json = _client.GetStringAsync(_baseUrl.TrimEnd('/') + "/" + rel).GetAwaiter().GetResult();
                string webId = JObject.Parse(json)["WebId"]?.ToString();
                if (string.IsNullOrWhiteSpace(webId))
                    throw new Exception("PI Web API did not return WebId for point path: " + address);

                _webIdCache[address] = (webId, now.Add(_cacheTtl));
                return webId;
            }

            throw new Exception("Unsupported PI address format. Use WebId, AF path (\\\\AFServer\\\\Db\\\\Elem|Attr), or PI point path (\\\\PIServer\\\\Tag).");
        }

        private static bool LooksLikeJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            content = content.Trim();
            return (content.StartsWith("{") && content.EndsWith("}")) || (content.StartsWith("[") && content.EndsWith("]"));
        }

        private static object ConvertTyped(string content, string type)
        {
            content = (content ?? string.Empty).Trim();

            if (type == "Float")
            {
                return float.Parse(content, System.Globalization.CultureInfo.InvariantCulture);
            }
            if (type == "Boolean")
            {
                return bool.Parse(content);
            }
            if (type == "Integer")
            {
                return int.Parse(content, System.Globalization.CultureInfo.InvariantCulture);
            }

            return content;
        }

        private static string BuildPiValuePayload(string value, string type)
        {
            // PI Web API expects JSON object with Timestamp and Value
            // Timestamp "*" means “now”
            // If your value is numeric/bool we should not quote it.
            string jsonValue;
            if (type == "Float" || type == "Integer")
            {
                jsonValue = value; // assume already numeric string
            }
            else if (type == "Boolean")
            {
                jsonValue = value.ToLowerInvariant();
            }
            else
            {
                jsonValue = "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }

            return $"{{\"Timestamp\":\"*\",\"Value\":{jsonValue}}}";
        }

        public void ApplyAuthFromEnvironment(HttpClient client)
        {
            // Optional (keeps TD simple):
            // PIWEBAPI_AUTH=basic|bearer
            // PIWEBAPI_USERNAME / PIWEBAPI_PASSWORD
            // PIWEBAPI_BEARER_TOKEN
            var mode = (Environment.GetEnvironmentVariable("PIWEBAPI_AUTH") ?? "").Trim().ToLowerInvariant();
            client.DefaultRequestHeaders.Authorization = null;

            if (mode == "basic")
            {
                var user = Environment.GetEnvironmentVariable("PIWEBAPI_USERNAME");
                var pass = Environment.GetEnvironmentVariable("PIWEBAPI_PASSWORD");

                if (string.IsNullOrWhiteSpace(user) || pass == null)
                {
                    throw new Exception("PIWEBAPI_AUTH=basic requires PIWEBAPI_USERNAME and PIWEBAPI_PASSWORD");
                }

                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
            else if (mode == "bearer")
            {
                var token = Environment.GetEnvironmentVariable("PIWEBAPI_BEARER_TOKEN");
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new Exception("PIWEBAPI_AUTH=bearer requires PIWEBAPI_BEARER_TOKEN");
                }

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }
    }
}
