namespace Opc.Ua.Edge.Translator.WotCon
{
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Opc.Ua.Server;
    using global::Opc.Ua.WotCon;
    using global::Opc.Ua.WotCon.Bindings;
    using global::Opc.Ua.WotCon.Server;
    using global::Opc.Ua.WotCon.Server.Materialization;
    using global::Opc.Ua.WotCon.Server.Registry;
    using Serilog;
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Hosts the OPC UA WoT Connectivity 1.1 registry on a running server and
    /// routes every materialized Thing Description form to our protocol drivers.
    /// </summary>
    /// <remarks>
    /// The registry is composed by hand instead of through the package's DI
    /// extensions because the server is started through <c>ApplicationInstance</c>,
    /// not a generic host. The composition mirrors
    /// <c>OpcUaWotRegistryServerBuilderExtensions</c>.
    /// </remarks>
    public sealed class WotRegistryHost : IAsyncDisposable
    {
        public const string DisableVariable = "DISABLE_WOTCON_REGISTRY";

        private readonly DriverWotBindingExecutor _executor;

        private readonly WotMaterializationCoordinator _coordinator;

        private readonly IServerInternal _server;

        private NodeManagerRegistration _registration;

        private WotRegistryHost(
            IServerInternal server,
            IWotRegistryService registry,
            WotMaterializationCoordinator coordinator,
            DriverWotBindingExecutor executor,
            ThingDescriptionCache thingDescriptions)
        {
            _server = server;
            Registry = registry;
            _coordinator = coordinator;
            _executor = executor;
            ThingDescriptions = thingDescriptions;
        }

        public IWotRegistryService Registry { get; }

        public ThingDescriptionCache ThingDescriptions { get; }

        // Preview feature, on by default; set DISABLE_WOTCON_REGISTRY to turn it off.
        public static bool IsEnabled =>
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DisableVariable));

        public static async Task<WotRegistryHost> StartAsync(
            StandardServer server,
            ProtocolDriverRegistry drivers,
            string storageFolder,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(drivers);

            IServerInternal serverInternal = server.CurrentInstance;

            var thingDescriptions = new ThingDescriptionCache();
            var binder = new DriverWotBinder(drivers);
            var executor = new DriverWotBindingExecutor(drivers, thingDescriptions);

            var endpointPolicy = AssetEndpointPolicy.Create(drivers);

            var binderRegistry = new WotProtocolBinderRegistry(
                [binder],
                [executor],
                endpointPolicy: endpointPolicy,
                telemetry: Program.Telemetry);

            var options = new WotRegistryServerOptions
            {
                StorageFolder = storageFolder,
                AutoRefresh = true,
                StrictBindings = false,
                ManagementAccess = new WotManagementAccessPolicy
                {
                    // WoT-Con 1.1 §Security: management Methods require role-based
                    // access control and a SignAndEncrypt SecureChannel for every mutation.
                    RequiredRoleId = Ua.ObjectIds.WellKnownRole_AuthenticatedUser,
                    MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
                    AllowAnonymous = false
                }
            };

            Directory.CreateDirectory(storageFolder);
            var deferredRegistry = new DeferredWotRegistryService(
                new WotRegistryService(new FileWotRegistryStore(storageFolder), options.Bounds));
            IWotRegistryService registry = deferredRegistry;

            var projectionHost = new LifecycleWotProjectionHost(
                server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(binderRegistry));

            var coordinator = new WotMaterializationCoordinator(
                registry,
                projectionHost,
                binderRegistry,
                nodeSetResolver: new CloudLibraryNodeSetResolver(new UACloudLibraryClient()),
                viewProjectionHost: new LifecycleWotViewProjectionHost(server.NodeManagerLifecycle));

            var host = new WotRegistryHost(serverInternal, registry, coordinator, executor, thingDescriptions);
            registry.Changed += host.OnRegistryChanged;

            // The registry node manager loads and materializes the registry inside its
            // own registration, while NodeManagerLifecycle holds the semaphore that
            // materializing needs too; with persisted Things present the server would
            // never finish starting. Register it against an empty registry, then load
            // the persisted content and materialize it once the registration returned.
            // See DeferredWotRegistryService.
            host._registration = await server.NodeManagerLifecycle.AddAsync(
                new WotRegistryNodeManagerFactory(options, registry, coordinator),
                null,
                cancellationToken).ConfigureAwait(false);

            await deferredRegistry.LoadAsync(cancellationToken).ConfigureAwait(false);
            await host.CacheThingDescriptionsAsync(registry.Current, cancellationToken).ConfigureAwait(false);
            await coordinator.RefreshAsync(new WotRefreshRequest { RequestId = "startup" }, cancellationToken).ConfigureAwait(false);

            await host.ImportSettingsFilesAsync(
                Path.Combine(Directory.GetCurrentDirectory(), "settings"),
                cancellationToken).ConfigureAwait(false);

            Log.Logger.Information("WoT Connectivity registry is hosted (storage: {StorageFolder}).", storageFolder);
            return host;
        }

        /// <summary>
        /// Imports the Thing Descriptions in <paramref name="settingsFolder"/> (*.jsonld)
        /// into the registry, one resource per file, named after the file. A file whose
        /// content already is the resource's default version is skipped, so restarts do
        /// not pile up versions; a changed file becomes the new default version. Files
        /// written for the 1.02 asset management are normalized on the way in; see
        /// <see cref="WotImportNormalizer"/>.
        /// </summary>
        public async Task ImportSettingsFilesAsync(string settingsFolder, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(settingsFolder))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(settingsFolder, "*.jsonld"))
            {
                string resourceId = Path.GetFileNameWithoutExtension(file);

                try
                {
                    byte[] content = WotImportNormalizer.Normalize(
                        await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false));

                    if (await IsCurrentDefaultAsync(resourceId, content, cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }

                    await Registry.UpsertResourceAsync(
                        new WotUpsertResourceRequest
                        {
                            Kind = WoTDocumentKindEnum.ThingDescription,
                            ResourceId = resourceId,
                            Name = resourceId,
                            Content = new ByteString(content),
                            SetAsDefault = true
                        },
                        cancellationToken).ConfigureAwait(false);

                    Log.Logger.Information("Imported WoT file {File} into the WoT Connectivity registry.", file);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Logger.Warning(ex, "Could not import WoT file {File} into the WoT Connectivity registry.", file);
                }
            }
        }

        private async Task<bool> IsCurrentDefaultAsync(string resourceId, byte[] content, CancellationToken cancellationToken)
        {
            WotResource existing = Registry.Current
                .ResourcesOfKind(WoTDocumentKindEnum.ThingDescription)
                .FirstOrDefault(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal));

            WotResourceVersion version = existing?.DefaultVersion;
            if (version == null || !version.HasContent)
            {
                return false;
            }

            ByteString stored = await Registry.ReadContentAsync(version, cancellationToken).ConfigureAwait(false);
            return stored.ToArray().AsSpan().SequenceEqual(content);
        }

        private void OnRegistryChanged(object sender, WotRegistryChangedEventArgs e)
        {
            // Drivers need the Thing Description before the projection opens its
            // first channel; refreshing the cache on every content mutation keeps
            // them in step with what the coordinator materializes.
            if (!e.ProjectionOnly)
            {
                _ = CacheThingDescriptionsAsync(e.Current, CancellationToken.None);
            }
        }

        private async Task CacheThingDescriptionsAsync(WotRegistrySnapshot snapshot, CancellationToken cancellationToken)
        {
            foreach (WotResource resource in snapshot.ResourcesOfKind(WoTDocumentKindEnum.ThingDescription))
            {
                WotResourceVersion version = resource.DefaultVersion;
                if (version == null || !version.HasContent)
                {
                    continue;
                }

                try
                {
                    ByteString content = await Registry.ReadContentAsync(version, cancellationToken).ConfigureAwait(false);
                    ThingDescriptions.Set(System.Text.Encoding.UTF8.GetString(content.ToArray()));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Logger.Warning(ex, "Could not cache Thing Description {Resource} for the protocol drivers.", resource.Name);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            Registry.Changed -= OnRegistryChanged;

            if (_registration != null)
            {
                try
                {
                    await ((StandardServer)null ?? throw new InvalidOperationException()).NodeManagerLifecycle
                        .RemoveAsync(_registration, null).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log.Logger.Debug(ex, "WoT registry node manager was already removed.");
                }
            }

            _coordinator.Dispose();
            await _executor.DisposeAsync().ConfigureAwait(false);
            _ = _server;
        }
    }
}
