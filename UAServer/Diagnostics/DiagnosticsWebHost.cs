namespace Opc.Ua.Edge.Translator.Diagnostics
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Edge.Translator.Components;
    using Serilog;
    using System;
    using System.Globalization;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Hosts the Blazor Server diagnostics dashboard side-by-side with the OPC UA
    /// server. The OPC UA stack listens on opc.tcp (4840); this host serves the
    /// HTML dashboard over HTTP on a separate fixed port (8081). It is started near
    /// the end of <see cref="Program.Main"/> and stopped during graceful shutdown.
    /// </summary>
    public sealed class DiagnosticsWebHost
    {
        private const int _port = 8081;

        private WebApplication _app;

        public string Url { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            int port = _port;
            Url = string.Create(CultureInfo.InvariantCulture, $"http://0.0.0.0:{port}");

            // Pin the content root to the binary location so the static web asset
            // manifest (emitted next to the assembly) resolves regardless of the
            // process working directory the OPC UA stack is launched from.
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory
            });

            builder.WebHost.UseStaticWebAssets();
            builder.WebHost.UseUrls(Url);

            // Route ASP.NET Core logs through the already-configured Serilog
            // logger instead of spinning up a second console logger.
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger);

            builder.Services.AddRazorComponents().AddInteractiveServerComponents();
            builder.Services.AddSingleton<DiagnosticsService>();

            _app = builder.Build();

            _app.UseStaticFiles();

            // Enforce HTTP Basic authentication on every request before anything
            // else runs. The dashboard exposes operational controls (certificates,
            // driver/device management), so access is mandatory-gated behind the
            // same OPCUA_USERNAME / OPCUA_PASSWORD credentials the OPC UA server
            // validates at startup (see Program.ValidateRequiredEnvironment).
            _app.Use(async (context, next) =>
            {
                if (!IsAuthorized(context))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Basic realm=\"UA Edge Translator\", charset=\"UTF-8\"";
                    return;
                }

                await next().ConfigureAwait(false);
            });

            _app.UseAntiforgery();
            _app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

            // Streams the public application certificate as a file download. Lives outside
            // the Blazor circuit so the browser performs a normal HTTP GET (Save As) instead
            // of trying to push bytes over the SignalR connection.
            _app.MapGet("/certificates/download/{thumbprint}", (string thumbprint, DiagnosticsService diagnostics) =>
            {
                CertificateFile file = diagnostics.GetApplicationCertificateFile(thumbprint);
                return file == null
                    ? Results.NotFound()
                    : Results.File(file.Content, file.ContentType, file.FileName);
            });

            await _app.StartAsync(cancellationToken).ConfigureAwait(false);

            Log.Logger.Information("Diagnostics UI is available on http://localhost:{Port} (listening on {Url}).", port, Url);
        }

        private static bool IsAuthorized(HttpContext context)
        {
            string expectedUsername = Program.OpcUaUsername;
            string expectedPassword = Program.OpcUaPassword;

            string headerValue = context.Request.Headers.Authorization;
            if (string.IsNullOrEmpty(headerValue)
                || !AuthenticationHeaderValue.TryParse(headerValue, out AuthenticationHeaderValue header)
                || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(header.Parameter))
            {
                return false;
            }

            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
            }
            catch (FormatException)
            {
                return false;
            }

            int separatorIndex = decoded.IndexOf(':');
            if (separatorIndex < 0)
            {
                return false;
            }

            string username = decoded.Substring(0, separatorIndex);
            string password = decoded.Substring(separatorIndex + 1);

            // Constant-time comparison to avoid leaking credential length/content
            // through timing side channels.
            bool usernameMatches = FixedTimeEquals(username, expectedUsername);
            bool passwordMatches = FixedTimeEquals(password, expectedPassword);

            return usernameMatches && passwordMatches;
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            byte[] bytesA = Encoding.UTF8.GetBytes(a ?? string.Empty);
            byte[] bytesB = Encoding.UTF8.GetBytes(b ?? string.Empty);
            return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
        }

        public async Task StopAsync()
        {
            if (_app == null)
            {
                return;
            }

            try
            {
                await _app.StopAsync().ConfigureAwait(false);
                await _app.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _app = null;
            }
        }
    }
}
