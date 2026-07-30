namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class HTTPClientAsset : IAsset
    {
        private string _baseUrl = string.Empty;

        private readonly HttpClient _client = new();

        public bool IsConnected { get; private set; } = false;

        public async Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
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
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, _baseUrl);
                using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);

                IsConnected = true;
                Log.Logger.Information("Connected to HTTP endpoint at " + _baseUrl);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            _client.Dispose();
            return ValueTask.CompletedTask;
        }

        public string GetRemoteEndpoint()
        {
            return _baseUrl;
        }

        public async Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default)
        {
            try
            {
                string url = _baseUrl.TrimEnd('/') + "/" + tag.Address.TrimStart('/');

                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
                using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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

        public async Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default)
        {
            try
            {
                string url = _baseUrl.TrimEnd('/') + "/" + tag.Address.TrimStart('/');

                using StringContent content = new StringContent(value.ToString(), Encoding.UTF8, "application/json");
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
                using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public async Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default)
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

                using StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string result = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                return AssetActionResult.FromOutputs(result, new List<object> { result });
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return AssetActionResult.FromStatus(null);
            }
        }
    }
}
