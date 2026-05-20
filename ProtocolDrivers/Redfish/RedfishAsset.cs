namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;

    /// <summary>
    /// Runtime asset for DMTF Redfish managed devices (servers, BMCs, chassis).
    /// Implements <see cref="IAsset"/> by issuing standard Redfish REST calls:
    /// GET for property reads (with optional JSON Pointer extraction), PATCH
    /// for property writes, and POST for Redfish Actions.
    /// </summary>
    public class RedfishAsset : IAsset
    {
        private string _baseUrl = string.Empty;
        private readonly HttpClient _client = new();

        public bool IsConnected { get; private set; } = false;

        public void Connect(string ipAddress, int port)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ipAddress))
                {
                    throw new ArgumentException("Redfish endpoint host is empty.", nameof(ipAddress));
                }

                // If the caller passed a full URL keep it; otherwise default to https.
                if (ipAddress.Contains("://", StringComparison.Ordinal))
                {
                    _baseUrl = ipAddress.TrimEnd('/');
                }
                else if (port > 0)
                {
                    _baseUrl = $"https://{ipAddress}:{port}";
                }
                else
                {
                    _baseUrl = $"https://{ipAddress}";
                }

                ApplyAuthFromEnvironment(_client);

                // Verify connectivity by reading the Redfish service root.
                var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "/redfish/v1/");
                var response = _client.Send(request);
                response.EnsureSuccessStatusCode();

                IsConnected = true;
                Log.Logger.Information("Connected to Redfish service at " + _baseUrl);
            }
            catch (Exception ex)
            {
                IsConnected = false;
                Log.Logger.Error(ex.Message, ex);
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
                if (string.IsNullOrWhiteSpace(tag?.Address))
                {
                    return null;
                }

                // Split <resourcePath>#<jsonPointer>.
                SplitAddress(tag.Address, out string resourcePath, out string jsonPointer);

                string url = _baseUrl.TrimEnd('/') + "/" + resourcePath.TrimStart('/');

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = _client.Send(request);
                response.EnsureSuccessStatusCode();

                string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                JToken token;
                try
                {
                    JObject json = JObject.Parse(content);
                    token = string.IsNullOrEmpty(jsonPointer)
                        ? json
                        : json.SelectToken(JsonPointerToJsonPath(jsonPointer));
                }
                catch (Exception)
                {
                    return ConvertTyped(content, tag.Type);
                }

                if (token == null)
                {
                    return null;
                }

                return ConvertTyped(token.Type == JTokenType.String ? token.ToString() : token.ToString(Newtonsoft.Json.Formatting.None), tag.Type);
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
                if (string.IsNullOrWhiteSpace(tag?.Address) || value == null)
                {
                    return;
                }

                SplitAddress(tag.Address, out string resourcePath, out string jsonPointer);
                if (string.IsNullOrEmpty(jsonPointer))
                {
                    throw new InvalidOperationException(
                        "Redfish writes require a JSON Pointer fragment in the form address (e.g. '/redfish/v1/Systems/1#/IndicatorLED').");
                }

                string url = _baseUrl.TrimEnd('/') + "/" + resourcePath.TrimStart('/');

                // Build the smallest patch document covering only the addressed field.
                JObject patch = BuildPatchDocument(jsonPointer, value, tag.Type);

                var content = new StringContent(patch.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
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
                // The action's address is taken from the method's BrowseName fall-back, or
                // for Redfish from a dedicated form already mapped onto the OPC UA method
                // by AddNodeForWoTForm (Href is passed through tag.Address elsewhere).
                string actionName = method.BrowseName.Name;
                string url = _baseUrl.TrimEnd('/') + "/" + actionName.TrimStart('/');

                string body;
                if (inputArgs != null && inputArgs.Count > 0 && inputArgs[0] != null)
                {
                    string raw = inputArgs[0].ToString();
                    // Pass through JSON bodies as-is, otherwise wrap as { "Action": "..." }
                    body = LooksLikeJson(raw) ? raw : "{\"ResetType\":\"" + raw.Replace("\"", "\\\"") + "\"}";
                }
                else
                {
                    body = "{}";
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

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Read REDFISH_USERNAME / REDFISH_PASSWORD from the environment and
        /// apply HTTP Basic auth. Redfish also supports session-based auth
        /// (POST /redfish/v1/SessionService/Sessions returning X-Auth-Token);
        /// Basic auth is sufficient for the typical edge-translator deployment
        /// where credentials are managed via a Kubernetes Secret.
        /// </summary>
        public void ApplyAuthFromEnvironment(HttpClient client)
        {
            client.DefaultRequestHeaders.Authorization = null;
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var user = Environment.GetEnvironmentVariable("REDFISH_USERNAME");
            var pass = Environment.GetEnvironmentVariable("REDFISH_PASSWORD");
            if (!string.IsNullOrWhiteSpace(user) && pass != null)
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }
        }

        private static void SplitAddress(string address, out string resourcePath, out string jsonPointer)
        {
            int hash = address.IndexOf('#');
            if (hash < 0)
            {
                resourcePath = address;
                jsonPointer = string.Empty;
                return;
            }

            resourcePath = address.Substring(0, hash);
            jsonPointer = address.Substring(hash + 1);
        }

        /// <summary>
        /// Convert an RFC 6901 JSON Pointer (e.g. "/Status/Health") to a
        /// Newtonsoft JSON Path (e.g. "Status.Health") so we can use
        /// SelectToken which does not natively support JSON Pointer.
        /// </summary>
        private static string JsonPointerToJsonPath(string pointer)
        {
            if (string.IsNullOrEmpty(pointer))
            {
                return "$";
            }

            string trimmed = pointer.TrimStart('/');
            // unescape JSON Pointer: ~1 -> '/', ~0 -> '~'
            trimmed = trimmed.Replace("~1", "/").Replace("~0", "~");
            return trimmed.Replace("/", ".");
        }

        private static JObject BuildPatchDocument(string jsonPointer, object value, string type)
        {
            // Walk the JSON pointer, building nested objects so we end up with
            // { "Parent": { "Child": <value> } } for "/Parent/Child".
            string[] segments = jsonPointer.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                throw new InvalidOperationException("Cannot PATCH the resource root; specify a JSON Pointer in the address.");
            }

            JToken leaf = JToken.FromObject(CoerceValue(value, type));
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                var wrapper = new JObject
                {
                    [segments[i].Replace("~1", "/").Replace("~0", "~")] = leaf
                };
                leaf = wrapper;
            }

            return (JObject)leaf;
        }

        private static object CoerceValue(object value, string type)
        {
            string s = value?.ToString();
            return type switch
            {
                "Float" => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
                "Double" => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
                "Integer" => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
                "Long" => long.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
                "Boolean" => bool.Parse(s),
                _ => s
            };
        }

        private static object ConvertTyped(string content, string type)
        {
            content = (content ?? string.Empty).Trim().Trim('"');

            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            return type switch
            {
                "Float" => float.Parse(content, System.Globalization.CultureInfo.InvariantCulture),
                "Double" => double.Parse(content, System.Globalization.CultureInfo.InvariantCulture),
                "Integer" => int.Parse(content, System.Globalization.CultureInfo.InvariantCulture),
                "Long" => long.Parse(content, System.Globalization.CultureInfo.InvariantCulture),
                "Boolean" => bool.Parse(content),
                _ => content
            };
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
    }
}
