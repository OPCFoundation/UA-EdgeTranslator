namespace Opc.Ua.Edge.Translator.Diagnostics
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Edge.Translator.Components;
    using Serilog;
    using System;
    using System.Globalization;
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
            _app.UseAntiforgery();
            _app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

            await _app.StartAsync(cancellationToken).ConfigureAwait(false);

            Log.Logger.Information("Diagnostics UI is available on http://localhost:{Port} (listening on {Url}).", port, Url);
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
