namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;

    public class HTTPClientAsset : IAsset
    {
        private string _baseUrl = string.Empty;

        private readonly HttpClient _client = new();

        public bool IsConnected { get; private set; } = false;

        public void Connect(string ipAddress, int port)
        {
            try
            {
                if (port > 0)
                {
                    _baseUrl = $"http://{ipAddress}:{port}";
                }
                else
                {
                    _baseUrl = ipAddress;
                }

                // verify connectivity by sending a HEAD request to the base URL
                var request = new HttpRequestMessage(HttpMethod.Head, _baseUrl);
                var response = _client.Send(request);

                IsConnected = true;
                Log.Logger.Information("Connected to HTTP endpoint at " + _baseUrl);
            }
            catch (Exception ex)
            {
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
                string url = _baseUrl.TrimEnd('/') + "/" + tag.Address.TrimStart('/');

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = _client.Send(request);
                response.EnsureSuccessStatusCode();

                string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (tag.Type == "Float")
                {
                    return float.Parse(content);
                }
                else if (tag.Type == "Boolean")
                {
                    return bool.Parse(content);
                }
                else if (tag.Type == "Integer")
                {
                    return int.Parse(content);
                }
                else
                {
                    return content;
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return null;
            }
        }

        public void Write(AssetTag tag, string value)
        {
            try
            {
                string url = _baseUrl.TrimEnd('/') + "/" + tag.Address.TrimStart('/');

                var content = new StringContent(value, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
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
    }
}
