namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Export;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    // ICallAsyncNodeManager makes the server dispatch Call through CallAsync,
    // which runs the MethodState.OnCallMethod2Async handlers. CustomNodeManager2
    // already provides CallAsync but does not declare the interface, so without
    // it the server falls back to the synchronous Call path, which ignores them.
    public class UANodeManager : CustomNodeManager2, ICallAsyncNodeManager, IWriteAsyncNodeManager
    {
        private uint _lastUsedId = 0;

        // Polling tasks are fire-and-forget; this CTS is the single source of truth
        // for "server is shutting down, exit gracefully" and lets us cancel the per-tag
        // sleep without waiting up to a full second for the next iteration.
        private readonly CancellationTokenSource _shutdownCts = new();
 
        private readonly NodeFactory _nodeFactory;

        private BaseObjectState _assetManagement;

        // The four dictionaries below are mutated from OPC UA service threads
        // (CreateAsset / DeleteAsset / OnWriteValue) and concurrently read by the
        // per-asset polling tasks. Plain Dictionary<> is not safe for that pattern
        // — a concurrent CreateAsset would throw InvalidOperationException inside
        // the polling foreach. ConcurrentDictionary makes reads, writes and
        // enumeration safe by design.
        private readonly ConcurrentDictionary<string, BaseDataVariableState> _uaVariables = new();

        private readonly ConcurrentDictionary<string, PropertyState> _uaProperties = new();

        private readonly ConcurrentDictionary<string, IAsset> _assets = new();

        private readonly ConcurrentDictionary<string, List<AssetTag>> _tags = new();

        private readonly ConcurrentDictionary<string, BaseObjectState> _eventSources = new();

        private readonly ConcurrentDictionary<string, AlarmConditionState> _alarmConditions = new();

        private readonly ConcurrentDictionary<string, (IEventingAsset Asset, EventHandler<AlarmEvent> Handler)> _eventSubscriptions = new();

        private sealed class AlarmProjectionQueue
        {
            public required Channel<AlarmEvent> Channel { get; init; }
            public required CancellationTokenSource Cancellation { get; init; }
            public required Task Worker { get; init; }
            public int DroppedEvents;
        }

        private readonly ConcurrentDictionary<string, AlarmProjectionQueue> _alarmProjectionQueues = new();

        // Reverse index: maps the OPC UA NodeId of a translated tag to its
        // owning asset and tag, so OnReadValue / OnWriteValue can resolve a
        // tag in O(1) instead of scanning every (asset, tag) pair on every
        // service call.
        private readonly ConcurrentDictionary<NodeId, (string AssetId, AssetTag Tag)> _tagIndex = new();

        private readonly UACloudLibraryClient _uacloudLibraryClient = new();

        private readonly Dictionary<NodeId, FileManager> _fileManagers = new();

        private const string _cWotCon = "http://opcfoundation.org/UA/WoT-Con/";

        private const uint _cWoTAssetManagement = 31;
        private const uint _cWoTCreateAsset = 32;
        private const uint _cWoTCreateAssetInputArguments = 33;
        private const uint _cWoTCreateAssetOutputArguments = 34;
        private const uint _cWoTDeleteAsset = 35;
        private const uint _cWoTDeleteAssetInputArguments = 36;
        private const uint _cIWoTAssetType = 42;
        private const uint _cWoTAssetFileType = 110;
        private const uint _cWoTAssetConfigurationType = 105;

        // Type-declaration (InstanceDeclaration) NodeIds of the WoT-Con methods on
        // WoTAssetConnectionManagementType / WoTAssetFileType. OPC UA Part 4
        // §5.12.2.2 (Table 59) allows a client to Call a Method using either the
        // per-instance Method NodeId or the type-declaration NodeId.
        //
        // Two resolution paths are needed because the WoT-Con methods reach their
        // parent object two different ways:
        //  * DiscoverAssets/CreateAssetForEndpoint/ConnectionTest are created as
        //    in-memory children and the per-asset WoTFile.CloseAndUpdate is a real
        //    FileType child, so setting MethodState.MethodDeclarationId lets the
        //    SDK's NodeState.FindMethod (which enumerates children) resolve both
        //    the instance and type-declaration NodeId to the same handler.
        //  * CreateAsset (i=32) and DeleteAsset (i=35) come from the imported
        //    nodeset and are only loosely coupled to the management object via the
        //    reference table (not in its children collection), so FindMethod never
        //    sees them. For those two the Call override below remaps the static
        //    type-declaration NodeId onto the instance NodeId before dispatch.
        private const uint _cWoTCreateAssetType = 26;
        private const uint _cWoTDeleteAssetType = 29;
        private const uint _cWoTDiscoverAssetsType = 41;
        private const uint _cWoTCreateAssetForEndpointType = 49;
        private const uint _cWoTConnectionTestType = 75;
        private const uint _cWoTCloseAndUpdateType = 111;

        private const int _reconnectInitialBackoffMs = 1000;
        private const int _reconnectMaxBackoffMs = 60_000;

        private sealed class ReconnectState
        {
            public int CurrentBackoffMs;
            public long NextAttemptTimestamp; // Stopwatch.GetTimestamp() ticks
            public int ConsecutiveFailures;
        }

        private readonly ConcurrentDictionary<string, ReconnectState> _reconnectStates = new();

        // The provisioning-mode gate is evaluated at the top of every OnReadValue /
        // OnWriteValue call. Enumerating the issuer-certs folder on every OPC UA read
        // and write produces continuous filesystem I/O and allocation churn once a
        // driver is sampling tags. The underlying state only changes at commissioning
        // time (issuer certs are added), so the result is cached with a short TTL: the
        // security semantics are preserved (sub-second staleness) while the per-call
        // filesystem hit is removed from the hot path.
        private const int _provisioningModeTtlMs = 1000;
        private int _provisioningModeCheckedAtTick;
        private bool _provisioningModeChecked;
        private volatile bool _provisioningModeCached;

        // Exposes the most recently constructed node manager so the diagnostics
        // UI can read the live connection state of onboarded assets without
        // taking a dependency on the OPC UA address space or its locks.
        public static UANodeManager Instance { get; private set; }

        public UANodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            Instance = this;

            SystemContext.NodeIdFactory = this;

            _nodeFactory = new NodeFactory(this);

            // create our settings folder, if required
            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "settings")))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "settings"));
            }

            // create our nodesets folder, if required
            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "nodesets")))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "nodesets"));
            }

            List<string> namespaceUris = new()
            {
                "http://opcfoundation.org/UA/EdgeTranslator/"
            };

            NamespaceUris = namespaceUris;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Signal all per-asset polling tasks to exit before tearing down
                // the rest of the address space; otherwise they would race against
                // the disposed file managers and disposed lock.
                if (_shutdownCts != null)
                {
                    try
                    {
                        _shutdownCts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // already disposed — nothing to do
                    }
                }

                if (_eventSubscriptions != null)
                {
                    foreach (var eventSubscription in _eventSubscriptions.Values)
                    {
                        try
                        {
                            eventSubscription.Asset.AlarmReceived -= eventSubscription.Handler;
                            eventSubscription.Asset.StopEventSubscription();
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Warning(ex, "Failed to stop event subscription during server shutdown.");
                        }
                    }

                    _eventSubscriptions.Clear();
                }

                if (_alarmProjectionQueues != null)
                {
                    foreach (string assetId in _alarmProjectionQueues.Keys.ToArray())
                    {
                        StopAlarmProjectionQueue(assetId);
                    }
                }

                if (_fileManagers != null && Lock != null)
                {
                    lock (Lock)
                    {
                        foreach (FileManager manager in _fileManagers.Values)
                        {
                            manager.Dispose();
                        }

                        _fileManagers.Clear();
                    }
                }

                _shutdownCts?.Dispose();
            }
        }

        public override NodeId New(ISystemContext context, NodeState node)
        {
            // for new nodes we create, pick our default namespace
            return new NodeId(Utils.IncrementIdentifier(ref _lastUsedId), (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
        }

        // OPC UA Part 4 §5.12.2.2 (Table 59) / v1.04 §5.11.2.2 (Table 65) allow a
        // client to Call a Method using the NodeId of the Method that is the
        // InstanceDeclaration on the ObjectType the addressed Object instantiates
        // ("form 2"), not just the per-instance Method NodeId ("form 1").
        //
        // CreateAsset and DeleteAsset come from the imported WoT-Con nodeset and
        // are only loosely coupled to the WoTAssetConnectionManagement object via
        // the reference table, so the base NodeState.FindMethod (which enumerates
        // in-memory children) cannot see them and a type-declaration methodId
        // resolves to nothing -> Bad_MethodInvalid. Remap the two static
        // type-declaration NodeIds onto their instance NodeIds before dispatch so
        // both forms reach the same handler. (The other WoT-Con methods are real
        // children and are handled via MethodState.MethodDeclarationId instead.)
        public override void Call(
            OperationContext context,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors)
        {
            RemapTypeDeclarationMethodIds(methodsToCall);
            base.Call(context, methodsToCall, results, errors);
        }

        // The server dispatches Call through this async path, which is the one that
        // runs MethodState.OnCallMethod2Async handlers; it needs the same remap.
        public override ValueTask CallAsync(
            OperationContext context,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            RemapTypeDeclarationMethodIds(methodsToCall);
            return base.CallAsync(context, methodsToCall, results, errors, cancellationToken);
        }

        // Southbound write outcomes for the client Write currently being processed,
        // keyed by variable NodeId. WriteAsync awaits the driver writes first (with no
        // lock held) and records the outcome here; the SDK's synchronous Write then
        // calls OnWriteValue, which applies the recorded outcome instead of doing the
        // I/O itself. AsyncLocal keeps concurrent client writes isolated.
        private static readonly AsyncLocal<Dictionary<NodeId, DriverWriteOutcome>> s_pendingDriverWrites = new();

        // Error is set when the write must fail (unknown asset or driver exception);
        // otherwise Connected decides the value's status code, as before.
        private readonly record struct DriverWriteOutcome(ServiceResult Error, bool Connected);

        // Declaring IWriteAsyncNodeManager makes the server dispatch Write here, so the
        // driver I/O is awaited rather than bridged. Everything else (access checks,
        // range and type validation, auditing, value update) still runs in base.Write.
        public async ValueTask WriteAsync(
            OperationContext context,
            ArrayOf<WriteValue> nodesToWrite,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            Dictionary<NodeId, DriverWriteOutcome> outcomes = null;

            for (int i = 0; i < nodesToWrite.Count; i++)
            {
                WriteValue nodeToWrite = nodesToWrite[i];
                if (nodeToWrite == null
                    || nodeToWrite.Processed
                    || nodeToWrite.AttributeId != Attributes.Value
                    || !_tagIndex.TryGetValue(nodeToWrite.NodeId, out var indexed)
                    || Find(nodeToWrite.NodeId) is not BaseDataVariableState variable
                    || !IsDriverWriteAllowed(variable, nodeToWrite))
                {
                    continue;
                }

                outcomes ??= new Dictionary<NodeId, DriverWriteOutcome>();
                outcomes[nodeToWrite.NodeId] = await WriteToDriverAsync(indexed.AssetId, indexed.Tag, nodeToWrite.Value.WrappedValue, cancellationToken).ConfigureAwait(false);
            }

            Dictionary<NodeId, DriverWriteOutcome> previous = s_pendingDriverWrites.Value;
            s_pendingDriverWrites.Value = outcomes;
            try
            {
                base.Write(context, nodesToWrite, errors);
            }
            finally
            {
                s_pendingDriverWrites.Value = previous;
            }
        }

        // Mirrors the checks the SDK's own Write applies before it calls OnWriteValue,
        // so a write the server will reject is never sent to the device. Only checks
        // base.Write really performs belong here: an extra check would silently drop
        // writes the server would otherwise accept.
        private bool IsDriverWriteAllowed(BaseDataVariableState variable, WriteValue nodeToWrite)
        {
            if (IsInProvisioningMode() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE")))
            {
                return false;
            }

            if ((variable.AccessLevel & AccessLevels.CurrentWrite) == 0
                || (variable.UserAccessLevel & AccessLevels.CurrentWrite) == 0)
            {
                return false;
            }

            return string.IsNullOrEmpty(nodeToWrite.IndexRange);
        }

        private async ValueTask<DriverWriteOutcome> WriteToDriverAsync(string assetId, AssetTag tag, Variant value, CancellationToken cancellationToken)
        {
            try
            {
                if (!_assets.TryGetValue(assetId, out IAsset asset))
                {
                    Program.Telemetry.TagWriteErrors.Add(1);
                    return new DriverWriteOutcome(new ServiceResult(StatusCodes.BadDataUnavailable), false);
                }

                Program.Telemetry.TagWrites.Add(1);

                // Unwrap the Variant to the CLR value drivers have always received;
                // see VariantConversion for why a plain object assignment is wrong.
                await asset.WriteAsync(tag, VariantConversion.ToClrValue(value), cancellationToken).ConfigureAwait(false);

                return new DriverWriteOutcome(null, asset.IsConnected);
            }
            catch (Exception ex)
            {
                Program.Telemetry.TagWriteErrors.Add(1);
                // Log full exception server-side, return only a generic status to the
                // OPC UA client to avoid leaking internal exception text or stack traces.
                Log.Logger.Error(ex, "Failed to write value for tag: {TagName}", tag.Name);
                return new DriverWriteOutcome(new ServiceResult(StatusCodes.BadDataUnavailable), false);
            }
        }

        private void RemapTypeDeclarationMethodIds(ArrayOf<CallMethodRequest> methodsToCall)
        {
            int wotConNamespaceIndex = Server.NamespaceUris.GetIndex(_cWotCon);
            if (wotConNamespaceIndex > 0 && methodsToCall != null)
            {
                ushort ns = (ushort)wotConNamespaceIndex;
                NodeId managementObjectId = new NodeId(_cWoTAssetManagement, ns);
                NodeId createAssetType = new NodeId(_cWoTCreateAssetType, ns);
                NodeId deleteAssetType = new NodeId(_cWoTDeleteAssetType, ns);

                foreach (CallMethodRequest request in methodsToCall)
                {
                    if (request == null || request.ObjectId != managementObjectId)
                    {
                        continue;
                    }

                    if (request.MethodId == createAssetType)
                    {
                        request.MethodId = new NodeId(_cWoTCreateAsset, ns);
                    }
                    else if (request.MethodId == deleteAssetType)
                    {
                        request.MethodId = new NodeId(_cWoTDeleteAsset, ns);
                    }
                }
            }
        }

        // Returns a point-in-time snapshot of every onboarded asset and its
        // current southbound connection state. Reads are lock-free because the
        // backing collections are ConcurrentDictionary; any per-asset failure is
        // swallowed so a single misbehaving driver cannot break the dashboard.
        public IReadOnlyList<ConnectedAssetInfo> GetConnectedAssets()
        {
            List<ConnectedAssetInfo> result = new();

            foreach (KeyValuePair<string, IAsset> entry in _assets)
            {
                IAsset asset = entry.Value;

                bool connected = false;
                string endpoint = string.Empty;

                try
                {
                    connected = asset?.IsConnected ?? false;
                    endpoint = asset?.GetRemoteEndpoint() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    Log.Logger.Debug(ex, "Failed to read diagnostics status for asset {AssetId}", entry.Key);
                }

                int tagCount = _tags.TryGetValue(entry.Key, out List<AssetTag> tags) ? tags.Count : 0;

                result.Add(new ConnectedAssetInfo(entry.Key, connected, endpoint, tagCount));
            }

            return result;
        }

        // Asset names flow into filesystem paths (settings/<name>.jsonld), into
        // namespace URIs ("http://opcfoundation.org/UA/<name>/"), and become the
        // dictionary keys for _assets / _tags. A caller-controlled name like
        // "../etc/passwd" or one containing path separators / NUL would let an
        // OPC UA client write outside of /app/settings or hijack another asset's
        // tag-polling state. Reject anything that isn't a small set of safe
        // characters before we commit it anywhere.
        private const int _cMaxAssetNameLength = 128;

        private bool IsSafeAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (name.Length > _cMaxAssetNameLength)
            {
                return false;
            }

            // Must round-trip through Path.GetFileName unchanged (rejects "..",
            // path separators on every OS, alternate streams, etc.).
            if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
            {
                return false;
            }

            foreach (char c in name)
            {
                // Allow letters, digits, '.', '_', '-' only. This keeps the value
                // safe to embed in URIs, file names and OPC UA browse names
                // without escaping.
                bool ok = char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-';
                if (!ok)
                {
                    return false;
                }
            }

            // Disallow leading dot to avoid Unix hidden files / "..".
            if (name[0] == '.')
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns a string that is guaranteed to satisfy
        /// <see cref="IsSafeAssetName"/>: every disallowed character is
        /// replaced with '_', a leading dot is prefixed with '_', empty /
        /// whitespace input falls back to "Asset", and over-long names are
        /// truncated. Used for the local-file load path where the asset name
        /// is derived from a file on the trusted server disk; the OPC UA
        /// upload path still rejects unsafe names outright.
        /// </summary>
        private static string SanitizeAssetName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "Asset";
            }

            char[] chars = raw.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                {
                    chars[i] = '_';
                }
            }

            string sanitized = new string(chars);
            if (sanitized[0] == '.')
            {
                sanitized = "_" + sanitized;
            }

            if (sanitized.Length > _cMaxAssetNameLength)
            {
                sanitized = sanitized.Substring(0, _cMaxAssetNameLength);
            }

            return sanitized;
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            IList<IReference> objectsFolderReferences = null;

            lock (Lock)
            {
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out objectsFolderReferences))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = objectsFolderReferences = new List<IReference>();
                }
            }

            // Async methods internally acquire Lock where needed.
            // Holding Lock here while blocking would deadlock with AddNamespace's lock(Lock) on the continuation thread.
            // AsyncBridge.RunSync offloads the await onto the thread pool with no captured SynchronizationContext,
            // which is the only safe way to bridge async work into this synchronous SDK override.
            AsyncBridge.RunSync(() => AddNodesetAsync(_cWotCon));

            lock (Lock)
            {
                AddNodesForAssetManagement();
            }

            AsyncBridge.RunSync(LoadLocalWoTFilesAsync);

            lock (Lock)
            {
                AddReverseReferences(externalReferences);
                base.CreateAddressSpace(externalReferences);
            }
        }

        private async Task LoadLocalWoTFilesAsync()
        {
            IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
            foreach (string file in WoTFiles)
            {
                try
                {
                    string contents = System.IO.File.ReadAllText(file);
                    string fileName = Path.GetFileNameWithoutExtension(file);

                    // Files in the local settings directory live on the trusted
                    // server disk, so sanitize the asset name instead of
                    // skipping the file. The OPC UA upload path still rejects
                    // unsafe names outright via IsSafeAssetName.
                    string assetName = SanitizeAssetName(fileName);
                    if (!string.Equals(assetName, fileName, StringComparison.Ordinal))
                    {
                        Log.Logger.Warning("Sanitized WoT file name [{Original}] -> [{Sanitized}] for {File}", fileName, assetName, file);
                    }

                    if (!CreateAssetNode(assetName, out NodeState assetNode))
                    {
                        throw new Exception("Asset already exists");
                    }

                    await OnboardAssetFromWoTFileAsync(assetNode, contents).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // skip this file, but log an error
                    Log.Logger.Error(ex, "Failed to load WoT file: {FileName}", file);
                }
            }
        }

        private void AddNodesForAssetManagement()
        {
            ushort WoTConNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex(_cWotCon);

            _assetManagement = FindPredefinedNode<BaseObjectState>(new NodeId(_cWoTAssetManagement, WoTConNamespaceIndex));

            MethodState createAsset = FindPredefinedNode<MethodState>(new NodeId(_cWoTCreateAsset, WoTConNamespaceIndex));
            createAsset.OnCallMethod = new GenericMethodCalledEventHandler(OnCreateAsset);
            createAsset.InputArguments = _nodeFactory.CreateMethodArguments(createAsset, ["AssetName"], ["A unique name for the asset."], [new ExpandedNodeId(DataTypes.String)], true, false, new NodeId(_cWoTCreateAssetInputArguments, WoTConNamespaceIndex));
            createAsset.OutputArguments = _nodeFactory.CreateMethodArguments(createAsset, ["AssetId"], ["The NodeId of the WoTAsset object, if call was successful."], [new ExpandedNodeId(DataTypes.NodeId)], false, false, new NodeId(_cWoTCreateAssetOutputArguments, WoTConNamespaceIndex));

            MethodState deleteAsset = FindPredefinedNode<MethodState>(new NodeId(_cWoTDeleteAsset, WoTConNamespaceIndex));
            deleteAsset.OnCallMethod2Async = OnDeleteAssetAsync;
            deleteAsset.InputArguments = _nodeFactory.CreateMethodArguments(deleteAsset, ["AssetId"], ["The NodeId of the WoTAsset object."], [new ExpandedNodeId(DataTypes.NodeId)], true, false, new NodeId(_cWoTDeleteAssetInputArguments, WoTConNamespaceIndex));

            MethodState discoverAssets = _nodeFactory.CreateMethod(_assetManagement, "DiscoverAssets", WoTConNamespaceIndex);
            discoverAssets.OnCallMethod = new GenericMethodCalledEventHandler(OnDiscoverAssets);
            discoverAssets.MethodDeclarationId = new NodeId(_cWoTDiscoverAssetsType, WoTConNamespaceIndex);
            discoverAssets.OutputArguments = _nodeFactory.CreateMethodArguments(discoverAssets, ["AssetEndpoints"], ["The list of discovered asset endpoints."], [new ExpandedNodeId(DataTypes.String)], false, true);
            AddPredefinedNode(SystemContext, discoverAssets);

            MethodState createAssetForEndpoint = _nodeFactory.CreateMethod(_assetManagement, "CreateAssetForEndpoint", WoTConNamespaceIndex);
            createAssetForEndpoint.OnCallMethod2Async = OnCreateAssetForEndpointAsync;
            createAssetForEndpoint.MethodDeclarationId = new NodeId(_cWoTCreateAssetForEndpointType, WoTConNamespaceIndex);
            createAssetForEndpoint.InputArguments = _nodeFactory.CreateMethodArguments(createAssetForEndpoint, ["AssetName", "AssetEndpoint"], ["The name to be assigned to the asset.", "The endpoint to the asset on the network."], [new ExpandedNodeId(DataTypes.String), new ExpandedNodeId(DataTypes.String)], true);
            createAssetForEndpoint.OutputArguments = _nodeFactory.CreateMethodArguments(createAssetForEndpoint, ["AssetId"], ["The NodeId of the WoTAsset object, if call was successful."], [new ExpandedNodeId(DataTypes.NodeId)], false);
            AddPredefinedNode(SystemContext, createAssetForEndpoint);

            MethodState connectionTest = _nodeFactory.CreateMethod(_assetManagement, "ConnectionTest", WoTConNamespaceIndex);
            connectionTest.OnCallMethod2Async = OnConnectionTestAsync;
            connectionTest.MethodDeclarationId = new NodeId(_cWoTConnectionTestType, WoTConNamespaceIndex);
            connectionTest.InputArguments = _nodeFactory.CreateMethodArguments(connectionTest, ["AssetEndpoint"], ["The endpoint description of the asset to test the connection to."], [new ExpandedNodeId(DataTypes.String)], true);
            connectionTest.OutputArguments = _nodeFactory.CreateMethodArguments(connectionTest, ["Success", "Status"], ["Returns TRUE if a connection could be established to the asset.", "If a connection was established successfully, an asset-specific status code string describing the current health of the asset is returned."], [new ExpandedNodeId(DataTypes.String), new ExpandedNodeId(DataTypes.String)], false);
            AddPredefinedNode(SystemContext, connectionTest);

            // create file node to upload local nodeset files to the server
            FileState fileNode = new(_assetManagement);
            fileNode.Create(SystemContext, new NodeId("NodesetFileUpload", WoTConNamespaceIndex), new QualifiedName("NodesetFileUpload"), Ua.LocalizedText.Null, false);
            fileNode.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            _assetManagement.AddChild(fileNode);

            FileManager fileManager = new(this, fileNode);
            _fileManagers.Add(_assetManagement.NodeId, fileManager);
            AddPredefinedNode(SystemContext, fileNode);

            // create a property listing our supported WoT protocol bindings
            PropertyState supportedWoTBindingsProperty = _nodeFactory.CreateProperty(_assetManagement, "SupportedWoTBindings", DataTypes.UriString, WoTConNamespaceIndex, false);
            AddPredefinedNode(SystemContext, supportedWoTBindingsProperty);

            // create a property listing our supported OPC UA nodesets to map to
            PropertyState supportedOPCUAInfoModelsProperty = _nodeFactory.CreateProperty(_assetManagement, "SupportedOPCUAInfoModels", DataTypes.UriString, WoTConNamespaceIndex, false);
            AddPredefinedNode(SystemContext, supportedOPCUAInfoModelsProperty);

            BaseObjectState configuration = _nodeFactory.CreateObject(
                _assetManagement,
                "Configuration",
                new ExpandedNodeId(_cWoTAssetConfigurationType, _cWotCon), WoTConNamespaceIndex);
            AddPredefinedNode(SystemContext, configuration);

            // create a property for the license key
            PropertyState licenseProperty = _nodeFactory.CreateProperty(configuration, "License", DataTypes.String, WoTConNamespaceIndex, true, string.Empty);
            _uaProperties["License"] = licenseProperty;
            AddPredefinedNode(SystemContext, licenseProperty);

            // create a variable for the current memory working set
            BaseDataVariableState variable = _nodeFactory.CreateVariable(_assetManagement, "MemoryWorkingSet(MB)", DataTypes.Int32, WoTConNamespaceIndex);
            AddPredefinedNode(SystemContext, variable);
        }

        private async Task LoadNamespacesFromThingDescriptionAsync(ThingDescription td)
        {
            // check if an OPC UA companion spec is mentioned in the WoT TD file
            foreach (object ns in td.Context)
            {
                try
                {
                    if (ns.ToString().Contains("https://www.w3.org/") && !ns.ToString().Contains("uav:"))
                    {
                        continue;
                    }

                    Uri namespaceUri = new(((JValue)((JProperty)((JContainer)ns).First).Value).Value.ToString());
                    if (namespaceUri != null)
                    {
                        await AddNodesetAsync(namespaceUri.ToString()).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "Failed to load namespace from Thing Description: {Namespace}", ns);
                }
            }
        }

        public async Task AddNodesetAsync(string namespaceUri)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                Log.Logger.Error("Namespace URI is null or empty.");
                return;
            }

            // special case: Our WoT-Con nodeset is always available
            string nodesetXml;
            if (namespaceUri == _cWotCon)
            {
                nodesetXml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Opc.Ua.WotCon.NodeSet2.xml"));
            }
            else
            {
                // download nodeset from UA Cloud Library, if required
                nodesetXml = await _uacloudLibraryClient.DownloadNodesetAsync(namespaceUri).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(nodesetXml))
            {
                Log.Logger.Error($"Required nodeset {namespaceUri} not found.");
                return;
            }

            using (Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(nodesetXml)))
            {
                UANodeSet nodeSet = UANodeSet.Read(stream);

                // first load dependencies
                if ((nodeSet.Models != null) && (nodeSet.Models.Length > 0))
                {
                    foreach (ModelTableEntry te in nodeSet.Models)
                    {
                        if (te.RequiredModel != null && (te.RequiredModel.Length > 0))
                        {
                            foreach (ModelTableEntry rm in te.RequiredModel)
                            {
                                if (rm.ModelUri == Ua.Namespaces.OpcUa)
                                {
                                    // skip the base UA nodeset as it is always loaded
                                    continue;
                                }

                                if (NamespaceUris.Contains(rm.ModelUri))
                                {
                                    // skip any dependent nodesets already loaded
                                    continue;
                                }

                                // recursively add the dependent nodeset
                                await AddNodesetAsync(rm.ModelUri).ConfigureAwait(false);
                            }
                        }
                    }
                }

                if ((nodeSet.NamespaceUris != null) && (nodeSet.NamespaceUris.Length > 0))
                {
                    foreach (string ns in nodeSet.NamespaceUris)
                    {
                        AddNamespace(ns);
                    }
                }

                NodeStateCollection predefinedNodes = new NodeStateCollection();
                nodeSet.Import(SystemContext, predefinedNodes);
                for (int i = 0; i < predefinedNodes.Count; i++)
                {
                    try
                    {
                        AddPredefinedNode(SystemContext, predefinedNodes[i]);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error(ex, "Failed to add predefined node: {NodeId}", predefinedNodes[i]?.NodeId);
                    }
                }

                // ensure reverse references are registered for the predefined nodes added at runtime
                Dictionary<NodeId, IList<IReference>> externalReferences = new();
                AddReverseReferences(externalReferences);

                // apply external refs via MasterNodeManager
                foreach (var kvp in externalReferences)
                {
                    await Server.NodeManager.AddReferencesAsync(kvp.Key, kvp.Value).ConfigureAwait(false);
                }

                RaiseModelChangedEvent(ObjectIds.TypesFolder, ModelChangeStructureVerbMask.NodeAdded);
            }
        }

        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                base.DeleteAddressSpace();
            }
        }

        // Reads a method argument as a string with the same semantics as the pre-2.0.0
        // `object?.ToString()`: a missing/null argument yields null. Do not replace this
        // with Variant.ToString(): it renders a null value as the literal "<null>",
        // which is non-empty and would slip past the IsNullOrEmpty argument guards,
        // so missing arguments would no longer be rejected with BadInvalidArgument.
        private static string ArgumentAsString(ArrayOf<Variant> arguments, int index)
        {
            return arguments[index].AsBoxedObject()?.ToString();
        }

        public ServiceResult OnCreateAsset(ISystemContext context, MethodState method, ArrayOf<Variant> inputArguments, List<Variant> outputArguments)
        {
            ServiceResult authorized = WotCon.AssetManagementAuthorization.Authorize(context, "CreateAsset", mutates: true);
            if (ServiceResult.IsBad(authorized))
            {
                return authorized;
            }

            string assetName = ArgumentAsString(inputArguments, 0);
            if (!IsSafeAssetName(assetName))
            {
                Log.Logger.Warning("Rejecting CreateAsset: invalid asset name [{AssetName}]", assetName);
                return StatusCodes.BadInvalidArgument;
            }

            bool success = CreateAssetNode(assetName, out NodeState assetNode);
            if (!success)
            {
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated, new Opc.Ua.LocalizedText(assetNode.NodeId.ToString()));
            }
            else
            {
                outputArguments[0] = Variant.From(assetNode.NodeId);

                RaiseModelChangedEvent(assetNode.NodeId, ModelChangeStructureVerbMask.NodeAdded);

                return ServiceResult.Good;
            }
        }

        private bool CreateAssetNode(string assetName, out NodeState assetNode)
        {
            lock (Lock)
            {
                // check if the asset node already exists
                INodeBrowser browser = _assetManagement.CreateBrowser(SystemContext, null, ReferenceTypeIds.Organizes, true, BrowseDirection.Forward, QualifiedName.Null, null, true);

                for (IReference reference = browser.Next(); reference != null; reference = browser.Next())
                {
                    NodeState target = Find(ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris));
                    if ((target != null) && (target.DisplayName.Text == assetName))
                    {
                        assetNode = target;
                        return false;
                    }
                }

                NodeId iWoTAssetTypeId = ExpandedNodeId.ToNodeId(new ExpandedNodeId(_cIWoTAssetType, _cWotCon), Server.NamespaceUris);

                ushort WoTConNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex(_cWotCon);

                BaseObjectState asset = new(null);
                asset.Create(SystemContext, new NodeId(), new QualifiedName(assetName, WoTConNamespaceIndex), new Opc.Ua.LocalizedText(assetName), true);
                asset.TypeDefinitionId = ObjectTypeIds.BaseObjectType;

                asset.AddReference(ReferenceTypeIds.HasInterface, false, iWoTAssetTypeId);

                FileState fileNode = new(asset);
                fileNode.Create(SystemContext, new NodeId(), new QualifiedName("WoTFile", WoTConNamespaceIndex), Ua.LocalizedText.Null, true);
                fileNode.TypeDefinitionId = ExpandedNodeId.ToNodeId(new ExpandedNodeId(_cWoTAssetFileType, _cWotCon), Server.NamespaceUris);
                fileNode.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                asset.AddChild(fileNode);

                _assetManagement.AddReference(ReferenceTypeIds.Organizes, false, asset.NodeId);
                asset.AddReference(ReferenceTypeIds.Organizes, true, _assetManagement.NodeId);

                FileManager fileManager = new(this, fileNode);
                _fileManagers.Add(asset.NodeId, fileManager);

                // OPC UA Part 4 §5.12.2.2 (Table 59) lets a client Call this
                // per-asset CloseAndUpdate method using the WoTAssetFileType
                // type-declaration NodeId (ns=WoT-Con;i=111) instead of the
                // per-instance NodeId. Point the instance method's
                // MethodDeclarationId at that declaration so the SDK's
                // NodeState.FindMethod resolves both forms to this handler.
                fileNode.Close.MethodDeclarationId = new NodeId(_cWoTCloseAndUpdateType, WoTConNamespaceIndex);

                AddPredefinedNode(SystemContext, asset);

                assetNode = asset;
                return true;
            }
        }

        public void RaiseModelChangedEvent(NodeId nodeId, ModelChangeStructureVerbMask verb)
        {
            ModelChangeStructureDataType[] changes = new ModelChangeStructureDataType[]
            {
                new ModelChangeStructureDataType
                {
                    Affected = nodeId,
                    AffectedType = ObjectTypeIds.BaseObjectType,
                    Verb = (byte)verb
                }
            };

            var ev = new GeneralModelChangeEventState(null);

            ev.Initialize(SystemContext, null, EventSeverity.Medium, new Ua.LocalizedText("Model change"));
            // PropertyState<T> is abstract in SDK 2.0.0; use the event's typed factory,
            // which builds the correctly-typed PropertyState<ArrayOf<...>> child.
            ev.Changes = ev.CreateOrReplaceChanges(SystemContext, null, false);
            ev.Changes.Value = new ArrayOf<ModelChangeStructureDataType>(changes);

            // SourceNode is NodeId-typed (OPC UA Part 5 BaseEventType); pass the node's id,
            // not the node object itself.
            ev.SetChildValue(SystemContext, BrowseNames.SourceNode, _assetManagement?.NodeId ?? NodeId.Null, false);
            ev.SetChildValue(SystemContext, BrowseNames.SourceName, "AssetManagement", false);

            Server.ReportEvent(ev);
        }

        public async ValueTask<ServiceResult> OnDeleteAssetAsync(ISystemContext context, MethodState method, NodeId objectId, ArrayOf<Variant> inputArguments, List<Variant> outputArguments, CancellationToken cancellationToken)
        {
            ServiceResult authorized = WotCon.AssetManagementAuthorization.Authorize(context, "DeleteAsset", mutates: true);
            if (ServiceResult.IsBad(authorized))
            {
                return authorized;
            }

            if (string.IsNullOrEmpty(ArgumentAsString(inputArguments, 0)))
            {
                return StatusCodes.BadInvalidArgument;
            }

            NodeId assetId = (NodeId)inputArguments[0];

            // The driver's connection is released after the lock: C# cannot await
            // inside a lock, and the asset is already unreachable by then.
            IAsset assetToDisconnect = null;
            string disconnectedAssetName = null;

            lock (Lock)
            {
                NodeState asset = FindPredefinedNode<BaseObjectState>(assetId);
                if (asset == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                string assetName = asset.DisplayName.Text;

                // Dispose the FileManager (closes any open upload MemoryStream
                // handles and clears its handle table) before dropping it;
                // Remove() alone would leak those streams until finalization.
                if (_fileManagers.Remove(assetId, out FileManager fileManager))
                {
                    fileManager.Dispose();
                }

                _assetManagement.RemoveReference(ReferenceTypeIds.Organizes, false, assetId);
                asset.RemoveReference(ReferenceTypeIds.Organizes, true, _assetManagement.NodeId);

                DeleteNode(SystemContext, assetId);

                IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
                foreach (string file in WoTFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (fileName == assetName)
                    {
                        System.IO.File.Delete(file);
                    }
                }

                if (_tags.ContainsKey(assetName))
                {
                    _tags.TryRemove(assetName, out _);
                }

                if (_assets.TryRemove(assetName, out IAsset removedAsset))
                {
                    if (_eventSubscriptions.TryRemove(assetName, out var eventSubscription))
                    {
                        try
                        {
                            eventSubscription.Asset.AlarmReceived -= eventSubscription.Handler;
                            eventSubscription.Asset.StopEventSubscription();
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Warning(ex, "Failed to stop event subscription for asset {AssetId} during delete.", assetName);
                        }
                    }

                    StopAlarmProjectionQueue(assetName);

                    assetToDisconnect = removedAsset;
                    disconnectedAssetName = assetName;
                }

                _reconnectStates.TryRemove(assetName, out _);
                _eventSources.TryRemove(assetName, out _);
                foreach (var entry in _alarmConditions.Where(e => e.Key.StartsWith(assetName + ":", StringComparison.Ordinal)).ToArray())
                {
                    _alarmConditions.TryRemove(entry.Key, out _);
                }

                // Drop NodeId -> tag entries for this asset from the reverse index.
                foreach (var entry in _tagIndex.Where(e => e.Value.AssetId == assetName).ToArray())
                {
                    _tagIndex.TryRemove(entry.Key, out _);
                }

                string keyPrefix = assetName + ":";
                List<string> variableKeysToRemove = _uaVariables.Keys
                    .Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal))
                    .ToList();
                foreach (string key in variableKeysToRemove)
                {
                    _uaVariables.TryRemove(key, out _);
                }

                // _uaProperties is keyed by the same "{assetName}:..." variableId as
                // _uaVariables. Prune it here too; otherwise every onboarded property
                // (and the address-space subtree it keeps alive via PropertyState.Parent)
                // leaks for the process lifetime across asset delete/re-create cycles.
                List<string> propertyKeysToRemove = _uaProperties.Keys
                    .Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal))
                    .ToList();
                foreach (string key in propertyKeysToRemove)
                {
                    _uaProperties.TryRemove(key, out _);
                }

                RaiseModelChangedEvent(asset.NodeId, ModelChangeStructureVerbMask.NodeDeleted);
            }

            // Release the southbound connection (socket/stream) the driver holds.
            // Dropping only the dictionary reference leaks it until finalization.
            // Defensive: a faulty driver must not fail delete.
            if (assetToDisconnect != null)
            {
                try
                {
                    await assetToDisconnect.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning(ex, "Failed to disconnect asset {AssetId} during delete.", disconnectedAssetName);
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Unloads a WoT file from the running server: it locates the asset created
        /// from the file by name and removes it through the same code path used by the
        /// OPC UA DeleteAsset method (<see cref="OnDeleteAsset"/>), which deletes the
        /// address-space nodes, disconnects the southbound driver, prunes the tag state
        /// and removes the .jsonld file from the settings folder. Returns true when a
        /// matching asset was found and unloaded.
        /// </summary>
        public async Task<bool> UnloadWoTFileAsync(string fileName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            // The asset node display name matches the settings file name without its
            // extension (see LoadLocalWoTFilesAsync / ImportWoTFileAsync).
            string assetName = Path.GetFileNameWithoutExtension(fileName);

            NodeId assetId;
            lock (Lock)
            {
                assetId = FindAssetNodeIdByName(assetName);
            }

            if (assetId.IsNull)
            {
                Log.Logger.Warning("Cannot unload WoT file [{FileName}]: no matching asset is currently loaded.", fileName);
                return false;
            }

            ServiceResult result = await OnDeleteAssetAsync(SystemContext, null, NodeId.Null, new ArrayOf<Variant>(new[] { Variant.From(assetId) }), new List<Variant>(), cancellationToken).ConfigureAwait(false);

            bool success = result.StatusCode == StatusCodes.Good;
            if (success)
            {
                Log.Logger.Information("Unloaded WoT file [{FileName}] (asset [{AssetName}]).", fileName, assetName);
            }
            else
            {
                Log.Logger.Warning("Failed to unload WoT file [{FileName}]: {Status}", fileName, result.StatusCode);
            }

            return success;
        }

        // Resolves the NodeId of an asset under the AssetManagement object by its
        // display name. The caller must hold Lock while browsing the address space.
        private NodeId FindAssetNodeIdByName(string assetName)
        {
            if (_assetManagement == null || string.IsNullOrEmpty(assetName))
            {
                return NodeId.Null;
            }

            INodeBrowser browser = _assetManagement.CreateBrowser(SystemContext, null, ReferenceTypeIds.Organizes, true, BrowseDirection.Forward, QualifiedName.Null, null, true);

            for (IReference reference = browser.Next(); reference != null; reference = browser.Next())
            {
                NodeState target = Find(ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris));
                if ((target != null) && (target.DisplayName.Text == assetName))
                {
                    return target.NodeId;
                }
            }

            return NodeId.Null;
        }

        public ServiceResult OnDiscoverAssets(ISystemContext context, MethodState method, ArrayOf<Variant> inputArguments, List<Variant> outputArguments)
        {
            ServiceResult authorized = WotCon.AssetManagementAuthorization.Authorize(context, "DiscoverAssets", mutates: false);
            if (ServiceResult.IsBad(authorized))
            {
                return authorized;
            }

            List<string> allAddresses = new();

            try
            {
                foreach (var drv in Program.Drivers.AllDrivers)
                {
                    foreach (string endpoint in drv.Discover())
                    {
                        // WoT-Con 1.1 §Security: endpoints DiscoverAssets reports get the
                        // same endpoint policy as caller-supplied ones, so a driver can't
                        // surface (and a client can't then onboard) a blocked address.
                        if (ServiceResult.IsBad(WotCon.AssetEndpointPolicy.Validate(endpoint, Program.Drivers)))
                        {
                            Log.Logger.Warning("DiscoverAssets dropped endpoint {Endpoint}: rejected by the endpoint policy.", endpoint);
                            continue;
                        }

                        allAddresses.Add(endpoint);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log full exception server-side, return only a generic status to
                // the OPC UA client to avoid leaking internal exception text.
                Log.Logger.Error(ex, "Failed to discover assets");
                return new ServiceResult(StatusCodes.BadTimeout);
            }

            outputArguments[0] = Variant.From(allAddresses.ToArray());

            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> OnCreateAssetForEndpointAsync(ISystemContext context, MethodState method, NodeId objectId, ArrayOf<Variant> inputArguments, List<Variant> outputArguments, CancellationToken cancellationToken)
        {
            ServiceResult authorized = WotCon.AssetManagementAuthorization.Authorize(context, "CreateAssetForEndpoint", mutates: true);
            if (ServiceResult.IsBad(authorized))
            {
                return authorized;
            }

            if (string.IsNullOrEmpty(ArgumentAsString(inputArguments, 0)) || string.IsNullOrEmpty(ArgumentAsString(inputArguments, 1)))
            {
                return StatusCodes.BadInvalidArgument;
            }

            string assetName = ArgumentAsString(inputArguments, 0);
            if (!IsSafeAssetName(assetName))
            {
                Log.Logger.Warning("Rejecting CreateAssetForEndpoint: invalid asset name [{AssetName}]", assetName);
                return StatusCodes.BadInvalidArgument;
            }

            string assetEndpoint = ArgumentAsString(inputArguments, 1);
            if (!Program.Drivers.TryGetByUri(assetEndpoint, out var drv))
            {
                return new ServiceResult(StatusCodes.BadNotSupported, new Ua.LocalizedText($"No driver installed for endpoint: {assetEndpoint}"));
            }

            // WoT-Con 1.1 §Security: a caller-chosen endpoint passes the same
            // endpoint policy the binder applies before the server connects to it.
            // Checked before the asset node exists so a rejection leaves nothing behind.
            ServiceResult endpointCheck = WotCon.AssetEndpointPolicy.Validate(assetEndpoint, Program.Drivers);
            if (ServiceResult.IsBad(endpointCheck))
            {
                Log.Logger.Warning("Rejecting CreateAssetForEndpoint: endpoint {Endpoint} violates the endpoint policy.", assetEndpoint);
                return endpointCheck;
            }

            bool success = CreateAssetNode(assetName, out NodeState assetNode);
            if (!success)
            {
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated, new Ua.LocalizedText(assetNode.NodeId.ToString()));
            }
            else
            {
                outputArguments[0] = assetNode.NodeId;

                // generate WoT File contents
                ThingDescription td = drv.BrowseAndGenerateTD(assetName, assetEndpoint);

                string contents = JsonConvert.SerializeObject(td, Formatting.Indented);

                _fileManagers[assetNode.NodeId].Write(context, Encoding.UTF8.GetBytes(contents));

                await OnboardAssetFromWoTFileAsync(assetNode, contents).ConfigureAwait(false);

                System.IO.File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings", assetName + ".jsonld"), contents);

                RaiseModelChangedEvent(assetNode.NodeId, ModelChangeStructureVerbMask.NodeAdded);

                return ServiceResult.Good;
            }
        }

        private async ValueTask<ServiceResult> OnConnectionTestAsync(ISystemContext context, MethodState method, NodeId objectId, ArrayOf<Variant> inputArguments, List<Variant> outputArguments, CancellationToken cancellationToken)
        {
            ServiceResult authorized = WotCon.AssetManagementAuthorization.Authorize(context, "ConnectionTest", mutates: false);
            if (ServiceResult.IsBad(authorized))
            {
                return authorized;
            }

            if (string.IsNullOrEmpty(ArgumentAsString(inputArguments, 0)))
            {
                return StatusCodes.BadInvalidArgument;
            }

            string endpoint = ArgumentAsString(inputArguments, 0).Trim();

            // Use a TCP probe instead of ICMP. Ping requires CAP_NET_RAW which
            // is not granted to a non-root container by default, so the SDK's
            // hardened image cannot raise raw sockets and would always fail
            // the test. TCP probes also reflect the actual reachability of
            // the OPC UA / Modbus / EIP / S7 service the operator wants to
            // talk to, not just the host's ICMP responder.
            string host = endpoint;
            int port = 0;

            // accept "host", "host:port", "scheme://host:port[/path]"
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri))
            {
                host = uri.Host;
                port = uri.IsDefaultPort ? 0 : uri.Port;
            }
            else
            {
                int colon = endpoint.LastIndexOf(':');
                if (colon > 0 && colon < endpoint.Length - 1
                    && int.TryParse(endpoint[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort)
                    && parsedPort > 0 && parsedPort <= 65535)
                {
                    host = endpoint[..colon];
                    port = parsedPort;
                }
            }

            if (port <= 0)
            {
                outputArguments[0] = false;
                outputArguments[1] = $"Connection test to '{endpoint}' rejected: a TCP port is required (e.g. '{endpoint}:4840').";
                Log.Logger.Warning("ConnectionTest rejected: missing port in endpoint {Endpoint}", endpoint);
                return StatusCodes.BadInvalidArgument;
            }

            // WoT-Con 1.1 §Security: the caller chooses this address, so the same
            // endpoint policy as the binder applies and the server must not be
            // usable as a probe against the cloud instance-metadata service.
            if (WotCon.AssetEndpointPolicy.IsAlwaysBlockedHost(host))
            {
                outputArguments[0] = false;
                outputArguments[1] = $"Connection test to '{endpoint}' rejected by the endpoint policy.";
                Log.Logger.Warning("ConnectionTest rejected by endpoint policy: {Endpoint}", endpoint);
                return StatusCodes.BadUserAccessDenied;
            }

            const int probeTimeoutMs = 3000;
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token, cancellationToken);
                cts.CancelAfter(probeTimeoutMs);

                await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);

                sw.Stop();
                Log.Logger.Information("TCP probe to {Host}:{Port} successful in {Elapsed} ms.", host, port, sw.ElapsedMilliseconds);
                outputArguments[0] = true;
                outputArguments[1] = $"TCP probe to {host}:{port} successful. Roundtrip time: {sw.ElapsedMilliseconds} ms.";
                return ServiceResult.Good;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                Log.Logger.Warning("TCP probe to {Host}:{Port} timed out after {Elapsed} ms.", host, port, sw.ElapsedMilliseconds);
                outputArguments[0] = false;
                outputArguments[1] = $"TCP probe to {host}:{port} timed out after {sw.ElapsedMilliseconds} ms.";
                return StatusCodes.BadNotFound;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log.Logger.Warning(ex, "TCP probe to {Host}:{Port} failed.", host, port);
                outputArguments[0] = false;
                outputArguments[1] = $"TCP probe to {host}:{port} failed: {ex.GetType().Name}.";
                return StatusCodes.BadNotFound;
            }
        }

        public async Task OnboardAssetFromWoTFileAsync(NodeState parent, string contents)
        {
            // parse WoT TD file contents
            ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents.Trim('\uFEFF')); // strip BOM, if present

            // td.Name is caller-controlled (parsed from uploaded JSON-LD) and
            // is later used to build a namespace URI, the _assets / _tags
            // dictionary keys, and the file path written by
            // OnCreateAssetForEndpoint. Reject path-traversal attempts up front
            // so we never touch the filesystem with an unsafe value.
            if (td == null || !IsSafeAssetName(td.Name))
            {
                Log.Logger.Warning("Rejecting WoT onboarding: invalid Thing Description name [{Name}]", td?.Name);
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Invalid Thing Description name.");
            }

            await LoadNamespacesFromThingDescriptionAsync(td).ConfigureAwait(false);

            // add a new namespace for the Thing Description itself
            string namespaceUri = "http://opcfoundation.org/UA/" + td.Name + "/";
            AddNamespace(namespaceUri);

            // let the protocol driver register any structure types (e.g. EIP UDTs)
            // into the address space before we create variables that reference them
            if (Program.Drivers.TryGetByUri(td.Base, out var driver))
            {
                driver.RegisterStructureTypes(td, this);
            }

            byte unitId = 1;
            bool requiresEventConnection = td.Events?.Count > 0;
            if (requiresEventConnection || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_ASSET_CONNECTION_TEST")))
            {
                unitId = await AssetConnectionTestAsync(td).ConfigureAwait(false);
            }

            AttachEventingAsset(td.Name, parent);

            // create nodes for each TD property
            if ((td.Properties != null) && (td.Properties.Count > 0))
            {
                foreach (KeyValuePair<string, Property> property in td.Properties)
                {
                    if ((property.Value.Forms != null) && (property.Value.Forms.Length > 0))
                    {
                        foreach (object form in property.Value.Forms)
                        {
                            try
                            {
                                AddNodeForWoTForm(parent, td, property, form, td.Name, unitId);
                            }
                            catch (Exception ex)
                            {
                                Log.Logger.Error(ex, "Failed to add node for WoT form: {PropertyKey}", property.Key);
                            }
                        }
                    }
                    else
                    {
                        AddConstantProperty(parent, td, property);
                    }
                }
            }

            AddActionsFromWoTFile(parent, td);

            AddNodesForWoTEvents(parent, td, unitId);

            _ = Task.Run(() => ReadAssetTagsAsync(td.Name));

            RaiseModelChangedEvent(parent.NodeId, ModelChangeStructureVerbMask.NodeAdded);

            Log.Logger.Information($"Successfully parsed WoT file for asset: {td.Name}");
        }

        /// <summary>
        /// Imports a Thing Description supplied by the diagnostics UI. It derives a
        /// safe asset name from the uploaded file name, creates the asset node and
        /// parses the Thing Description through the same onboarding path used for
        /// assets created over OPC UA and for files loaded from the local settings
        /// folder (<see cref="OnboardAssetFromWoTFileAsync"/>), then persists the
        /// file to the settings folder so it survives a restart and appears in the
        /// WoT file list. Returns the persisted settings file name.
        /// </summary>
        public async Task<string> ImportWoTFileAsync(string fileName, string contents)
        {
            if (string.IsNullOrWhiteSpace(contents))
            {
                throw new ArgumentException("The Thing Description file is empty.", nameof(contents));
            }

            // Derive a safe asset name from the uploaded file name, mirroring the
            // trusted local-file load path (LoadLocalWoTFilesAsync). The OPC UA
            // upload path inside OnboardAssetFromWoTFileAsync still validates the
            // Thing Description's own name via IsSafeAssetName.
            string assetName = SanitizeAssetName(Path.GetFileNameWithoutExtension(fileName));

            if (!CreateAssetNode(assetName, out NodeState assetNode))
            {
                throw new InvalidOperationException($"An asset named '{assetName}' already exists.");
            }

            await OnboardAssetFromWoTFileAsync(assetNode, contents).ConfigureAwait(false);

            // Persist to the settings folder so the import survives a restart and
            // appears in the WoT file list served by the diagnostics dashboard.
            string settingsFileName = assetName + ".jsonld";
            string settingsPath = Path.Combine(Directory.GetCurrentDirectory(), "settings", settingsFileName);
            File.WriteAllText(settingsPath, contents);

            Log.Logger.Information("Imported WoT file [{FileName}] as asset [{AssetName}]", fileName, assetName);

            return settingsFileName;
        }

        public void AddPredefinedNodePublic(NodeState node)
        {
            AddPredefinedNode(SystemContext, node);
        }

        private void AddNamespace(string namespaceUri)
        {
            if (string.IsNullOrWhiteSpace(namespaceUri))
            {
                throw new ArgumentNullException(nameof(namespaceUri));
            }

            NamespaceTable serverNSTable = Server.NamespaceUris;

            lock (Lock)
            {
                int index = serverNSTable.GetIndex(namespaceUri);
                if (index >= 0)
                {
                    // already added
                    return;
                }

                List<string> nodeManagerNSTable = NamespaceUris.ToList();

                serverNSTable.Append(namespaceUri);
                nodeManagerNSTable.Add(namespaceUri);

                // update the table used by this NodeManager
                SetNamespaces(nodeManagerNSTable.ToArray());

                // register the new URI with the MasterNodeManager
                Server.NodeManager.RegisterNamespaceManager(namespaceUri, this);
            }

            if (Server.ServerObject != null)
            {
                // SDK 2.0.0 removed ICoreNodeManager.DataLock, which used to guard these
                // writes to nodes owned by the core node manager, and exposes no public
                // replacement. Reads are unaffected: NamespaceArray is now served by
                // ServerInternalData.OnReadNamespaceArray, which returns a copy of the
                // live namespace table, so readers never observe a partial update here.
                // These writes still matter for subscribers, which are notified through
                // the change masks and the UrisVersion bump below. Serialise them on this
                // node manager's lock, taken separately (never nested inside the lock
                // above) to keep the original lock-ordering guarantee.
                lock (Lock)
                {
                    // update the NamespaceArray variable with the new list of namespaces
                    var nsArrayVar = Server.ServerObject.NamespaceArray;
                    if (nsArrayVar != null)
                    {
                        nsArrayVar.Value = Server.NamespaceUris.ToArray();
                        nsArrayVar.Timestamp = DateTime.UtcNow;
                        nsArrayVar.StatusCode = StatusCodes.Good;
                        nsArrayVar.ClearChangeMasks(SystemContext, false);
                    }

                    // update the URIs version variable to trigger client updates of the namespace array
                    var uvVar = Server.ServerObject.UrisVersion;
                    if (uvVar != null)
                    {
                        uvVar.Value = (uint)DateTime.UtcNow.Ticks;
                        uvVar.Timestamp = DateTime.UtcNow;
                        uvVar.StatusCode = StatusCodes.Good;
                        uvVar.ClearChangeMasks(SystemContext, false);
                    }

                    // ensure changes propagate
                    Server.ServerObject.ClearChangeMasks(SystemContext, true);
                }
            }
        }

        private void AddConstantProperty(NodeState parent, ThingDescription td, KeyValuePair<string, Property> property)
        {
            ushort assetNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/" + td.Name + "/");

            // check for constant
            if (property.Value.Const != null)
            {
                // create a variable with the constant value
                BaseDataVariableState variable;
                if (property.Value.Type == TypeEnum.String)
                {
                    variable = _nodeFactory.CreateVariable(parent, property.Key, DataTypes.String, assetNamespaceIndex, false, property.Value.Const.ToString());
                }
                else if (property.Value.Type == TypeEnum.Number)
                {
                    variable = _nodeFactory.CreateVariable(parent, property.Key, DataTypes.Double, assetNamespaceIndex, false, Convert.ToDouble(property.Value.Const));
                }
                else if (property.Value.Type == TypeEnum.Integer)
                {
                    variable = _nodeFactory.CreateVariable(parent, property.Key, DataTypes.Int32, assetNamespaceIndex, false, Convert.ToInt32(property.Value.Const));
                }
                else if (property.Value.Type == TypeEnum.Boolean)
                {
                    variable = _nodeFactory.CreateVariable(parent, property.Key, DataTypes.Boolean, assetNamespaceIndex, false, Convert.ToBoolean(property.Value.Const));
                }
                else
                {
                    // default to string
                    variable = _nodeFactory.CreateVariable(parent, property.Key, DataTypes.String, assetNamespaceIndex, false, property.Value.Const.ToString());
                }

                AddPredefinedNode(SystemContext, variable);
                _uaVariables.TryAdd($"{td.Name}:{property.Key}", variable);
            }
        }

        private void AddActionsFromWoTFile(NodeState parent, ThingDescription td)
        {
            ushort assetNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/" + td.Name + "/");

            // create nodes for each TD action
            if ((td.Actions != null) && (td.Actions.Count > 0))
            {
                foreach (KeyValuePair<string, TDAction> action in td.Actions)
                {
                    MethodState method = _nodeFactory.CreateMethod(parent, action.Key, assetNamespaceIndex);
                    method.OnCallMethod2Async = OnTDActionCalledAsync;

                    if ((action.Value.Input != null) && (action.Value.Input.Properties != null) && (action.Value.Input.Properties.Count > 0))
                    {
                        // create expanded node IDs, names and descriptions for each input argument type
                        ExpandedNodeId[] inputArgumentTypes = new ExpandedNodeId[action.Value.Input.Properties.Count];
                        string[] inputArguentNames = new string[action.Value.Input.Properties.Count];

                        for (int i = 0; i < action.Value.Input.Properties.Count; i++)
                        {
                            inputArguentNames[i] = action.Value.Input.Properties.ElementAt(i).Key;

                            if (action.Value.Input.Properties.ElementAt(i).Value.Type == TypeEnum.String)
                            {
                                inputArgumentTypes[i] = new ExpandedNodeId(DataTypes.String);
                            }
                            else if (action.Value.Input.Properties.ElementAt(i).Value.Type == TypeEnum.Number)
                            {
                                inputArgumentTypes[i] = new ExpandedNodeId(DataTypes.Double);
                            }
                            else if (action.Value.Input.Properties.ElementAt(i).Value.Type == TypeEnum.Boolean)
                            {
                                inputArgumentTypes[i] = new ExpandedNodeId(DataTypes.Boolean);
                            }
                            else if (action.Value.Input.Properties.ElementAt(i).Value.Type == TypeEnum.Integer)
                            {
                                inputArgumentTypes[i] = new ExpandedNodeId(DataTypes.Int32);
                            }
                            else if (action.Value.Input.Properties.ElementAt(i).Value.Type == TypeEnum.Object)
                            {
                                inputArgumentTypes[i] = new ExpandedNodeId(DataTypes.ByteString);
                            }
                            else
                            {
                                inputArgumentTypes[i] = new ExpandedNodeId(DataTypes.String); // default to string
                            }
                        }

                        method.InputArguments = _nodeFactory.CreateMethodArguments(method, inputArguentNames, inputArguentNames, inputArgumentTypes, true);
                    }

                    if ((action.Value.Output != null) && (action.Value.Output.Properties != null) && (action.Value.Output.Properties.Count > 0))
                    {
                        // create expanded node IDs, names and descriptions for each output argument type
                        ExpandedNodeId[] outputArgumentTypes = new ExpandedNodeId[action.Value.Output.Properties.Count];
                        string[] outputArgumentNames = new string[action.Value.Output.Properties.Count];

                        for (int i = 0; i < action.Value.Output.Properties.Count; i++)
                        {
                            outputArgumentNames[i] = action.Value.Output.Properties.ElementAt(i).Key;

                            if (action.Value.Output.Properties.ElementAt(i).Value.Type == TypeEnum.String)
                            {
                                outputArgumentTypes[i] = new ExpandedNodeId(DataTypes.String);
                            }
                            else if (action.Value.Output.Properties.ElementAt(i).Value.Type == TypeEnum.Number)
                            {
                                outputArgumentTypes[i] = new ExpandedNodeId(DataTypes.Double);
                            }
                            else if (action.Value.Output.Properties.ElementAt(i).Value.Type == TypeEnum.Boolean)
                            {
                                outputArgumentTypes[i] = new ExpandedNodeId(DataTypes.Boolean);
                            }
                            else if (action.Value.Output.Properties.ElementAt(i).Value.Type == TypeEnum.Integer)
                            {
                                outputArgumentTypes[i] = new ExpandedNodeId(DataTypes.Int32);
                            }
                            else if (action.Value.Output.Properties.ElementAt(i).Value.Type == TypeEnum.Object)
                            {
                                outputArgumentTypes[i] = new ExpandedNodeId(DataTypes.ByteString);
                            }
                            else
                            {
                                outputArgumentTypes[i] = new ExpandedNodeId(DataTypes.String); // default to string
                            }
                        }

                        method.OutputArguments = _nodeFactory.CreateMethodArguments(method, outputArgumentNames, outputArgumentNames, outputArgumentTypes, false, true);
                    }

                    // check if there are any specified forms for the action
                    if ((action.Value.Forms != null) && (action.Value.Forms.Length > 0))
                    {
                        List<GenericForm> methodForms = new();
                        foreach (object form in action.Value.Forms)
                        {
                            GenericForm genericForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());
                            methodForms.Add(genericForm);
                        }

                        method.Handle = methodForms;
                    }

                    parent.AddChild(method);
                    AddPredefinedNode(SystemContext, method);
                }
            }
        }

        /// <summary>
        /// Creates OPC UA variables for a Thing Description's event affordances.
        /// <para>
        /// Push-oriented bindings describe their values as events rather than as
        /// readable properties. The W3C WoT LoRaWAN binding is explicit about
        /// this: a LoRaWAN device transmits on its own schedule, so "a consumer
        /// does not poll a sensor on demand; instead it subscribes to uplinks",
        /// and the binding therefore "models uplink values as WoT events, not
        /// readable properties".
        /// </para>
        /// <para>
        /// Each such event still carries exactly one decoded value, which maps
        /// cleanly onto a single OPC UA variable. Rather than duplicating the
        /// node-creation logic, every event affordance is projected onto the
        /// equivalent <see cref="Property"/> and pushed through the same
        /// <see cref="AddNodeForWoTForm"/> path the property affordances use, so
        /// type mapping, complex types and tag creation behave identically.
        /// </para>
        /// </summary>
        private void AddNodesForWoTEvents(NodeState assetFolder, ThingDescription td, byte unitId)
        {
            if ((td.Events == null) || (td.Events.Count == 0))
            {
                return;
            }

            // An eventing asset (OPC A&E) delivers its events as a push stream
            // through the Alarms folder created by AttachEventingAsset, and its
            // driver has no pollable tag to offer. Projecting those events onto
            // variables as well would both duplicate them in the address space
            // and fault on every form, so only non-eventing assets — such as
            // LoRaWAN, whose binding models sensor readings as events rather
            // than properties — take the variable path.
            if (_assets.TryGetValue(td.Name, out IAsset asset) && asset is IEventingAsset)
            {
                return;
            }

            foreach (KeyValuePair<string, TDEvent> tdEvent in td.Events)
            {
                if ((tdEvent.Value?.Forms == null) || (tdEvent.Value.Forms.Length == 0))
                {
                    continue;
                }

                Property property = ProjectEventOntoProperty(tdEvent.Value);

                foreach (object form in tdEvent.Value.Forms)
                {
                    try
                    {
                        AddNodeForWoTForm(assetFolder, td, new KeyValuePair<string, Property>(tdEvent.Key, property), form, td.Name, unitId);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error(ex, "Failed to add node for WoT event: {EventKey}", tdEvent.Key);
                    }
                }
            }
        }

        /// <summary>
        /// Projects an event affordance onto the property shape the node-creation
        /// path expects. The event's <c>data</c> schema describes what the value
        /// means, so its type and OPC UA mapping hints are lifted from there.
        /// </summary>
        private static Property ProjectEventOntoProperty(TDEvent tdEvent)
        {
            Property property = new()
            {
                // An event value is pushed by the device and cannot be written
                // back through this binding.
                ReadOnly = true,
                Observable = true,
                Forms = tdEvent.Forms
            };

            if (tdEvent.Data is null)
            {
                return property;
            }

            // The data schema is a plain JSON schema object; read the same
            // mapping hints a property would carry.
            try
            {
                JObject data = JObject.FromObject(tdEvent.Data);

                if (data["type"] is JToken type && Enum.TryParse(type.ToString(), true, out TypeEnum parsed))
                {
                    property.Type = parsed;
                }

                property.OpcUaNodeId = data["uav:mapToNodeId"]?.ToString();
                property.OpcUaType = data["uav:mapToType"]?.ToString();
                property.OpcUaFieldPath = data["uav:mapByFieldPath"]?.ToString();
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to read the data schema of a WoT event; falling back to defaults.");
            }

            return property;
        }

        /// <summary>
        /// Creates and registers the variable for a WoT property the first time
        /// one of its forms is processed. A property with several forms (one per
        /// operation) maps onto a single OPC UA variable, so later forms reuse it.
        /// </summary>
        private void AddVariableOnce(string variableId, Func<BaseDataVariableState> create)
        {
            if (_uaVariables.ContainsKey(variableId))
            {
                return;
            }

            BaseDataVariableState variable = create();
            _uaVariables[variableId] = variable;
            AddPredefinedNode(SystemContext, variable);
        }

        private void AddNodeForWoTForm(NodeState assetFolder, ThingDescription td, KeyValuePair<string, Property> property, object form, string assetId, byte unitId)
        {
            string variableId;
            string variableName;
            if (string.IsNullOrEmpty(property.Value.OpcUaNodeId))
            {
                variableId = $"{assetId}:{property.Key}";
                variableName = property.Key;
            }
            else
            {
                variableId = $"{assetId}:{property.Value.OpcUaNodeId}";
                variableName = property.Value.OpcUaNodeId.Substring(property.Value.OpcUaNodeId.IndexOf("=") + 1);
            }

            ushort assetNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/" + td.Name + "/");

            string fieldPath = string.Empty;

            // create an OPC UA variable optionally with a specified type.
            if (!string.IsNullOrEmpty(property.Value.OpcUaType))
            {
                string[] opcuaTypeParts = property.Value.OpcUaType.Split(['=', ';']);
                if ((opcuaTypeParts.Length > 3) && (opcuaTypeParts[0] == "nsu") && (opcuaTypeParts[2] == "i" || opcuaTypeParts[2] == "s"))
                {
                    string namespaceURI = opcuaTypeParts[1];
                    if (NamespaceUris.Contains(namespaceURI))
                    {
                        DataTypeState dataType = Find(ExpandedNodeId.ToNodeId(ParseExpandedNodeId(property.Value.OpcUaType), Server.NamespaceUris)) as DataTypeState;

                        // check if this variable is part of a complex type and we need to load the complex type first and then assign a part of it to the new variable.
                        if (!string.IsNullOrEmpty(property.Value.OpcUaFieldPath))
                        {
                            // Check if the type is an ObjectType (not a DataType/StructureDefinition)
                            BaseObjectTypeState objectType = Find(ExpandedNodeId.ToNodeId(ParseExpandedNodeId(property.Value.OpcUaType), Server.NamespaceUris)) as BaseObjectTypeState;
                            if (objectType != null)
                            {
                                // Object type path: instantiate the object type once, then map WoT properties to child variables

                                // Check if we already created this object instance for a previous field
                                BaseObjectState objectInstance;
                                NodeState childVariable = null;
                                // This node manager creates the instance itself (AddPredefinedNode
                                // below), so look it up locally. FindNodeInAddressSpace is obsolete
                                // in SDK 2.0.0 and points at the async master node manager, which
                                // would force sync-over-async here for a node we own.
                                var existingNode = Find(new NodeId(variableId, assetNamespaceIndex));
                                if (existingNode == null)
                                {
                                    // Give the object instance the same asset-namespaced NodeId
                                    // (variableId) the lookup above expects, so a second field
                                    // mapped onto the same object reuses this instance instead of
                                    // creating a duplicate in the EdgeTranslator namespace.
                                    objectInstance = _nodeFactory.CreateObject(assetFolder, variableName, ParseExpandedNodeId(property.Value.OpcUaType), assetNamespaceIndex, variableId);
                                    AddPredefinedNode(SystemContext, objectInstance);
                                }
                                else
                                {
                                    objectInstance = existingNode as BaseObjectState;
                                }

                                // Instantiate a child variable underneath the object instance
                                childVariable = InstantiateObjectTypeChild(objectInstance, objectType, assetNamespaceIndex, variableName, property.Value.OpcUaFieldPath, !property.Value.ReadOnly);
                                if (childVariable != null)
                                {
                                    if (!_uaVariables.ContainsKey(variableId))
                                    {
                                        if (childVariable is BaseDataVariableState)
                                        {
                                            _uaVariables[variableId] = childVariable as BaseDataVariableState;
                                        }
                                        else
                                        {
                                            _uaProperties[variableId] = childVariable as PropertyState;
                                        }
                                    }

                                    fieldPath = property.Value.OpcUaFieldPath;
                                }
                            }
                            else if (TryGetStructureDefinition(dataType, out StructureDefinition structureDefinition))
                            {
                                // Data Structure type path: instantiate a new extension object once, then map WoT properties to fields
                                NodeId defaultBinaryEncodingId = GetDefaultBinaryEncodingId(dataType);
                                if (defaultBinaryEncodingId.IsNull)
                                {
                                    throw new InvalidOperationException($"No 'Default Binary' encoding found for DataType {dataType.BrowseName} ({dataType.NodeId}).");
                                }

                                // ExtensionObject is an immutable struct in SDK 2.0.0 and
                                // ServiceMessageContext takes its Factory via the constructor, so
                                // encode the body first and build the extension object from it.
                                using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry, Server.Factory)
                                {
                                    NamespaceUris = Server.NamespaceUris
                                });

                                foreach (StructureField field in structureDefinition.Fields)
                                {
                                    EncodeField(encoder, field, null);

                                    if (field.Name == property.Value.OpcUaFieldPath)
                                    {
                                        // capture the field path to make sure we can identify the tag during data updates
                                        fieldPath = field.Name;
                                    }
                                }

                                ExtensionObject complexTypeInstance = new(defaultBinaryEncodingId, new ByteString(encoder.CloseAndReturnBuffer()));

                                // check if we are creating this variable for the first time, or if it's already been created as part of a previous field mapping for this asset
                                if (!_uaVariables.ContainsKey(variableId))
                                {
                                    BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, dataType.NodeId, assetNamespaceIndex, !property.Value.ReadOnly, complexTypeInstance);
                                    _uaVariables[variableId] = variable;
                                    AddPredefinedNode(SystemContext, variable);
                                }
                            }
                            else
                            {
                                // OPC UA type info not found, default to float
                                AddVariableOnce(variableId, () => _nodeFactory.CreateVariable(assetFolder, variableName, DataTypes.Float, assetNamespaceIndex, !property.Value.ReadOnly));
                            }
                        }
                        else
                        {
                            // No field path — check if it's a structure type that needs an initialized ExtensionObject, or a simple built-in type.
                            if (TryGetStructureDefinition(dataType, out StructureDefinition structDef))
                            {
                                NodeId defaultBinaryEncodingId = GetDefaultBinaryEncodingId(dataType);
                                if (defaultBinaryEncodingId.IsNull)
                                {
                                    throw new InvalidOperationException($"No 'Default Binary' encoding found for DataType {dataType.BrowseName} ({dataType.NodeId}).");
                                }

                                using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry, Server.Factory) {
                                    NamespaceUris = Server.NamespaceUris
                                });

                                foreach (StructureField field in structDef.Fields)
                                {
                                    EncodeField(encoder, field, null);
                                }

                                ExtensionObject complexTypeInstance = new(defaultBinaryEncodingId, new ByteString(encoder.CloseAndReturnBuffer()));

                                AddVariableOnce(variableId, () => _nodeFactory.CreateVariable(assetFolder, variableName, dataType.NodeId, assetNamespaceIndex, !property.Value.ReadOnly, complexTypeInstance));
                            }
                            else
                            {
                                // it's an OPC UA built-in type
                                AddVariableOnce(variableId, () => _nodeFactory.CreateVariable(assetFolder, variableName, dataType.NodeId, assetNamespaceIndex, !property.Value.ReadOnly));
                            }
                        }
                    }
                    else
                    {
                        // no namespace info, default to float
                        AddVariableOnce(variableId, () => _nodeFactory.CreateVariable(assetFolder, variableName, DataTypes.Float, assetNamespaceIndex, !property.Value.ReadOnly));
                    }
                }
                else
                {
                    // can't parse type info, default to float
                    AddVariableOnce(variableId, () => _nodeFactory.CreateVariable(assetFolder, variableName, DataTypes.Float, assetNamespaceIndex, !property.Value.ReadOnly));
                }
            }
            else
            {
                // no type info, default to float
                AddVariableOnce(variableId, () => _nodeFactory.CreateVariable(assetFolder, variableName, DataTypes.Float, assetNamespaceIndex, !property.Value.ReadOnly));
            }

            // check if we need to create a new asset first
            if (!_tags.ContainsKey(assetId))
            {
                _tags.GetOrAdd(assetId, _ => new List<AssetTag>());
            }

            AddTag(td, form, assetId, unitId, variableId, fieldPath);
        }

        private NodeId GetDefaultBinaryEncodingId(DataTypeState dataType)
        {
            var refs = new List<IReference>();
            dataType.GetReferences(SystemContext, refs, ReferenceTypeIds.HasEncoding, isInverse: false);

            foreach (var r in refs)
            {
                var targetId = ExpandedNodeId.ToNodeId(r.TargetId, SystemContext.NamespaceUris);
                var targetNode = Find(targetId);

                if (targetNode?.BrowseName.Name == "Default Binary")
                {
                    return targetNode.NodeId;
                }
            }

            return NodeId.Null;
        }

        // DataTypeDefinition is an ExtensionObject whose Body property became obsolete in
        // SDK 2.0.0 in favour of the type-safe TryGetValue<T> accessor. Centralised here
        // because the structure definition is needed on several node-creation paths.
        private bool TryGetStructureDefinition(DataTypeState dataType, out StructureDefinition structureDefinition)
        {
            structureDefinition = null;

            if (dataType == null || dataType.DataTypeDefinition.IsNull)
            {
                return false;
            }

            if (!dataType.DataTypeDefinition.TryGetValue(out StructureDefinition definition, Server.MessageContext))
            {
                return false;
            }

            structureDefinition = definition;

            return structureDefinition != null && structureDefinition.Fields.Count > 0;
        }

        private void AddTag(ThingDescription td, object form, string assetId, byte unitId, string variableId, string fieldPath)
        {
            if (!Program.Drivers.TryGetByUri(td.Base, out var drv))
            {
                throw new Exception($"No driver installed for base URI: {td.Base}");
            }

            NodeId mappedNodeId;
            if (_uaVariables.TryGetValue(variableId, out BaseDataVariableState mappedVar))
            {
                mappedNodeId = mappedVar.NodeId;
            }
            else
            {
                mappedNodeId = _uaProperties[variableId].NodeId;
            }

            string mappedExpandedNodeId = NodeId.ToExpandedNodeId(mappedNodeId, Server.NamespaceUris).ToString();

            AssetTag tag = drv.CreateTag(td, form, assetId, unitId, variableId, mappedExpandedNodeId, fieldPath);
            _tags[assetId].Add(tag);

            // Maintain the NodeId -> (asset, tag) reverse index so OnReadValue /
            // OnWriteValue can resolve tags in O(1).
            _tagIndex[mappedNodeId] = (assetId, tag);
        }

        private async Task<byte> AssetConnectionTestAsync(ThingDescription td, CancellationToken cancellationToken = default)
        {
            if (!Program.Drivers.TryGetByUri(td.Base, out var drv))
            {
                throw new Exception($"No driver installed for base URI: {td.Base}");
            }

            AssetConnection connection = await drv.CreateAndConnectAssetAsync(td, cancellationToken).ConfigureAwait(false);

            _assets[td.Name] = connection.Asset;

            return connection.UnitId;
        }

        private void AttachEventingAsset(string assetId, NodeState assetNode)
        {
            if (!_assets.TryGetValue(assetId, out IAsset asset) || asset is not IEventingAsset eventingAsset)
            {
                return;
            }

            string namespaceUri = "http://opcfoundation.org/UA/" + assetId + "/";
            ushort namespaceIndex = (ushort)Server.NamespaceUris.GetIndexOrAppend(namespaceUri);
            BaseObjectState source = _nodeFactory.CreateObject(assetNode as BaseObjectState, "Alarms", ObjectTypeIds.BaseObjectType, namespaceIndex, assetId + "_Alarms");
            source.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddPredefinedNode(SystemContext, source);
            _eventSources[assetId] = source;

            AlarmProjectionQueue projectionQueue = CreateAlarmProjectionQueue(assetId);

            EventHandler<AlarmEvent> handler = (_, alarm) =>
            {
                if (alarm == null)
                {
                    return;
                }

                if (!projectionQueue.Channel.Writer.TryWrite(alarm))
                {
                    int dropped = Interlocked.Increment(ref projectionQueue.DroppedEvents);
                    if (dropped == 1 || dropped % 100 == 0)
                    {
                        Log.Logger.Warning(
                            "Dropped OPC A&E alarm for asset {AssetId} because projection queue is full. Total dropped={DroppedCount}.",
                            assetId,
                            dropped);
                    }
                }
            };

            try
            {
                eventingAsset.AlarmReceived += handler;
                _eventSubscriptions[assetId] = (eventingAsset, handler);
                eventingAsset.StartEventSubscription();
            }
            catch
            {
                eventingAsset.AlarmReceived -= handler;
                _eventSubscriptions.TryRemove(assetId, out _);
                StopAlarmProjectionQueue(assetId);
                throw;
            }
        }

        private AlarmProjectionQueue CreateAlarmProjectionQueue(string assetId)
        {
            var queue = Channel.CreateBounded<AlarmEvent>(new BoundedChannelOptions(512)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });

            var cancellation = new CancellationTokenSource();
            Task worker = Task.Run(async () =>
            {
                try
                {
                    await foreach (AlarmEvent alarm in queue.Reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
                    {
                        try
                        {
                            ReportReadOnlyAlarm(assetId, alarm);
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Warning(ex, "Failed to project alarm event for asset {AssetId}", assetId);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // expected during delete/shutdown
                }
            }, cancellation.Token);

            var projectionQueue = new AlarmProjectionQueue
            {
                Channel = queue,
                Cancellation = cancellation,
                Worker = worker
            };

            _alarmProjectionQueues[assetId] = projectionQueue;
            return projectionQueue;
        }

        private void StopAlarmProjectionQueue(string assetId)
        {
            if (!_alarmProjectionQueues.TryRemove(assetId, out AlarmProjectionQueue queue))
            {
                return;
            }

            try
            {
                queue.Channel.Writer.TryComplete();
                queue.Cancellation.Cancel();
                queue.Worker.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to stop alarm projection queue for asset {AssetId}.", assetId);
            }
            finally
            {
                queue.Cancellation.Dispose();
            }
        }

        private void ReportReadOnlyAlarm(string assetId, AlarmEvent alarm)
        {
            if (!_eventSources.TryGetValue(assetId, out BaseObjectState source))
            {
                return;
            }

            lock (Lock)
            {
                string conditionKey = string.IsNullOrWhiteSpace(alarm.ConditionKey)
                    ? assetId + ":" + alarm.Source + ":" + alarm.ConditionName
                    : assetId + ":" + alarm.ConditionKey;

                AlarmConditionState condition = _alarmConditions.GetOrAdd(conditionKey, _ =>
                {
                    var created = new AlarmConditionState(source);
                    created.Initialize(SystemContext, null, EventSeverity.Medium, new Ua.LocalizedText("Alarm condition"));
                    return created;
                });

                condition.SetChildValue(SystemContext, BrowseNames.SourceNode, source.NodeId, false);
                condition.SetChildValue(SystemContext, BrowseNames.SourceName, alarm.Source ?? assetId, false);
                condition.SetChildValue(SystemContext, BrowseNames.ConditionName, alarm.ConditionName ?? alarm.Source ?? assetId, false);
                condition.SetChildValue(SystemContext, BrowseNames.Severity, (ushort)Math.Clamp(alarm.Severity, 0, ushort.MaxValue), false);
                condition.SetChildValue(SystemContext, BrowseNames.Message, new Ua.LocalizedText(alarm.Message ?? string.Empty), false);
                condition.SetChildValue(SystemContext, BrowseNames.Time, (DateTimeUtc)(alarm.Time == default ? DateTime.UtcNow : alarm.Time), false);
                condition.SetChildValue(SystemContext, BrowseNames.ReceiveTime, (DateTimeUtc)DateTime.UtcNow, false);
                condition.SetChildValue(SystemContext, BrowseNames.Retain, alarm.Active || !alarm.Acknowledged, false);
                condition.SetChildValue(SystemContext, BrowseNames.EnabledState, new Ua.LocalizedText(alarm.Enabled ? "Enabled" : "Disabled"), false);
                condition.SetChildValue(SystemContext, BrowseNames.ActiveState, new Ua.LocalizedText(alarm.Active ? "Active" : "Inactive"), false);
                condition.SetChildValue(SystemContext, BrowseNames.AckedState, new Ua.LocalizedText(alarm.Acknowledged ? "Acknowledged" : "Unacknowledged"), false);

                condition.EnabledState?.SetChildValue(SystemContext, BrowseNames.Id, alarm.Enabled, false);
                condition.ActiveState?.SetChildValue(SystemContext, BrowseNames.Id, alarm.Active, false);
                condition.AckedState?.SetChildValue(SystemContext, BrowseNames.Id, alarm.Acknowledged, false);

                Server.ReportEvent(condition);
            }
        }

        private ExpandedNodeId ParseExpandedNodeId(string nodeString)
        {
            if (!string.IsNullOrEmpty(nodeString))
            {
                string[] parentNodeDetails = nodeString.Split('=', ';');
                if (parentNodeDetails.Length > 3 && parentNodeDetails[0] == "nsu" && (parentNodeDetails[2] == "i" || parentNodeDetails[2] == "s"))
                {
                    string namespaceUri = parentNodeDetails[1];

                    if (!NamespaceUris.Contains(namespaceUri))
                    {
                        return ExpandedNodeId.Null;
                    }

                    switch (parentNodeDetails[2])
                    {
                        case "i":
                            return new ExpandedNodeId(uint.Parse(parentNodeDetails[3]),
                                (ushort)Server.NamespaceUris.GetIndex(namespaceUri));
                        case "s":
                            return new ExpandedNodeId(parentNodeDetails[3],
                                (ushort)Server.NamespaceUris.GetIndex(namespaceUri));
                        default:
                            return ExpandedNodeId.Null;
                    }
                }
            }

            return ExpandedNodeId.Null;
        }

        // Returns whether the server is in provisioning mode (no issuer certificates
        // present yet). The result is cached for a short TTL so the OPC UA read/write
        // hot paths do not enumerate the filesystem on every single service call.
        private bool IsInProvisioningMode()
        {
            int now = Environment.TickCount;
            if (!_provisioningModeChecked || (uint)(now - _provisioningModeCheckedAtTick) >= _provisioningModeTtlMs)
            {
                string certsPath = Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "certs");
                _provisioningModeCached = !Directory.Exists(certsPath) || !Directory.EnumerateFiles(certsPath).Any();
                _provisioningModeCheckedAtTick = now;
                _provisioningModeChecked = true;
            }

            return _provisioningModeCached;
        }

        public ServiceResult OnReadValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref Variant value, ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            bool provisioningMode = IsInProvisioningMode();
            if (provisioningMode && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE")))
            {
                return new ServiceResult(StatusCodes.BadNotReadable, new Ua.LocalizedText("Access to UA Edge Translator is limited while in provisioning mode!"));
            }

            PropertyState property = node as PropertyState;
            if (property != null)
            {
                if (node.DisplayName.Text == "SupportedWoTBindings")
                {
                    value = Variant.From(Program.Drivers.AllDrivers
                        .Select(d => d.WoTBindingUri)
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .ToArray());

                    timestamp = DateTime.UtcNow;
                    statusCode = StatusCodes.Good;

                    return ServiceResult.Good;
                }

                if (node.DisplayName.Text == "SupportedOPCUAInfoModels")
                {
                    value = Variant.From(Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "nodesets")));

                    timestamp = DateTime.UtcNow;
                    statusCode = StatusCodes.Good;

                    return ServiceResult.Good;
                }

                if (node.DisplayName.Text == "License")
                {
                    value = _uaProperties[node.DisplayName.Text].Value;

                    timestamp = _uaProperties[node.DisplayName.Text].Timestamp;
                    statusCode = StatusCodes.Good;

                    return ServiceResult.Good;
                }
            }

            BaseDataVariableState variable = node as BaseDataVariableState;
            if (variable != null)
            {
                if (node.DisplayName.Text == "MemoryWorkingSet(MB)")
                {
                    using Process currentProcess = Process.GetCurrentProcess();
                    value = Variant.From(currentProcess.WorkingSet64 / (1024 * 1024));
                    timestamp = DateTime.UtcNow;
                    statusCode = StatusCodes.Good;

                    return ServiceResult.Good;
                }

                // Snapshot before enumeration: ConcurrentDictionary protects the
                // outer enumeration but the inner List<AssetTag> is still mutated
                // by AddTag(), so resolving the tag via the reverse NodeId index
                // is both O(1) AND avoids holding any reference to the list.
                if (_tagIndex.TryGetValue(variable.NodeId, out var indexed))
                {
                    string assetId = indexed.AssetId;
                    AssetTag tag = indexed.Tag;

                    try
                    {
                        if (_uaVariables.TryGetValue(tag.Name, out BaseDataVariableState cached))
                        {
                            value = cached.Value;
                            timestamp = cached.Timestamp;
                        }

                        statusCode = (_assets.TryGetValue(assetId, out IAsset asset) && asset.IsConnected)
                            ? StatusCodes.Good
                            : StatusCodes.BadDataUnavailable;

                        return ServiceResult.Good;
                    }
                    catch (Exception ex)
                    {
                        // Log full exception server-side, return only a generic
                        // status to the OPC UA client to avoid leaking internal
                        // exception text or stack traces over the wire.
                        Log.Logger.Error(ex, "Failed to read value for tag: {TagName}", tag.Name);
                        return new ServiceResult(StatusCodes.BadDataUnavailable);
                    }
                }
            }

            return ServiceResult.Good;
        }

        public ServiceResult OnWriteValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref Variant value, ref StatusCode statusCode, ref DateTimeUtc timestamp)
        {
            bool provisioningMode = IsInProvisioningMode();
            if (provisioningMode && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE")))
            {
                return new ServiceResult(StatusCodes.BadNotWritable, new Ua.LocalizedText("Access to UA Edge Translator is limited while in provisioning mode!"));
            }

            PropertyState property = node as PropertyState;
            if (property != null)
            {
                if (node.DisplayName.Text == "SupportedWoTBindings")
                {
                    statusCode = StatusCodes.Good;

                    return ServiceResult.Good;
                }

                if (node.DisplayName.Text == "SupportedOPCUAInfoModels")
                {
                    statusCode = StatusCodes.Good;

                    return ServiceResult.Good;
                }

                if (node.DisplayName.Text == "License")
                {
                    // validate license key provided. Read the value once with the same
                    // semantics as the pre-2.0.0 `object?.ToString()`: Variant.ToString()
                    // renders a null value as the non-empty "<null>", which would slip past
                    // this guard. The same string feeds the comparison below.
                    string providedLicenseKey = value.AsBoxedObject()?.ToString();
                    if (string.IsNullOrEmpty(providedLicenseKey))
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument, new Ua.LocalizedText("License key cannot be empty!"));
                    }

                    // The expected license key must be supplied out-of-band via the LICENSE_KEY
                    // environment variable. If it is not configured the server refuses the write
                    // rather than falling back to a hard-coded value.
                    string expectedLicenseKey = Environment.GetEnvironmentVariable("LICENSE_KEY");
                    if (string.IsNullOrEmpty(expectedLicenseKey))
                    {
                        Log.Logger.Warning("License write rejected: LICENSE_KEY environment variable is not configured on the server.");
                        return new ServiceResult(StatusCodes.BadNotSupported, new Ua.LocalizedText("License validation is not configured on this server."));
                    }

                    byte[] providedBytes = Encoding.UTF8.GetBytes(providedLicenseKey);
                    byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedLicenseKey);
                    if (providedBytes.Length != expectedBytes.Length
                     || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument, new Ua.LocalizedText("Invalid license key!"));
                    }

                    _uaProperties["License"].Value = value;
                    _uaProperties["License"].Timestamp = DateTime.UtcNow;
                    _uaProperties["License"].ClearChangeMasks(SystemContext, true);
                    statusCode = StatusCodes.Good;

                    // TODO: In a commercial product, you would validate the license key here using some algorithm
                    // and only when the key is valid switch out of provisioning mode

                    return ServiceResult.Good;
                }
            }

            BaseDataVariableState variable = node as BaseDataVariableState;
            if (variable != null)
            {
                if (node.DisplayName.Text == "MemoryWorkingSet")
                {
                    statusCode = StatusCodes.Good;

                    return ServiceResult.Good;
                }

                // Resolve the tag in O(1) via the reverse NodeId index instead
                // of scanning every (asset, tag) pair on every write.
                if (_tagIndex.TryGetValue(variable.NodeId, out var indexed))
                {
                    AssetTag tag = indexed.Tag;

                    // The driver write was already awaited in WriteAsync (no lock held,
                    // no sync-over-async); apply its outcome here. The server always
                    // dispatches Write through WriteAsync (IWriteAsyncNodeManager), so a
                    // missing outcome means the SDK pre-checks rejected the write and no
                    // southbound I/O was attempted.
                    if (s_pendingDriverWrites.Value is not { } pending
                        || !pending.TryGetValue(variable.NodeId, out DriverWriteOutcome outcome))
                    {
                        return new ServiceResult(StatusCodes.BadNotWritable);
                    }

                    if (outcome.Error != null)
                    {
                        return outcome.Error;
                    }

                    if (_uaVariables.TryGetValue(tag.Name, out BaseDataVariableState cached))
                    {
                        cached.Value = value;
                        cached.Timestamp = DateTime.UtcNow;
                        cached.ClearChangeMasks(SystemContext, true);
                    }

                    statusCode = outcome.Connected ? StatusCodes.Good : StatusCodes.BadDataUnavailable;
                    return ServiceResult.Good;
                }
            }

            return ServiceResult.Good;
        }

        private async ValueTask<ServiceResult> OnTDActionCalledAsync(ISystemContext context, MethodState method, NodeId objectId, ArrayOf<Variant> inputArguments, List<Variant> outputArguments, CancellationToken cancellationToken)
        {
            try
            {
                string assetId = method.Parent.BrowseName.Name;

                // The SDK now hands method arguments over as Variant, but the driver-facing
                // IAsset contract is still object-based so protocol drivers do not have to
                // take a dependency on the OPC UA value model. Marshal across the boundary.
                List<object> driverInputs = new(inputArguments.Count);
                foreach (Variant inputArgument in inputArguments)
                {
                    driverInputs.Add(VariantConversion.ToClrValue(inputArgument));
                }

                AssetActionResult actionResult = await _assets[assetId].ExecuteActionAsync(method, driverInputs, cancellationToken).ConfigureAwait(false);

                // Copy the driver's outputs into the buffer the SDK handed us.
                // The previous 'ref IList<object> outputArgs' contract allowed a
                // driver to *replace* the list, which silently dropped its outputs
                // because the SDK reads back the instance it passed in.
                if (actionResult?.Outputs != null && outputArguments != null)
                {
                    for (int i = 0; i < actionResult.Outputs.Count; i++)
                    {
                        object output = actionResult.Outputs[i];

                        // Driver outputs are runtime-typed: see NodeFactory.CreateVariable.
#pragma warning disable CS0618 // Type or member is obsolete
                        Variant outputVariant = new(output, TypeInfo.Construct(output));
#pragma warning restore CS0618

                        if (i < outputArguments.Count)
                        {
                            outputArguments[i] = outputVariant;
                        }
                        else
                        {
                            outputArguments.Add(outputVariant);
                        }
                    }
                }

                string result = actionResult?.Status;
                if (result == null)
                {
                    return new ServiceResult(StatusCodes.Uncertain, new Ua.LocalizedText("no result"));
                }
                else if (actionResult.IsSuccess)
                {
                    return new ServiceResult(StatusCodes.Good, new Ua.LocalizedText("success"));
                }
                else
                {
                    return new ServiceResult(StatusCodes.Bad, new Ua.LocalizedText(result));
                }
            }
            catch (Exception ex)
            {
                // Log full exception server-side, return only a generic status to
                // the OPC UA client to avoid leaking internal exception text.
                Log.Logger.Error(ex, "Failed to execute action on asset: {AssetId}", method.Parent.BrowseName.Name);
                return new ServiceResult(StatusCodes.BadInternalError);
            }
        }

        // Per-asset reconnect state. We back off exponentially on consecutive
        // failures (1 s -> 2 -> 4 -> 8 -> … -> capped at _reconnectMaxBackoffMs)
        // and skip the asset's poll cycles until the next attempt is due.
        private async Task ReadAssetTagsAsync(object assetNameObject)
        {
            string assetId = (string)assetNameObject;
            CancellationToken shutdownToken = _shutdownCts.Token;

            // Stopwatch is monotonic and won't drift / overflow the way the
            // previous "uint ticks" did (~49 days). All polling decisions are
            // made against elapsed milliseconds since the loop started.
            Stopwatch stopwatch = Stopwatch.StartNew();
            long lastTickMs = -1;

            while (!shutdownToken.IsCancellationRequested)
            {
                // 1 second is the minimum supported polling interval.
                // Awaiting Task.Delay releases the thread between polls (the loop
                // used to occupy a dedicated long-running thread) and still exits
                // promptly when the host is shutting down.
                try
                {
                    await Task.Delay(1000, shutdownToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (string.IsNullOrEmpty(assetId)
                    || !_tags.TryGetValue(assetId, out List<AssetTag> assetTags)
                    || !_assets.TryGetValue(assetId, out IAsset asset))
                {
                    // asset was deleted while we were polling — exit gracefully
                    return;
                }

                long nowMs = stopwatch.ElapsedMilliseconds;
                if (nowMs == lastTickMs)
                {
                    continue;
                }

                lastTickMs = nowMs;

                // Snapshot the inner list so that a concurrent AddTag()/DeleteAsset()
                // cannot mutate the collection mid-enumeration.
                foreach (AssetTag tag in assetTags.ToArray())
                {
                    try
                    {
                        int effectivePollingIntervalMs = tag.PollingInterval <= 0 ? 1000 : tag.PollingInterval;
                        if (nowMs % effectivePollingIntervalMs >= 1000)
                        {
                            // not on a poll boundary for this tag this second
                            continue;
                        }

                        if (!IsReconnectAttemptDue(assetId))
                        {
                            // we are inside a backoff window — surface BadDataUnavailable but don't hammer the asset
                            UpdateUAServerVariable(tag, 0, false);
                            UpdateUAServerProperty(tag, 0, false);
                            continue;
                        }

                        // read the tag once per poll cycle and reuse the value
                        // for both variable and property updates to halve asset I/O.
                        Program.Telemetry.TagReads.Add(1);
                        object value = await asset.ReadAsync(tag, shutdownToken).ConfigureAwait(false);
                        bool connected = asset.IsConnected;
                        UpdateUAServerVariable(tag, value, connected);
                        UpdateUAServerProperty(tag, value, connected);

                        if (connected)
                        {
                            // success — clear reconnect backoff
                            ResetReconnectState(assetId);
                        }
                        else
                        {
                            // Drivers that report failure by returning null/false
                            // (rather than throwing) would otherwise leave the
                            // asset stuck "disconnected forever". Drive the same
                            // backoff/reconnect cycle the catch block uses.
                            Program.Telemetry.TagReadErrors.Add(1);
                            await TryReconnectAsync(assetId, asset, shutdownToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Program.Telemetry.TagReadErrors.Add(1);
                        UpdateUAServerVariable(tag, 0, false);
                        UpdateUAServerProperty(tag, 0, false);
                        Log.Logger.Error(ex, "Failed to read tag: {TagName}, Asset: {AssetId}", tag.Name, assetId);

                        await TryReconnectAsync(assetId, asset, shutdownToken).ConfigureAwait(false);
                    }
                }
            }
        }

        private bool IsReconnectAttemptDue(string assetId)
        {
            if (!_reconnectStates.TryGetValue(assetId, out ReconnectState state))
            {
                return true;
            }

            return Stopwatch.GetTimestamp() >= state.NextAttemptTimestamp;
        }

        private void ResetReconnectState(string assetId)
        {
            _reconnectStates.TryRemove(assetId, out _);
        }

        private async Task TryReconnectAsync(string assetId, IAsset asset, CancellationToken cancellationToken = default)
        {
            ReconnectState state = _reconnectStates.AddOrUpdate(
                assetId,
                _ => new ReconnectState { CurrentBackoffMs = _reconnectInitialBackoffMs, ConsecutiveFailures = 0 },
                (_, existing) => existing);

            try
            {
                Program.Telemetry.AssetReconnects.Add(1);
                Log.Logger.Information("Trying to reconnect to asset {AssetId} (attempt {Attempt})", assetId, state.ConsecutiveFailures + 1);

                string remoteEndpoint = asset.GetRemoteEndpoint();
                string host = null;
                int port = 0;

                if (string.IsNullOrWhiteSpace(remoteEndpoint))
                {
                    Log.Logger.Warning("Asset {AssetId} returned an empty remote endpoint; skipping reconnect.", assetId);
                    ScheduleNextReconnect(state);
                    return;
                }

                // Prefer URI parsing for endpoints like
                // opc.da://localhost/Matrikon.OPC.Simulation.1.
                if (Uri.TryCreate(remoteEndpoint, UriKind.Absolute, out Uri uri)
                    && !string.IsNullOrWhiteSpace(uri.Host))
                {
                    host = uri.Host;
                    if (!uri.IsDefaultPort)
                    {
                        port = uri.Port;
                    }
                }
                else if (remoteEndpoint.Contains("://", StringComparison.Ordinal))
                {
                    Log.Logger.Warning("Asset {AssetId} returned an invalid URI-like endpoint [{Endpoint}]; skipping reconnect.", assetId, remoteEndpoint);
                    ScheduleNextReconnect(state);
                    return;
                }
                else
                {
                    // Backward-compatible fallback for host[:port] endpoints.
                    string[] endpointParts = remoteEndpoint.Split(':');
                    host = endpointParts.Length > 0 ? endpointParts[0] : null;

                    if (endpointParts.Length > 1
                        && !string.IsNullOrEmpty(endpointParts[1])
                        && !int.TryParse(endpointParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
                    {
                        Log.Logger.Warning("Asset {AssetId} reported a non-integer port [{Port}]; defaulting to 0.", assetId, endpointParts[1]);
                        port = 0;
                    }
                }

                if (string.IsNullOrWhiteSpace(host))
                {
                    Log.Logger.Warning("Asset {AssetId} returned an endpoint without a valid host [{Endpoint}]; skipping reconnect.", assetId, remoteEndpoint);
                    ScheduleNextReconnect(state);
                    return;
                }

                await asset.DisconnectAsync(cancellationToken).ConfigureAwait(false);

                await asset.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

                if (asset.IsConnected)
                {
                    if (asset is IEventingAsset eventingAsset)
                    {
                        eventingAsset.StartEventSubscription();
                    }

                    Log.Logger.Information("Reconnected to asset {AssetId}.", assetId);
                    ResetReconnectState(assetId);
                    return;
                }

                Program.Telemetry.AssetReconnectFailures.Add(1);
                ScheduleNextReconnect(state);
            }
            catch (Exception ex)
            {
                Program.Telemetry.AssetReconnectFailures.Add(1);
                Log.Logger.Warning(ex, "Reconnect attempt for asset {AssetId} failed.", assetId);
                ScheduleNextReconnect(state);
            }
        }

        private void ScheduleNextReconnect(ReconnectState state)
        {
            state.ConsecutiveFailures++;
            int backoffMs = Math.Min(_reconnectMaxBackoffMs, state.CurrentBackoffMs);
            state.NextAttemptTimestamp = Stopwatch.GetTimestamp() + (long)(backoffMs * (Stopwatch.Frequency / 1000.0));
            state.CurrentBackoffMs = Math.Min(_reconnectMaxBackoffMs, state.CurrentBackoffMs * 2);
        }

        private void UpdateUAServerVariable(AssetTag tag, object value, bool connected)
        {
            if (!_uaVariables.TryGetValue(tag.Name, out var variable))
            {
                return;
            }

            // check for complex type
            if (variable.Value.TryGetValue(out ExtensionObject oldEo) && !oldEo.IsNull)
            {
                var opcuaType = (DataTypeState)Find(variable.DataType);

                if (!TryGetStructureDefinition(opcuaType, out StructureDefinition structDef))
                {
                    throw new InvalidOperationException($"DataTypeDefinition for {variable.DataType} is missing or not a structure.");
                }

                if (string.IsNullOrEmpty(tag.MappedUAFieldPath))
                {
                    // Whole-UDT update: driver returns decoded field values
                    if (value is Dictionary<string, object> fieldValues)
                    {
                        using var encoder = new BinaryEncoder(new ServiceMessageContext(Program.Telemetry, Server.Factory)
                        {
                            NamespaceUris = Server.NamespaceUris
                        });

                        foreach (StructureField field in structDef.Fields)
                        {
                            fieldValues.TryGetValue(field.Name, out object fieldVal);
                            EncodeField(encoder, field, fieldVal);
                        }

                        variable.Value = Variant.From(new ExtensionObject(oldEo.TypeId, new ByteString(encoder.CloseAndReturnBuffer())));
                    }
                }
                else
                {
                    // Single-field update: decode all existing values, overwrite the
                    // matching field, and re-encode the entire structure.
                    ByteString oldBody = default;
                    oldEo.TryGetAsBinary(out oldBody, Server.MessageContext);

                    using var decoder = new BinaryDecoder(oldBody.ToArray(), new ServiceMessageContext(Program.Telemetry, Server.Factory) {
                        NamespaceUris = Server.NamespaceUris
                    });

                    using var encoder = new BinaryEncoder(new ServiceMessageContext(Program.Telemetry, Server.Factory) {
                        NamespaceUris = Server.NamespaceUris
                    });

                    foreach (StructureField field in structDef.Fields)
                    {
                        bool isTargetField = field.Name == tag.MappedUAFieldPath;

                        uint fieldDataType = 0;
                        field.DataType.TryGetValue(out fieldDataType);

                        switch (fieldDataType)
                        {
                            case DataTypes.Float:
                            {
                                float v = decoder.ReadFloat(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteFloat(field.Name, v);
                                break;
                            }
                            case DataTypes.Double:
                            {
                                double v = decoder.ReadDouble(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteDouble(field.Name, v);
                                break;
                            }
                            case DataTypes.Boolean:
                            {
                                bool v = decoder.ReadBoolean(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteBoolean(field.Name, v);
                                break;
                            }
                            case DataTypes.SByte:
                            {
                                sbyte v = decoder.ReadSByte(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteSByte(field.Name, v);
                                break;
                            }
                            case DataTypes.Byte:
                            {
                                byte v = decoder.ReadByte(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteByte(field.Name, v);
                                break;
                            }
                            case DataTypes.Int16:
                            {
                                short v = decoder.ReadInt16(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteInt16(field.Name, v);
                                break;
                            }
                            case DataTypes.UInt16:
                            {
                                ushort v = decoder.ReadUInt16(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteUInt16(field.Name, v);
                                break;
                            }
                            case DataTypes.Int32:
                            {
                                int v = decoder.ReadInt32(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteInt32(field.Name, v);
                                break;
                            }
                            case DataTypes.UInt32:
                            {
                                uint v = decoder.ReadUInt32(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteUInt32(field.Name, v);
                                break;
                            }
                            case DataTypes.Int64:
                            {
                                long v = decoder.ReadInt64(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteInt64(field.Name, v);
                                break;
                            }
                            case DataTypes.UInt64:
                            {
                                ulong v = decoder.ReadUInt64(field.Name);
                                if (isTargetField) v = ConvertFieldValue(value, v, field.Name);
                                encoder.WriteUInt64(field.Name, v);
                                break;
                            }
                            case DataTypes.String:
                            {
                                string v = decoder.ReadString(field.Name);
                                if (isTargetField) v = Convert.ToString(value) ?? string.Empty;
                                encoder.WriteString(field.Name, v);
                                break;
                            }
                            default: throw new NotImplementedException("Complex type field data type " + field.DataType.ToString() + " not yet supported!");
                        }
                    }

                    variable.Value = Variant.From(new ExtensionObject(oldEo.TypeId, new ByteString(encoder.CloseAndReturnBuffer())));
                }
            }
            else
            {
                // Convert CLR types that are not OPC UA Variant-compatible.
                if (value is TimeSpan ts)
                {
                    // OPC UA Duration is a Double in milliseconds.
                    value = ts.TotalMilliseconds;
                }
                else if (value is DateTimeOffset dto)
                {
                    value = dto.UtcDateTime;
                }

                // Runtime-typed southbound value: see the note in NodeFactory.CreateVariable.
#pragma warning disable CS0618 // Type or member is obsolete
                variable.Value = new Variant(value, TypeInfo.Construct(value));
#pragma warning restore CS0618
            }

            variable.StatusCode = connected ? StatusCodes.Good : StatusCodes.BadDataUnavailable;
            variable.Timestamp = DateTime.UtcNow;
            variable.ClearChangeMasks(SystemContext, true);
        }

        private void UpdateUAServerProperty(AssetTag tag, object value, bool connected)
        {
            if (!_uaProperties.TryGetValue(tag.Name, out var property))
            {
                return;
            }

            // Runtime-typed southbound value: see the note in NodeFactory.CreateVariable.
#pragma warning disable CS0618 // Type or member is obsolete
            property.Value = new Variant(value, TypeInfo.Construct(value));
#pragma warning restore CS0618
            property.StatusCode = connected ? StatusCodes.Good : StatusCodes.BadDataUnavailable;
            property.Timestamp = DateTime.UtcNow;
            property.ClearChangeMasks(SystemContext, true);
        }

        /// <summary>
        /// Encodes a single structure field value for whole-UDT updates.
        /// Used by both AddNodeForWoTForm (initialization) and UpdateUAServerVariable (runtime).
        /// </summary>
        /// <summary>
        /// Converts a southbound value to the CLR type of a structure field.
        /// Drivers routinely return a neighbouring numeric type (a float for a
        /// Double field, an int for an Int16 field); those are converted rather
        /// than silently replaced by zero. When the value cannot be represented
        /// (wrong format or out of range) the fallback is kept and a warning is
        /// logged, so a bad reading never overwrites good data with a fake zero.
        /// </summary>
        private static T ConvertFieldValue<T>(object value, T fallback, string fieldName)
        {
            if (value is null)
            {
                return fallback;
            }

            if (value is T typed)
            {
                return typed;
            }

            try
            {
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                Log.Logger.Warning(
                    "Value {Value} ({ValueType}) cannot be converted to {TargetType} for structure field {Field}; keeping the previous value.",
                    value, value.GetType().Name, typeof(T).Name, fieldName);
                return fallback;
            }
        }

        private void EncodeField(BinaryEncoder encoder, StructureField field, object value)
        {
            uint fieldDataType = 0;
            field.DataType.TryGetValue(out fieldDataType);

            switch (fieldDataType)
            {
                case DataTypes.Float:
                    encoder.WriteFloat(field.Name, ConvertFieldValue(value, 0f, field.Name));
                    break;
                case DataTypes.Double:
                    encoder.WriteDouble(field.Name, ConvertFieldValue(value, 0d, field.Name));
                    break;
                case DataTypes.Boolean:
                    encoder.WriteBoolean(field.Name, ConvertFieldValue(value, false, field.Name));
                    break;
                case DataTypes.SByte:
                    encoder.WriteSByte(field.Name, ConvertFieldValue(value, (sbyte)0, field.Name));
                    break;
                case DataTypes.Byte:
                    encoder.WriteByte(field.Name, ConvertFieldValue(value, (byte)0, field.Name));
                    break;
                case DataTypes.Int16:
                    encoder.WriteInt16(field.Name, ConvertFieldValue(value, (short)0, field.Name));
                    break;
                case DataTypes.UInt16:
                    encoder.WriteUInt16(field.Name, ConvertFieldValue(value, (ushort)0, field.Name));
                    break;
                case DataTypes.Int32:
                    encoder.WriteInt32(field.Name, ConvertFieldValue(value, 0, field.Name));
                    break;
                case DataTypes.UInt32:
                    encoder.WriteUInt32(field.Name, ConvertFieldValue(value, 0u, field.Name));
                    break;
                case DataTypes.Int64:
                    encoder.WriteInt64(field.Name, ConvertFieldValue(value, 0L, field.Name));
                    break;
                case DataTypes.UInt64:
                    encoder.WriteUInt64(field.Name, ConvertFieldValue(value, 0UL, field.Name));
                    break;
                case DataTypes.String:
                    encoder.WriteString(field.Name, value?.ToString() ?? string.Empty);
                    break;
                default: throw new NotImplementedException("Complex type field data type " + field.DataType.ToString() + " not yet supported!");
            }
        }

        private NodeState InstantiateObjectTypeChild(BaseObjectState objectInstance, BaseObjectTypeState objectType, ushort namespaceIndex, string parentName, string fieldName, bool writeable)
        {
            NodeState mappedChild = null;

            // Browse the object type for its variable and property declarations.
            var browser = objectType.CreateBrowser(SystemContext, null, NodeId.Null, true, BrowseDirection.Forward, QualifiedName.Null, null, true);

            IReference reference = browser.Next();
            while (reference != null)
            {
                bool isComponent = reference.ReferenceTypeId == ReferenceTypeIds.HasComponent;
                bool isProperty = reference.ReferenceTypeId == ReferenceTypeIds.HasProperty;

                if ((isComponent || isProperty)
                    && Find(ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris)) is BaseVariableState declaration)
                {
                    if (declaration.DisplayName.Text == fieldName)
                    {
                        mappedChild = CreateObjectTypeChild(objectInstance, declaration, isProperty, namespaceIndex, parentName, writeable, mapped: true);
                    }
                    else if (declaration.ModellingRuleId == ObjectIds.ModellingRule_Mandatory)
                    {
                        // An instance must carry every mandatory child of its type,
                        // even when the Thing Description maps no tag onto it.
                        CreateObjectTypeChild(objectInstance, declaration, isProperty, namespaceIndex, parentName, false, mapped: false);
                    }
                }

                reference = browser.Next();
            }

            return mappedChild;
        }

        private BaseVariableState CreateObjectTypeChild(BaseObjectState objectInstance, BaseVariableState declaration, bool isProperty, ushort namespaceIndex, string parentName, bool writeable, bool mapped)
        {
            // Placeholder declarations are named like "<MeasurementValue>"; the
            // instance uses the plain name, in the type's namespace.
            string childName = declaration.BrowseName.Name.Trim('<', '>');
            QualifiedName browseName = new(childName, declaration.BrowseName.NamespaceIndex);

            // A property with several forms, or a second field mapped onto the
            // same object, reuses the child created the first time.
            if (objectInstance.FindChild(SystemContext, browseName) is BaseVariableState existing)
            {
                return existing;
            }

            // The NodeId stays qualified by the parent name so it is unique within
            // the asset namespace; only the browse and display names are shortened.
            string nodeName = $"{parentName}.{childName}";

            BaseVariableState child;
            if (isProperty)
            {
                child = _nodeFactory.CreateProperty(objectInstance, nodeName, declaration.DataType, namespaceIndex, writeable);
            }
            else if (mapped)
            {
                child = _nodeFactory.CreateVariable(objectInstance, nodeName, declaration.DataType, namespaceIndex, writeable);
            }
            else
            {
                // Unmapped mandatory components are not WoT components, so they are
                // created without the HasWoTComponent reference the factory adds.
                child = new BaseDataVariableState(objectInstance)
                {
                    NodeId = new NodeId(nodeName, namespaceIndex),
                    DataType = declaration.DataType,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    OnReadValue = OnReadValue
                };
                objectInstance.AddChild(child);
            }

            child.SymbolicName = childName;
            child.BrowseName = browseName;
            child.DisplayName = new Ua.LocalizedText("en", childName);
            child.ReferenceTypeId = isProperty ? ReferenceTypeIds.HasProperty : ReferenceTypeIds.HasComponent;
            child.TypeDefinitionId = declaration.TypeDefinitionId;
            child.ValueRank = declaration.ValueRank;

            AddPredefinedNode(SystemContext, child);

            return child;
        }
    }
}
