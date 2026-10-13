namespace Opc.Ua.Edge.Translator
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Opc.Ua.Edge.Translator.WotCon;
    using Serilog;
    using System.IO;
    using Opc.Ua.Configuration;
    using Opc.Ua.Server;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Composes the UA Edge Translator OPC UA server on the .NET generic host.
    /// Shared by <see cref="Program.Main"/> and the integration-test fixture so both
    /// run the same startup path.
    /// </summary>
    public static class OpcUaHosting
    {
        /// <summary>
        /// Registers the OPC UA server, loaded from <paramref name="configurationFile"/>,
        /// with the UA Edge Translator node manager.
        /// </summary>
        /// <param name="configure">
        /// Applied to the loaded configuration before certificates are checked and
        /// the server starts.
        /// </param>
        public static HostedOpcUaApplication AddEdgeTranslatorServer(
            this IServiceCollection services,
            ITelemetryContext telemetry,
            string configurationFile,
            Action<ApplicationConfiguration> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(telemetry);

            HostedOpcUaApplication application = new(telemetry);

            // The SDK registers its defaults with TryAdd, so these registrations win.
            services.AddSingleton(telemetry);
            services.AddSingleton(application);
            services.AddSingleton<IApplicationInstanceFactory>(application);

            // Registered with the host (not only on the started server) so the SDK can
            // check them against the configured user token policies at startup.
            services.AddOpcUa()
                .AddServer<UAServer>(configurationFile, configure)
                .AddNodeManager(new UANodeManagerFactory())
                .AddIdentityAuthenticator(UAServer.UserNameAuthenticator.CreateHosted())
                .AddIdentityAuthenticator(new UAServer.UnsupportedTokenTypeAuthenticator(UserTokenType.Anonymous))
                .AddIdentityAuthenticator(new UAServer.UnsupportedTokenTypeAuthenticator(UserTokenType.Certificate))
                .AddIdentityAuthenticator(new UAServer.UnsupportedTokenTypeAuthenticator(UserTokenType.IssuedToken))
                .AddStartupTask((sp, server, ct) => application.OnServerStartedAsync());

            return application;
        }

        /// <summary>
        /// Hosts the WoT Connectivity registry (preview, on by default) next to the OPC UA
        /// server. It starts once the server is running and is removed before the server stops.
        /// </summary>
        public static IServiceCollection AddWotRegistry(this IServiceCollection services, HostedOpcUaApplication application, string storageFolder)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(application);

            if (!WotRegistryHost.IsEnabled)
            {
                return services;
            }

            WotRegistryHostedService registry = new(storageFolder);
            services.AddSingleton(registry);

            // Hosted services stop in reverse order, so registering after the server
            // removes the registry while the server is still running.
            services.AddHostedService(sp => registry);
            application.StartupTasks.Add(server => registry.StartAsync(server));

            return services;
        }
    }

    /// <summary>
    /// Owns the <see cref="WotRegistryHost"/> for the generic host's lifetime.
    /// </summary>
    public sealed class WotRegistryHostedService : IHostedService
    {
        private readonly string _storageFolder;

        internal WotRegistryHostedService(string storageFolder)
        {
            _storageFolder = storageFolder ?? throw new ArgumentNullException(nameof(storageFolder));
        }

        /// <summary>The running registry, once the OPC UA server has started.</summary>
        public WotRegistryHost Registry { get; private set; }

        internal async Task StartAsync(StandardServer server)
        {
            Registry = await WotRegistryHost.StartAsync(server, Program.Drivers, _storageFolder).ConfigureAwait(false);
        }

        Task IHostedService.StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        async Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            WotRegistryHost registry = Registry;
            Registry = null;
            if (registry == null)
            {
                return;
            }

            try
            {
                await registry.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Logger.Error(ex, "Error while stopping the WoT Connectivity registry.");
            }
        }
    }

    /// <summary>
    /// Captures the <see cref="ApplicationInstance"/> and server the generic host
    /// creates, and signals when the server has started.
    /// </summary>
    /// <remarks>
    /// The hosted service owns the application and server, but the dashboard, the
    /// WoT-Con registry and the tests still need them; this hands them out once the
    /// server is running.
    /// </remarks>
    public sealed class HostedOpcUaApplication : IApplicationInstanceFactory
    {
        private readonly ITelemetryContext _telemetry;

        private readonly TaskCompletionSource<StandardServer> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal HostedOpcUaApplication(ITelemetryContext telemetry)
        {
            _telemetry = telemetry;
        }

        /// <summary>The application instance created by the host.</summary>
        public ApplicationInstance Application { get; private set; }

        /// <summary>The server created by the host.</summary>
        public StandardServer Server { get; internal set; }

        /// <summary>
        /// Raised as soon as the host creates the application instance, before the server
        /// starts. Node managers build their address space (and drivers may start) during
        /// server startup, so anything they read through the instance must be set here.
        /// </summary>
        public event Action<ApplicationInstance> Created;

        /// <summary>Raised once the server has started, before it is handed out.</summary>
        public event Action<ApplicationInstance> Started;

        public IApplicationInstance Create(ITelemetryContext telemetry)
        {
            Application = new ApplicationInstance(telemetry ?? _telemetry)
            {
                ApplicationType = ApplicationType.ClientAndServer,
                ConfigSectionName = "Ua.Edge.Translator"
            };

            Created?.Invoke(Application);
            return Application;
        }

        /// <summary>
        /// Waits for the hosted server to start. Faults if the host fails or stops first.
        /// </summary>
        public async Task<StandardServer> WaitForStartAsync(Task hostRun, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(hostRun);

            Task first = await Task.WhenAny(_started.Task, hostRun, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            if (first == _started.Task)
            {
                return await _started.Task.ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // The host stopped (or faulted) before the server started.
            await hostRun.ConfigureAwait(false);
            throw new InvalidOperationException("The OPC UA server stopped before it started.");
        }

        /// <summary>Work run, in order, once the server has started.</summary>
        public System.Collections.Generic.List<Func<StandardServer, Task>> StartupTasks { get; } = new();

        internal async ValueTask OnServerStartedAsync()
        {
            if (Server == null)
            {
                _started.TrySetException(new InvalidOperationException("The hosted OPC UA server was not created by the UA Edge Translator."));
                return;
            }

            Started?.Invoke(Application);
            try
            {
                foreach (Func<StandardServer, Task> task in StartupTasks)
                {
                    await task(Server).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _started.TrySetException(ex);
                throw;
            }

            _started.TrySetResult(Server);
        }
    }
}
