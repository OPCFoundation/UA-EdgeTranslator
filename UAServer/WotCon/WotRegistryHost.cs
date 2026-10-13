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

        private readonly StandardServer _standardServer;

        private readonly IWotNodeSetResolver _nodeSetResolver;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, NodeManagerRegistration> _companionModels = new(StringComparer.Ordinal);

        private NodeManagerRegistration _registration;

        // Serializes the prepare-then-refresh work that registry changes trigger, so
        // each refresh sees the caches and companion models of the change before it.
        private readonly SemaphoreSlim _changeGate = new(1, 1);

        // The registry generation last prepared and refreshed; guarded by _changeGate.
        private long _preparedGeneration = -1;

        private readonly CancellationTokenSource _stopping = new();

        private WotRegistryHost(
            StandardServer server,
            IWotRegistryService registry,
            WotMaterializationCoordinator coordinator,
            DriverWotBindingExecutor executor,
            ThingDescriptionCache thingDescriptions,
            IWotNodeSetResolver nodeSetResolver)
        {
            _standardServer = server;
            _server = server.CurrentInstance;
            _nodeSetResolver = nodeSetResolver;
            Registry = registry;
            _coordinator = coordinator;
            _executor = executor;
            ThingDescriptions = thingDescriptions;
        }

        public IWotRegistryService Registry { get; }

        /// <summary>The materialization coordinator; exposed for refresh and diagnostics.</summary>
        public WotMaterializationCoordinator Coordinator => _coordinator;

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
            var mappingTypes = new WotMappingTypeModel(serverInternal);
            var targetResolver = new CreatingTargetVariableResolver(mappingTypes);
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

                // The host refreshes after every content change itself, once the Thing
                // Descriptions are cached and their companion models are loaded (see
                // OnRegistryChanged). The package's auto-refresh would run before that.
                AutoRefresh = false,
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
                new ObjectTypeMappingRuntimeFactory(
                    new WotProjectionBindingRuntimeFactory(binderRegistry, targetResolver),
                    targetResolver,
                    thingDescriptions));

            var nodeSetResolver = new CloudLibraryNodeSetResolver(new UACloudLibraryClient());
            var coordinator = new WotMaterializationCoordinator(
                registry,
                projectionHost,
                binderRegistry,
                nodeSetResolver: nodeSetResolver,
                viewProjectionHost: new LifecycleWotViewProjectionHost(server.NodeManagerLifecycle));

            var host = new WotRegistryHost(server, registry, coordinator, executor, thingDescriptions, nodeSetResolver);
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
            await host.LoadCompanionModelsAsync(registry.Current, cancellationToken).ConfigureAwait(false);
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

                    // A target-mapping type from a companion model must be in the address
                    // space before the Thing is materialized against it.
                    await LoadCompanionModelsAsync(content, cancellationToken).ConfigureAwait(false);

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

        private async Task LoadCompanionModelsAsync(WotRegistrySnapshot snapshot, CancellationToken cancellationToken)
        {
            foreach (WotResource resource in snapshot.ResourcesOfKind(WoTDocumentKindEnum.ThingDescription))
            {
                WotResourceVersion version = resource.DefaultVersion;
                if (version == null || !version.HasContent)
                {
                    continue;
                }

                ByteString content = await Registry.ReadContentAsync(version, cancellationToken).ConfigureAwait(false);
                await LoadCompanionModelsAsync(content.ToArray(), cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Loads, through the node-set resolver, every companion model the Thing
        /// Description names in a <c>uav:mapToType</c> (with its required models first),
        /// as runtime NodeSets. The registry only resolves namespaces a converted NodeSet
        /// lists as required models, which a mapping type is not, so without this the
        /// target type would be unknown when the Thing is materialized.
        /// </summary>
        private async Task LoadCompanionModelsAsync(byte[] thingDescription, CancellationToken cancellationToken)
        {
            foreach (string namespaceUri in WotImportNormalizer.MappingTypeNamespaces(thingDescription))
            {
                await LoadCompanionModelAsync(namespaceUri, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task LoadCompanionModelAsync(string namespaceUri, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(namespaceUri)
                || string.Equals(namespaceUri, Ua.Namespaces.OpcUa, StringComparison.Ordinal)
                || _companionModels.ContainsKey(namespaceUri)
                || _server.NamespaceUris.GetIndex(namespaceUri) >= 0)
            {
                return;
            }

            byte[] xml;
            using (Stream stream = await _nodeSetResolver.TryResolveAsync(namespaceUri, cancellationToken).ConfigureAwait(false))
            {
                if (stream == null)
                {
                    Log.Logger.Warning("Companion model {NamespaceUri} named by a WoT target mapping could not be resolved.", namespaceUri);
                    return;
                }

                using MemoryStream copy = new();
                await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
                xml = copy.ToArray();
            }

            Ua.Export.UANodeSet nodeSet;
            using (MemoryStream read = new(xml, writable: false))
            {
                nodeSet = Ua.Export.UANodeSet.Read(read);
            }

            // Required models first, so the companion's references resolve.
            foreach (Ua.Export.ModelTableEntry model in nodeSet.Models ?? Array.Empty<Ua.Export.ModelTableEntry>())
            {
                foreach (Ua.Export.ModelTableEntry required in model.RequiredModel ?? Array.Empty<Ua.Export.ModelTableEntry>())
                {
                    await LoadCompanionModelAsync(required.ModelUri, cancellationToken).ConfigureAwait(false);
                }
            }

            string[] owned = (nodeSet.Models ?? Array.Empty<Ua.Export.ModelTableEntry>())
                .Select(m => m.ModelUri)
                .Where(uri => !string.IsNullOrEmpty(uri))
                .ToArray();
            if (owned.Length == 0)
            {
                owned = [namespaceUri];
            }

            Ua.Server.RuntimeNodeSet.RuntimeNodeSetOptions options = new()
            {
                Sources = new ArrayOf<Ua.Server.RuntimeNodeSet.RuntimeNodeSetSource>(new Ua.Server.RuntimeNodeSet.RuntimeNodeSetSource[]
                {
                    Ua.Server.RuntimeNodeSet.RuntimeNodeSetSource.FromStream(
                        namespaceUri,
                        _ => new ValueTask<Stream>(OpenNodeSet(xml)),
                        new ArrayOf<string>(owned))
                })
            };

            NodeManagerRegistration registration = await Ua.Server.RuntimeNodeSet.RuntimeNodeSetLifecycleExtensions
                .AddRuntimeNodeSetAsync(_standardServer.NodeManagerLifecycle, options, null, cancellationToken)
                .ConfigureAwait(false);

            foreach (string uri in owned)
            {
                _companionModels[uri] = registration;
            }

            Log.Logger.Information("Loaded companion model {NamespaceUri} for WoT target mappings.", namespaceUri);
        }

        /// <summary>
        /// Opens a fresh read-only stream over a companion NodeSet. The runtime NodeSet
        /// loader takes ownership: it disposes the stream once the NodeSet is parsed, and
        /// calls this again for every (re)load, so the stream must not be shared or cached.
        /// </summary>
        private static Stream OpenNodeSet(byte[] xml)
        {
            return new MemoryStream(xml, writable: false);
        }

        private void OnRegistryChanged(object sender, WotRegistryChangedEventArgs e)
        {
            // Content changes come from settings imports and from xRegistry (OPC UA
            // methods). Before the change is materialized, drivers need its Thing
            // Descriptions, and target mappings need the companion models their
            // uav:mapToType names; only then is the coordinator refreshed.
            if (!e.ProjectionOnly)
            {
                _ = PrepareAndRefreshAsync();
            }
        }

        private async Task PrepareAndRefreshAsync()
        {
            CancellationToken cancellationToken = _stopping.Token;
            try
            {
                await _changeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                // Changes queued behind this one are coalesced: prepare the latest snapshot,
                // and if another change commits meanwhile, prepare again, so the refresh
                // (which projects the registry's current state) never sees an unprepared one.
                WotRegistrySnapshot snapshot = Registry.Current;
                if (snapshot.Generation <= _preparedGeneration)
                {
                    return;
                }

                while (true)
                {
                    await CacheThingDescriptionsAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    await LoadCompanionModelsAsync(snapshot, cancellationToken).ConfigureAwait(false);

                    WotRegistrySnapshot current = Registry.Current;
                    if (current.Generation == snapshot.Generation)
                    {
                        break;
                    }

                    snapshot = current;
                }

                await _coordinator.RefreshAsync(new WotRefreshRequest { RequestId = "change" }, cancellationToken).ConfigureAwait(false);
                _preparedGeneration = snapshot.Generation;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Could not materialize a WoT Connectivity registry change.");
            }
            finally
            {
                _changeGate.Release();
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
                    ThingDescriptions.Set(resource.Xid, System.Text.Encoding.UTF8.GetString(content.ToArray()));
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

            // Cancel pending change work and wait for a refresh in progress, so nothing
            // materializes again while the projected Things are being removed.
            _stopping.Cancel();
            await _changeGate.WaitAsync().ConfigureAwait(false);

            // Remove the projected Things first. Removing the registry node manager would
            // otherwise remove them from inside its own lifecycle operation, which waits
            // on the lifecycle lock that operation already holds.
            try
            {
                await _coordinator.RemoveAllAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Logger.Warning(ex, "Failed to remove the projected WoT Connectivity resources.");
            }

            if (_registration != null)
            {
                try
                {
                    await _standardServer.NodeManagerLifecycle
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
