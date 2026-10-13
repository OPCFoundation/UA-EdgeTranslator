namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.Diagnostics;
    using System;
    using System.Globalization;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Sockets;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// End-to-end tests for the diagnostics web host: they start the real
    /// ASP.NET Core host on a free port and exercise the Basic authentication
    /// gate and the certificate download endpoint over HTTP.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class DiagnosticsWebHostIntegrationTests : IAsyncLifetime
    {
        private OpcUaServerFixture _fixture;
        private DiagnosticsWebHost _host;
        private HttpClient _client;

        public async Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();

            int port = GetFreePort();
            _host = new DiagnosticsWebHost(port);
            await _host.StartAsync(CancellationToken.None).ConfigureAwait(false);

            _client = new HttpClient { BaseAddress = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}/")) };
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            _client?.Dispose();

            if (_host != null)
            {
                await _host.StopAsync().ConfigureAwait(false);
            }

            if (_fixture != null)
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
                _fixture = null;
            }
        }

        [Fact]
        public void The_url_reflects_the_configured_port()
        {
            Assert.StartsWith("http://0.0.0.0:", _host.Url, StringComparison.Ordinal);
            Assert.EndsWith(_client.BaseAddress.Port.ToString(CultureInfo.InvariantCulture), _host.Url, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Requests_without_credentials_are_challenged()
        {
            using HttpResponseMessage response = await _client.GetAsync(new Uri("/", UriKind.Relative)).ConfigureAwait(false);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Basic");
        }

        [Theory]
        [InlineData("Bearer abc")]
        [InlineData("Basic")]
        [InlineData("Basic !!!not-base64!!!")]
        [InlineData("Basic bm9zZXBhcmF0b3I=")]
        public async Task Malformed_or_foreign_authorization_headers_are_rejected(string header)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/", UriKind.Relative));
            request.Headers.TryAddWithoutValidation("Authorization", header);

            using HttpResponseMessage response = await _client.SendAsync(request).ConfigureAwait(false);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Wrong_password_is_rejected()
        {
            using HttpResponseMessage response = await GetAsync("/", _fixture.Username, "wrong").ConfigureAwait(false);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Valid_credentials_serve_the_dashboard()
        {
            using HttpResponseMessage response = await GetAsync("/", _fixture.Username, _fixture.Password).ConfigureAwait(false);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.Contains("UA Edge Translator", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Certificate_download_streams_the_public_certificate()
        {
            string thumbprint = new DiagnosticsService().GetCertificateOverview().ApplicationCertificates[0].Thumbprint;

            using HttpResponseMessage response = await GetAsync($"/certificates/download/{thumbprint}", _fixture.Username, _fixture.Password).ConfigureAwait(false);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.EndsWith(".der", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'), StringComparison.Ordinal);

            byte[] content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(content);
            Assert.Equal(thumbprint, certificate.Thumbprint);
            Assert.False(certificate.HasPrivateKey);
        }

        [Fact]
        public async Task Certificate_download_returns_not_found_for_an_unknown_thumbprint()
        {
            using HttpResponseMessage response = await GetAsync("/certificates/download/00112233445566778899AABBCCDDEEFF00112233", _fixture.Username, _fixture.Password).ConfigureAwait(false);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Stopping_twice_is_harmless()
        {
            await _host.StopAsync().ConfigureAwait(false);
            await _host.StopAsync().ConfigureAwait(false);

            _host = null;
        }

        private Task<HttpResponseMessage> GetAsync(string path, string username, string password)
        {
            HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password)));

            return _client.SendAsync(request);
        }

        private static int GetFreePort()
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
