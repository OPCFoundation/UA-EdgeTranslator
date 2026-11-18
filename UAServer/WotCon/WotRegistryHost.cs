namespace Opc.Ua.Edge.Translator.WotCon
{
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Opc.Ua.Server;
    using global::Opc.Ua.WotCon;
    using global::Opc.Ua.WotCon.Bindings;
    using global::Opc.Ua.WotCon.Server;
    using global::Opc.Ua.WotCon.Server.Materialization;
    using global::Opc.Ua.WotCon.Server.Registry;
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
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

        private readonly LeasingWotBlobResourceStore _documents;

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
            IWotNodeSetResolver nodeSetResolver,
            LeasingWotBlobResourceStore documents)
        {
            _standardServer = server;
            _server = server.CurrentInstance;
            _nodeSetResolver = nodeSetResolver;
            _documents = documents;
            Registry = registry;
            _coordinator = coordinator;
            _executor = executor;
            ThingDescriptions = thingDescriptions;
        }

        public IWotRegistryService Registry { get; }

        /// <summary>The running registry host, or null when the registry is disabled or stopped.</summary>
        public static WotRegistryHost Current { get; private set; }

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

            // The package's default document store only supports the content leases that
            // materialization requires on Windows; see LeasingWotBlobResourceStore.
            var documents = LeasingWotBlobResourceStore.OpenForRegistry(storageFolder);
            var registry = new WotRegistryService(new FileWotRegistryStore(storageFolder, documents), options.Bounds);

            var projectionHost = new LifecycleWotProjectionHost(
                server.NodeManagerLifecycle,
                new ObjectTypeMappingRuntimeFactory(
                    new WotProjectionBindingRuntimeFactory(binderRegistry, targetResolver),
                    targetResolver,
                    thingDescriptions,
                    xid => string.IsNullOrEmpty(xid) ? null : registry.Current.FindResourceByXid(xid)?.ThingId));

            var nodeSetResolver = new CloudLibraryNodeSetResolver(new UACloudLibraryClient());
            var coordinator = new WotMaterializationCoordinator(
                registry,
                projectionHost,
                binderRegistry,
                nodeSetResolver: nodeSetResolver,
                viewProjectionHost: new LifecycleWotViewProjectionHost(server.NodeManagerLifecycle));

            var host = new WotRegistryHost(server, registry, coordinator, executor, thingDescriptions, nodeSetResolver, documents);

            // Drivers need the persisted Things' Thing Descriptions, and target mappings
            // the companion models their uav:mapToType names, before the registry node
            // manager materializes them; it does so while it is registered.
            await registry.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await host.CacheThingDescriptionsAsync(registry.Current, cancellationToken).ConfigureAwait(false);
            await host.LoadCompanionModelsAsync(registry.Current, cancellationToken).ConfigureAwait(false);
            registry.Changed += host.OnRegistryChanged;

            host._registration = await CommitChangeAsync(
                server.NodeManagerLifecycle,
                NodeManagerBatchChange.Add(new WotRegistryNodeManagerFactory(options, registry, coordinator)),
                cancellationToken).ConfigureAwait(false);

            await host.ImportSettingsFilesAsync(
                Path.Combine(Directory.GetCurrentDirectory(), "settings"),
                cancellationToken).ConfigureAwait(false);

            Current = host;
            Log.Logger.Information("WoT Connectivity registry is hosted (storage: {StorageFolder}).", storageFolder);
            return host;
        }

        /// <summary>
        /// Imports the Thing Descriptions in <paramref name="settingsFolder"/> (*.jsonld)
        /// into the registry, one resource per file, named after its Thing Description. A file whose
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
                try
                {
                    byte[] content = WotImportNormalizer.Normalize(
                        await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false));

                    // The resource is named after the Thing Description, like the asset of the
                    // 1.02 asset management, whatever the file is called.
                    string resourceId = ThingName(content);
                    if (resourceId == null)
                    {
                        Log.Logger.Warning("Skipping WoT file {File}: its Thing Description has no valid name.", file);
                        continue;
                    }

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

        /// <summary>
        /// Removes the registry Thing a settings file was imported as (see
        /// <see cref="ImportSettingsFilesAsync"/>), so deleting the file also stops the Thing
        /// from being rematerialized, and its asset reconnected, on every startup. Returns
        /// true when a Thing was removed.
        /// </summary>
        public async Task<bool> RemoveImportedThingAsync(byte[] fileContent, CancellationToken cancellationToken = default)
        {
            string resourceId = ThingName(WotImportNormalizer.Normalize(fileContent));
            if (resourceId == null)
            {
                return false;
            }

            WotResource resource = Registry.Current
                .ResourcesOfKind(WoTDocumentKindEnum.ThingDescription)
                .FirstOrDefault(r => string.Equals(r.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));
            if (resource == null)
            {
                return false;
            }

            var result = await Registry.DeleteResourceAsync(resource.GroupId, resource.ResourceId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Changed)
            {
                Log.Logger.Warning("Could not remove WoT Connectivity registry Thing {ResourceId}: {Outcome} {Message}", resource.ResourceId, result.Outcome, result.Message);
                return false;
            }

            Log.Logger.Information("Removed WoT Connectivity registry Thing {ResourceId}.", resource.ResourceId);
            return true;
        }

        /// <summary>
        /// The resource id of a Thing Description: its <c>name</c>, or its <c>title</c> when it has
        /// none. Null when that is not a valid asset name (letters, digits, '.', '_', '-').
        /// </summary>
        internal static string ThingName(byte[] content)
        {
            System.Text.Json.Nodes.JsonObject document = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
            string name = (document?["name"] as System.Text.Json.Nodes.JsonValue)?.GetValue<string>()
                ?? (document?["title"] as System.Text.Json.Nodes.JsonValue)?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name[0] == '.'
                || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
            {
                return null;
            }

            return name;
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
            IReadOnlyList<string> namespaces = WotImportNormalizer.MappingTypeNamespaces(thingDescription);

            RegisterOwnStructureTypes(thingDescription, namespaces);

            foreach (string namespaceUri in namespaces)
            {
                await LoadCompanionModelAsync(namespaceUri, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Mapping types in the Thing's own namespace are defined by its protocol driver
        /// (for example Rockwell UDTs embedded in the forms), not by a companion model.
        /// Registers them before the Thing is materialized. Without this they existed only
        /// when legacy onboarding happened to load the same file first, so the registry
        /// looked the asset's namespace up in the Cloud Library, failed, and materialized
        /// the mapped variables without their structure type.
        /// </summary>
        private static void RegisterOwnStructureTypes(byte[] thingDescription, IReadOnlyList<string> mappingNamespaces)
        {
            UANodeManager manager = UANodeManager.Instance;
            if (manager == null || mappingNamespaces.Count == 0)
            {
                return;
            }

            try
            {
                ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(Encoding.UTF8.GetString(thingDescription).Trim('\uFEFF'));
                if (td?.Name == null || !mappingNamespaces.Contains(UANodeManager.AssetNamespaceUri(td.Name), StringComparer.Ordinal))
                {
                    return;
                }

                manager.RegisterOwnStructureTypes(td);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Could not register the protocol driver's structure types for a Thing Description.");
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

            NodeManagerRegistration registration = await CommitChangeAsync(
                _standardServer.NodeManagerLifecycle,
                NodeManagerBatchChange.Add(new Ua.Server.RuntimeNodeSet.RuntimeNodeSetNodeManagerFactory(options)),
                cancellationToken).ConfigureAwait(false);

            foreach (string uri in owned)
            {
                _companionModels[uri] = registration;
            }

            Log.Logger.Information("Loaded companion model {NamespaceUri} for WoT target mappings.", namespaceUri);
        }

        /// <summary>
        /// The companion models that can be loaded for target mappings: the ones in the local
        /// nodesets folder and the ones in the UA Cloud Library (when configured), sorted.
        /// </summary>
        public Task<IReadOnlyList<string>> GetAvailableCompanionModelsAsync()
        {
            return UACloudLibraryClient.GetCompanionModelUrisAsync((_nodeSetResolver as CloudLibraryNodeSetResolver)?.Client);
        }

        /// <summary>
        /// Loads a companion model, with its required models, into the address space the same
        /// way a <c>uav:mapToType</c> naming it does, so its types can be mapped onto WoT
        /// properties before any Thing uses them. Returns true when the namespace is in the
        /// address space afterwards.
        /// </summary>
        public async Task<bool> AddCompanionModelAsync(string namespaceUri, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(namespaceUri))
            {
                return false;
            }

            namespaceUri = namespaceUri.Trim();

            await _changeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await LoadCompanionModelAsync(namespaceUri, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _changeGate.Release();
            }

            return _server.NamespaceUris.GetIndex(namespaceUri) >= 0;
        }

        /// <summary>
        /// Stores an uploaded NodeSet2 document in the local nodesets folder, where the
        /// node-set resolver looks first, and loads the models it defines. Returns the model
        /// URIs that are in the address space afterwards.
        /// </summary>
        public async Task<IReadOnlyList<string>> AddCompanionModelAsync(byte[] nodeSetXml, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> models = await UACloudLibraryClient.SaveLocalNodesetAsync(nodeSetXml).ConfigureAwait(false);

            List<string> loaded = [];
            foreach (string model in models)
            {
                if (await AddCompanionModelAsync(model, cancellationToken).ConfigureAwait(false))
                {
                    loaded.Add(model);
                }
            }

            return loaded;
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
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }

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
                    await CommitChangeAsync(
                        _standardServer.NodeManagerLifecycle,
                        NodeManagerBatchChange.Remove(_registration),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log.Logger.Debug(ex, "WoT registry node manager was already removed.");
                }
            }

            _coordinator.Dispose();
            await _executor.DisposeAsync().ConfigureAwait(false);
            _documents.Dispose();
            _ = _server;
        }

        /// <summary>
        /// Prepares and publishes a single node manager addition or removal.
        /// </summary>
        /// <returns>The committed registration for an addition, or null for a removal.</returns>
        private static async Task<NodeManagerRegistration> CommitChangeAsync(
            INodeManagerLifecycle lifecycle,
            NodeManagerBatchChange change,
            CancellationToken cancellationToken)
        {
            var batch = await lifecycle.PrepareAsync(
                new ArrayOf<NodeManagerBatchChange>(new[] { change }),
                cancellationToken).ConfigureAwait(false);

            await using (batch.ConfigureAwait(false))
            {
                var result = await batch.CommitAsync(
                    static _ => default,
                    cancellationToken).ConfigureAwait(false);

                if (result.CleanupFailure != null)
                {
                    Log.Logger.Warning(result.CleanupFailure, "Node manager change was published, but cleanup failed.");
                }

                foreach (NodeManagerRegistration registration in result.Registrations)
                {
                    return registration;
                }

                return null;
            }
        }
    }
}
