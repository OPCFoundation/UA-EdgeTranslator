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
    using System.Threading.Tasks;

    public class UANodeManager : CustomNodeManager2
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
                try
                {
                    _shutdownCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // already disposed — nothing to do
                }

                lock (Lock)
                {
                    foreach (FileManager manager in _fileManagers.Values)
                    {
                        manager.Dispose();
                    }

                    _fileManagers.Clear();
                }

                _shutdownCts.Dispose();
            }
        }

        public override NodeId New(ISystemContext context, NodeState node)
        {
            // for new nodes we create, pick our default namespace
            return new NodeId(Utils.IncrementIdentifier(ref _lastUsedId), (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
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
            deleteAsset.OnCallMethod = new GenericMethodCalledEventHandler(OnDeleteAsset);
            deleteAsset.InputArguments = _nodeFactory.CreateMethodArguments(deleteAsset, ["AssetId"], ["The NodeId of the WoTAsset object."], [new ExpandedNodeId(DataTypes.NodeId)], true, false, new NodeId(_cWoTDeleteAssetInputArguments, WoTConNamespaceIndex));

            MethodState discoverAssets = _nodeFactory.CreateMethod(_assetManagement, "DiscoverAssets", WoTConNamespaceIndex);
            discoverAssets.OnCallMethod = new GenericMethodCalledEventHandler(OnDiscoverAssets);
            discoverAssets.OutputArguments = _nodeFactory.CreateMethodArguments(discoverAssets, ["AssetEndpoints"], ["The list of discovered asset endpoints."], [new ExpandedNodeId(DataTypes.String)], false, true);
            AddPredefinedNode(SystemContext, discoverAssets);

            MethodState createAssetForEndpoint = _nodeFactory.CreateMethod(_assetManagement, "CreateAssetForEndpoint", WoTConNamespaceIndex);
            createAssetForEndpoint.OnCallMethod = new GenericMethodCalledEventHandler(OnCreateAssetForEndpoint);
            createAssetForEndpoint.InputArguments = _nodeFactory.CreateMethodArguments(createAssetForEndpoint, ["AssetName", "AssetEndpoint"], ["The name to be assigned to the asset.", "The endpoint to the asset on the network."], [new ExpandedNodeId(DataTypes.String), new ExpandedNodeId(DataTypes.String)], true);
            createAssetForEndpoint.OutputArguments = _nodeFactory.CreateMethodArguments(createAssetForEndpoint, ["AssetId"], ["The NodeId of the WoTAsset object, if call was successful."], [new ExpandedNodeId(DataTypes.NodeId)], false);
            AddPredefinedNode(SystemContext, createAssetForEndpoint);

            MethodState connectionTest = _nodeFactory.CreateMethod(_assetManagement, "ConnectionTest", WoTConNamespaceIndex);
            connectionTest.OnCallMethod = new GenericMethodCalledEventHandler(OnConnectionTest);
            connectionTest.InputArguments = _nodeFactory.CreateMethodArguments(connectionTest, ["AssetEndpoint"], ["The endpoint description of the asset to test the connection to."], [new ExpandedNodeId(DataTypes.String)], true);
            connectionTest.OutputArguments = _nodeFactory.CreateMethodArguments(connectionTest, ["Success", "Status"], ["Returns TRUE if a connection could be established to the asset.", "If a connection was established successfully, an asset-specific status code string describing the current health of the asset is returned."], [new ExpandedNodeId(DataTypes.String), new ExpandedNodeId(DataTypes.String)], false);
            AddPredefinedNode(SystemContext, connectionTest);

            // create file node to upload local nodeset files to the server
            FileState fileNode = new(_assetManagement);
            fileNode.Create(SystemContext, new NodeId("NodesetFileUpload", WoTConNamespaceIndex), new QualifiedName("NodesetFileUpload"), null, false);
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
                new ExpandedNodeId(_cWoTAssetConfigurationType, _cWotCon));
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

        public ServiceResult OnCreateAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            string assetName = inputArguments[0]?.ToString();
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
                outputArguments[0] = assetNode.NodeId;

                RaiseModelChangedEvent(assetNode.NodeId, ModelChangeStructureVerbMask.NodeAdded);

                return ServiceResult.Good;
            }
        }

        private bool CreateAssetNode(string assetName, out NodeState assetNode)
        {
            lock (Lock)
            {
                // check if the asset node already exists
                INodeBrowser browser = _assetManagement.CreateBrowser(SystemContext, null, null, false, BrowseDirection.Forward, null, null, true);

                IReference reference = browser.Next();
                while ((reference != null) && (reference is NodeStateReference))
                {
                    NodeStateReference node = reference as NodeStateReference;
                    if ((node.Target != null) && (node.Target.DisplayName.Text == assetName))
                    {
                        assetNode = node.Target;
                        return false;
                    }

                    reference = browser.Next();
                }

                BaseInterfaceState asset = new(null);
                asset.Create(SystemContext, new NodeId(), new QualifiedName(assetName), null, true);
                asset.TypeDefinitionId = ExpandedNodeId.ToNodeId(new ExpandedNodeId(_cIWoTAssetType, _cWotCon), Server.NamespaceUris);

                FileState fileNode = new(asset);
                fileNode.Create(SystemContext, new NodeId(), new QualifiedName("WoTFile"), null, true);
                fileNode.TypeDefinitionId = ExpandedNodeId.ToNodeId(new ExpandedNodeId(_cWoTAssetFileType, _cWotCon), Server.NamespaceUris);
                asset.AddChild(fileNode);

                _assetManagement.AddChild(asset);

                FileManager fileManager = new(this, fileNode);
                _fileManagers.Add(asset.NodeId, fileManager);

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
            ev.Changes = new PropertyState<ModelChangeStructureDataType[]>(ev);
            ev.Changes.Value = changes;

            ev.SetChildValue(SystemContext, BrowseNames.SourceNode, _assetManagement, false);
            ev.SetChildValue(SystemContext, BrowseNames.SourceName, "AssetManagement", false);

            Server.ReportEvent(ev);
        }

        public ServiceResult OnDeleteAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (string.IsNullOrEmpty(inputArguments[0]?.ToString()))
            {
                return StatusCodes.BadInvalidArgument;
            }

            NodeId assetId = (NodeId)inputArguments[0];

            lock (Lock)
            {
                NodeState asset = FindPredefinedNode<BaseInterfaceState>(assetId);
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
                    // Release the southbound connection (socket/stream) the driver
                    // holds. Dropping only the dictionary reference leaks it until
                    // finalization. Defensive: a faulty driver must not fail delete.
                    try
                    {
                        removedAsset?.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Warning(ex, "Failed to disconnect asset {AssetId} during delete.", assetName);
                    }
                }

                _reconnectStates.TryRemove(assetName, out _);

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

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Unloads a WoT file from the running server: it locates the asset created
        /// from the file by name and removes it through the same code path used by the
        /// OPC UA DeleteAsset method (<see cref="OnDeleteAsset"/>), which deletes the
        /// address-space nodes, disconnects the southbound driver, prunes the tag state
        /// and removes the .jsonld file from the settings folder. Returns true when a
        /// matching asset was found and unloaded.
        /// </summary>
        public bool UnloadWoTFile(string fileName)
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

            if (assetId == null)
            {
                Log.Logger.Warning("Cannot unload WoT file [{FileName}]: no matching asset is currently loaded.", fileName);
                return false;
            }

            ServiceResult result = OnDeleteAsset(SystemContext, null, new List<object> { assetId }, new List<object>());

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
                return null;
            }

            INodeBrowser browser = _assetManagement.CreateBrowser(SystemContext, null, null, false, BrowseDirection.Forward, null, null, true);

            IReference reference = browser.Next();
            while ((reference != null) && (reference is NodeStateReference))
            {
                NodeStateReference node = reference as NodeStateReference;
                if ((node.Target != null) && (node.Target.DisplayName.Text == assetName))
                {
                    return node.Target.NodeId;
                }

                reference = browser.Next();
            }

            return null;
        }

        public ServiceResult OnDiscoverAssets(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            List<string> allAddresses = new();

            try
            {
                foreach (var drv in Program.Drivers.AllDrivers)
                {
                    allAddresses.AddRange(drv.Discover());
                }
            }
            catch (Exception ex)
            {
                // Log full exception server-side, return only a generic status to
                // the OPC UA client to avoid leaking internal exception text.
                Log.Logger.Error(ex, "Failed to discover assets");
                return new ServiceResult(StatusCodes.BadTimeout);
            }

            outputArguments[0] = allAddresses.ToArray();

            return ServiceResult.Good;
        }

        private ServiceResult OnCreateAssetForEndpoint(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (string.IsNullOrEmpty(inputArguments[0]?.ToString()) || string.IsNullOrEmpty(inputArguments[1]?.ToString()))
            {
                return StatusCodes.BadInvalidArgument;
            }

            string assetName = inputArguments[0].ToString();
            if (!IsSafeAssetName(assetName))
            {
                Log.Logger.Warning("Rejecting CreateAssetForEndpoint: invalid asset name [{AssetName}]", assetName);
                return StatusCodes.BadInvalidArgument;
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
                string assetEndpoint = inputArguments[1].ToString();
                if (!Program.Drivers.TryGetByUri(assetEndpoint, out var drv))
                {
                    return new ServiceResult(StatusCodes.BadNotSupported, $"No driver installed for endpoint: {assetEndpoint}");
                }

                ThingDescription td = drv.BrowseAndGenerateTD(assetName, assetEndpoint);

                string contents = JsonConvert.SerializeObject(td, Formatting.Indented);

                _fileManagers[assetNode.NodeId].Write(context, Encoding.UTF8.GetBytes(contents));

                AsyncBridge.RunSync(() => OnboardAssetFromWoTFileAsync(assetNode, contents));

                System.IO.File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings", assetName + ".jsonld"), contents);

                RaiseModelChangedEvent(assetNode.NodeId, ModelChangeStructureVerbMask.NodeAdded);

                return ServiceResult.Good;
            }
        }

        private ServiceResult OnConnectionTest(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (string.IsNullOrEmpty(inputArguments[0]?.ToString()))
            {
                return StatusCodes.BadInvalidArgument;
            }

            string endpoint = inputArguments[0].ToString().Trim();

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

            const int probeTimeoutMs = 3000;
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
                cts.CancelAfter(probeTimeoutMs);

                AsyncBridge.RunSync(() => tcp.ConnectAsync(host, port, cts.Token).AsTask());

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
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_ASSET_CONNECTION_TEST")))
            {
                AssetConnectionTest(td, out unitId);
            }

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

            _ = Task.Factory.StartNew(ReadAssetTags, td.Name, TaskCreationOptions.LongRunning);

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
                lock (Server.CoreNodeManager.DataLock)
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
                    method.OnCallMethod = new GenericMethodCalledEventHandler(OnTDActionCalled);

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
                                var existingNode = FindNodeInAddressSpace(new NodeId(variableId, assetNamespaceIndex));
                                if (existingNode == null)
                                {
                                    objectInstance = _nodeFactory.CreateObject(assetFolder, variableName, ParseExpandedNodeId(property.Value.OpcUaType));
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
                            else if ((dataType?.DataTypeDefinition?.Body is StructureDefinition) && (((StructureDefinition)dataType?.DataTypeDefinition?.Body)?.Fields?.Count > 0))
                            {
                                // Data Structure type path: instantiate a new extension object once, then map WoT properties to fields
                                NodeId defaultBinaryEncodingId = GetDefaultBinaryEncodingId(dataType);
                                if (defaultBinaryEncodingId == null)
                                {
                                    throw new InvalidOperationException($"No 'Default Binary' encoding found for DataType {dataType.BrowseName} ({dataType.NodeId}).");
                                }

                                ExtensionObject complexTypeInstance = new()
                                {
                                    TypeId = defaultBinaryEncodingId
                                };

                                using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry)
                                {
                                    NamespaceUris = Server.NamespaceUris,
                                    Factory = Server.Factory
                                });

                                foreach (StructureField field in ((StructureDefinition)dataType?.DataTypeDefinition?.Body).Fields)
                                {
                                    EncodeField(encoder, field, null);

                                    if (field.Name == property.Value.OpcUaFieldPath)
                                    {
                                        // capture the field path to make sure we can identify the tag during data updates
                                        fieldPath = field.Name;
                                    }
                                }

                                complexTypeInstance.Body = encoder.CloseAndReturnBuffer();

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
                                BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, DataTypes.Float, assetNamespaceIndex, !property.Value.ReadOnly);
                                _uaVariables.TryAdd(variableId, variable);
                                AddPredefinedNode(SystemContext, variable);
                            }
                        }
                        else
                        {
                            // No field path — check if it's a structure type that needs an initialized ExtensionObject, or a simple built-in type.
                            if ((dataType?.DataTypeDefinition?.Body is StructureDefinition structDef) && (structDef.Fields?.Count > 0))
                            {
                                NodeId defaultBinaryEncodingId = GetDefaultBinaryEncodingId(dataType);
                                if (defaultBinaryEncodingId == null)
                                {
                                    throw new InvalidOperationException($"No 'Default Binary' encoding found for DataType {dataType.BrowseName} ({dataType.NodeId}).");
                                }

                                ExtensionObject complexTypeInstance = new() {
                                    TypeId = defaultBinaryEncodingId
                                };

                                using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry) {
                                    NamespaceUris = Server.NamespaceUris,
                                    Factory = Server.Factory
                                });

                                foreach (StructureField field in structDef.Fields)
                                {
                                    EncodeField(encoder, field, null);
                                }

                                complexTypeInstance.Body = encoder.CloseAndReturnBuffer();

                                BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, dataType.NodeId, assetNamespaceIndex, !property.Value.ReadOnly, complexTypeInstance);
                                _uaVariables.TryAdd(variableId, variable);
                                AddPredefinedNode(SystemContext, variable);
                            }
                            else
                            {
                                // it's an OPC UA built-in type
                                BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, dataType.NodeId, assetNamespaceIndex, !property.Value.ReadOnly);
                                _uaVariables.TryAdd(variableId, variable);
                                AddPredefinedNode(SystemContext, variable);
                            }
                        }
                    }
                    else
                    {
                        // no namespace info, default to float
                        BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, DataTypes.Float, assetNamespaceIndex, !property.Value.ReadOnly);
                        _uaVariables.TryAdd(variableId, variable);
                        AddPredefinedNode(SystemContext, variable);
                    }
                }
                else
                {
                    // can't parse type info, default to float
                    BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, DataTypes.Float, assetNamespaceIndex, !property.Value.ReadOnly);
                    _uaVariables.TryAdd(variableId, variable);
                    AddPredefinedNode(SystemContext, variable);
                }
            }
            else
            {
                // no type info, default to float
                BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, DataTypes.Float, assetNamespaceIndex, !property.Value.ReadOnly);
                _uaVariables.TryAdd(variableId, variable);
                AddPredefinedNode(SystemContext, variable);
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

                if (targetNode?.BrowseName?.Name == "Default Binary")
                {
                    return targetNode.NodeId;
                }
            }

            return null;
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

        private void AssetConnectionTest(ThingDescription td, out byte unitId)
        {
            unitId = 1;

            if (!Program.Drivers.TryGetByUri(td.Base, out var drv))
            {
                throw new Exception($"No driver installed for base URI: {td.Base}");
            }

            IAsset asset = drv.CreateAndConnectAsset(td, out unitId);

            _assets[td.Name] = asset;
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
                        return null;
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
                            return null;
                    }
                }
            }

            return null;
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

        public ServiceResult OnReadValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
        {
            bool provisioningMode = IsInProvisioningMode();
            if (provisioningMode && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE")))
            {
                return new ServiceResult(StatusCodes.BadNotReadable, "Access to UA Edge Translator is limited while in provisioning mode!");
            }

            PropertyState property = node as PropertyState;
            if (property != null)
            {
                if (node.DisplayName.Text == "SupportedWoTBindings")
                {
                    value = Program.Drivers.AllDrivers
                        .Select(d => d.WoTBindingUri)
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .ToArray();

                    timestamp = DateTime.UtcNow;
                    statusCode = StatusCodes.Good;

                    return ServiceResult.Good;
                }

                if (node.DisplayName.Text == "SupportedOPCUAInfoModels")
                {
                    value = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "nodesets"));

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
                    value = currentProcess.WorkingSet64 / (1024 * 1024);
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

        public ServiceResult OnWriteValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
        {
            bool provisioningMode = IsInProvisioningMode();
            if (provisioningMode && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE")))
            {
                return new ServiceResult(StatusCodes.BadNotWritable, "Access to UA Edge Translator is limited while in provisioning mode!");
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
                    // validate license key provided
                    if (value == null || string.IsNullOrEmpty(value.ToString()))
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument, "License key cannot be empty!");
                    }

                    // The expected license key must be supplied out-of-band via the LICENSE_KEY
                    // environment variable. If it is not configured the server refuses the write
                    // rather than falling back to a hard-coded value.
                    string expectedLicenseKey = Environment.GetEnvironmentVariable("LICENSE_KEY");
                    if (string.IsNullOrEmpty(expectedLicenseKey))
                    {
                        Log.Logger.Warning("License write rejected: LICENSE_KEY environment variable is not configured on the server.");
                        return new ServiceResult(StatusCodes.BadNotSupported, "License validation is not configured on this server.");
                    }

                    byte[] providedBytes = Encoding.UTF8.GetBytes(value.ToString());
                    byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedLicenseKey);
                    if (providedBytes.Length != expectedBytes.Length
                     || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument, "Invalid license key!");
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
                    string assetId = indexed.AssetId;
                    AssetTag tag = indexed.Tag;

                    try
                    {
                        if (!_assets.TryGetValue(assetId, out IAsset asset))
                        {
                            Program.Telemetry.TagWriteErrors.Add(1);
                            return new ServiceResult(StatusCodes.BadDataUnavailable);
                        }

                        Program.Telemetry.TagWrites.Add(1);
                        asset.Write(tag, value);

                        if (_uaVariables.TryGetValue(tag.Name, out BaseDataVariableState cached))
                        {
                            cached.Value = value;
                            cached.Timestamp = DateTime.UtcNow;
                            cached.ClearChangeMasks(SystemContext, true);
                        }

                        statusCode = asset.IsConnected ? StatusCodes.Good : StatusCodes.BadDataUnavailable;
                        return ServiceResult.Good;
                    }
                    catch (Exception ex)
                    {
                        Program.Telemetry.TagWriteErrors.Add(1);
                        // Log full exception server-side, return only a generic
                        // status to the OPC UA client to avoid leaking internal
                        // exception text or stack traces over the wire.
                        Log.Logger.Error(ex, "Failed to write value for tag: {TagName}", tag.Name);
                        return new ServiceResult(StatusCodes.BadDataUnavailable);
                    }
                }
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnTDActionCalled(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            try
            {
                string assetId = method.Parent.BrowseName.Name;

                string result = _assets[assetId].ExecuteAction(method, inputArguments, ref outputArguments);
                if (result == null)
                {
                    return new ServiceResult(StatusCodes.Uncertain, new Ua.LocalizedText("no result"));
                }
                else if (result.ToLowerInvariant() == "ok" || result.ToLowerInvariant() == "success")
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
        private void ReadAssetTags(object assetNameObject)
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
                // WaitOne(...) on the cancellation handle lets the polling task
                // exit promptly when the host is shutting down instead of
                // waiting up to a full second for the next iteration.
                if (shutdownToken.WaitHandle.WaitOne(1000))
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
                        object value = asset.Read(tag);
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
                            TryReconnect(assetId, asset);
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Telemetry.TagReadErrors.Add(1);
                        UpdateUAServerVariable(tag, 0, false);
                        UpdateUAServerProperty(tag, 0, false);
                        Log.Logger.Error(ex, "Failed to read tag: {TagName}, Asset: {AssetId}", tag.Name, assetId);

                        TryReconnect(assetId, asset);
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

        private void TryReconnect(string assetId, IAsset asset)
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
                string[] endpointParts = remoteEndpoint?.Split(':');
                if (endpointParts == null || endpointParts.Length == 0 || string.IsNullOrEmpty(endpointParts[0]))
                {
                    Log.Logger.Warning("Asset {AssetId} returned an empty remote endpoint; skipping reconnect.", assetId);
                    ScheduleNextReconnect(state);
                    return;
                }

                asset.Disconnect();

                int port = 0;
                if (endpointParts.Length > 1
                    && !string.IsNullOrEmpty(endpointParts[1])
                    && !int.TryParse(endpointParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
                {
                    Log.Logger.Warning("Asset {AssetId} reported a non-integer port [{Port}]; defaulting to 0.", assetId, endpointParts[1]);
                    port = 0;
                }

                asset.Connect(endpointParts[0], port);

                if (asset.IsConnected)
                {
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
            if (variable.Value is ExtensionObject oldEo)
            {
                var opcuaType = (DataTypeState)Find(variable.DataType);

                var structDef = opcuaType?.DataTypeDefinition?.Body as StructureDefinition;
                if (structDef == null || structDef.Fields == null || structDef.Fields.Count == 0)
                {
                    throw new InvalidOperationException($"DataTypeDefinition for {variable.DataType} is missing or not a structure.");
                }

                if (string.IsNullOrEmpty(tag.MappedUAFieldPath))
                {
                    // Whole-UDT update: driver returns decoded field values
                    if (value is Dictionary<string, object> fieldValues)
                    {
                        using var encoder = new BinaryEncoder(new ServiceMessageContext(Program.Telemetry)
                        {
                            NamespaceUris = Server.NamespaceUris,
                            Factory = Server.Factory
                        });

                        foreach (StructureField field in structDef.Fields)
                        {
                            fieldValues.TryGetValue(field.Name, out object fieldVal);
                            EncodeField(encoder, field, fieldVal);
                        }

                        variable.Value = new ExtensionObject(oldEo.TypeId, encoder.CloseAndReturnBuffer());
                    }
                }
                else
                {
                    // Single-field update: decode all existing values, overwrite the
                    // matching field, and re-encode the entire structure.
                    var oldBody = oldEo.Body as byte[];
                    using var decoder = new BinaryDecoder(oldBody, new ServiceMessageContext(Program.Telemetry) {
                        NamespaceUris = Server.NamespaceUris,
                        Factory = Server.Factory
                    });

                    using var encoder = new BinaryEncoder(new ServiceMessageContext(Program.Telemetry) {
                        NamespaceUris = Server.NamespaceUris,
                        Factory = Server.Factory
                    });

                    foreach (StructureField field in structDef.Fields)
                    {
                        bool isTargetField = field.Name == tag.MappedUAFieldPath;

                        switch ((uint)field.DataType.Identifier)
                        {
                            case DataTypes.Float:
                            {
                                float v = decoder.ReadFloat(field.Name);
                                if (isTargetField) v = value is float f ? f : 0f;
                                encoder.WriteFloat(field.Name, v);
                                break;
                            }
                            case DataTypes.Double:
                            {
                                double v = decoder.ReadDouble(field.Name);
                                if (isTargetField) v = value is double d ? d : 0d;
                                encoder.WriteDouble(field.Name, v);
                                break;
                            }
                            case DataTypes.Boolean:
                            {
                                bool v = decoder.ReadBoolean(field.Name);
                                if (isTargetField) v = value is bool b && b;
                                encoder.WriteBoolean(field.Name, v);
                                break;
                            }
                            case DataTypes.SByte:
                            {
                                sbyte v = decoder.ReadSByte(field.Name);
                                if (isTargetField) v = value is sbyte sb ? sb : (sbyte)0;
                                encoder.WriteSByte(field.Name, v);
                                break;
                            }
                            case DataTypes.Byte:
                            {
                                byte v = decoder.ReadByte(field.Name);
                                if (isTargetField) v = value is byte by ? by : (byte)0;
                                encoder.WriteByte(field.Name, v);
                                break;
                            }
                            case DataTypes.Int16:
                            {
                                short v = decoder.ReadInt16(field.Name);
                                if (isTargetField) v = value is short s ? s : (short)0;
                                encoder.WriteInt16(field.Name, v);
                                break;
                            }
                            case DataTypes.UInt16:
                            {
                                ushort v = decoder.ReadUInt16(field.Name);
                                if (isTargetField) v = value is ushort us ? us : (ushort)0;
                                encoder.WriteUInt16(field.Name, v);
                                break;
                            }
                            case DataTypes.Int32:
                            {
                                int v = decoder.ReadInt32(field.Name);
                                if (isTargetField) v = value is int iv ? iv : 0;
                                encoder.WriteInt32(field.Name, v);
                                break;
                            }
                            case DataTypes.UInt32:
                            {
                                uint v = decoder.ReadUInt32(field.Name);
                                if (isTargetField) v = value is uint uv ? uv : 0u;
                                encoder.WriteUInt32(field.Name, v);
                                break;
                            }
                            case DataTypes.Int64:
                            {
                                long v = decoder.ReadInt64(field.Name);
                                if (isTargetField) v = value is long lv ? lv : 0L;
                                encoder.WriteInt64(field.Name, v);
                                break;
                            }
                            case DataTypes.UInt64:
                            {
                                ulong v = decoder.ReadUInt64(field.Name);
                                if (isTargetField) v = value is ulong ulv ? ulv : 0UL;
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

                    variable.Value = new ExtensionObject(oldEo.TypeId, encoder.CloseAndReturnBuffer());
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

                variable.Value = value;
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

            property.Value = value;
            property.StatusCode = connected ? StatusCodes.Good : StatusCodes.BadDataUnavailable;
            property.Timestamp = DateTime.UtcNow;
            property.ClearChangeMasks(SystemContext, true);
        }

        /// <summary>
        /// Encodes a single structure field value for whole-UDT updates.
        /// Used by both AddNodeForWoTForm (initialization) and UpdateUAServerVariable (runtime).
        /// </summary>
        private void EncodeField(BinaryEncoder encoder, StructureField field, object value)
        {
            switch ((uint)field.DataType.Identifier)
            {
                case DataTypes.Float:
                    encoder.WriteFloat(field.Name, value is float f ? f : 0f);
                    break;
                case DataTypes.Double:
                    encoder.WriteDouble(field.Name, value is double d ? d : 0d);
                    break;
                case DataTypes.Boolean:
                    encoder.WriteBoolean(field.Name, value is bool b && b);
                    break;
                case DataTypes.SByte:
                    encoder.WriteSByte(field.Name, value is sbyte sb ? sb : (sbyte)0);
                    break;
                case DataTypes.Byte:
                    encoder.WriteByte(field.Name, value is byte by ? by : (byte)0);
                    break;
                case DataTypes.Int16:
                    encoder.WriteInt16(field.Name, value is short s ? s : (short)0);
                    break;
                case DataTypes.UInt16:
                    encoder.WriteUInt16(field.Name, value is ushort us ? us : (ushort)0);
                    break;
                case DataTypes.Int32:
                    encoder.WriteInt32(field.Name, value is int iv ? iv : 0);
                    break;
                case DataTypes.UInt32:
                    encoder.WriteUInt32(field.Name, value is uint uv ? uv : 0u);
                    break;
                case DataTypes.Int64:
                    encoder.WriteInt64(field.Name, value is long lv ? lv : 0L);
                    break;
                case DataTypes.UInt64:
                    encoder.WriteUInt64(field.Name, value is ulong ulv ? ulv : 0UL);
                    break;
                case DataTypes.String:
                    encoder.WriteString(field.Name, value?.ToString() ?? string.Empty);
                    break;
                default: throw new NotImplementedException("Complex type field data type " + field.DataType.ToString() + " not yet supported!");
            }
        }

        private NodeState InstantiateObjectTypeChild(BaseObjectState objectInstance, BaseObjectTypeState objectType, ushort namespaceIndex, string parentName, string fieldName, bool writeable)
        {
            NodeState childVar = null;

            // Browse the object type for child variables and replicate them on the instance
            var browser = objectType.CreateBrowser(SystemContext, null, null, true, BrowseDirection.Forward, null, null, true);

            IReference reference = browser.Next();
            while (reference != null)
            {
                // find the target node for this reference
                NodeState target = Find(ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris));
                if (target.DisplayName.Text == fieldName)
                {
                    // Remove angle brackets from field name if present
                    string cleanFieldName = fieldName.Trim('<', '>');

                    if (reference.ReferenceTypeId == ReferenceTypes.HasComponent)
                    {
                        // Create a matching variable under the object instance
                        childVar = _nodeFactory.CreateVariable(
                            objectInstance,
                            $"{parentName}.{cleanFieldName}",
                            reference.ReferenceTypeId,
                            namespaceIndex,
                            writeable);
                    }

                    if (reference.ReferenceTypeId == ReferenceTypes.HasProperty)
                    {
                        // Create a matching property under the object instance
                        childVar = _nodeFactory.CreateProperty(
                            objectInstance,
                            $"{parentName}.{cleanFieldName}",
                            reference.ReferenceTypeId,
                            namespaceIndex,
                            writeable);
                    }
                 }

                reference = browser.Next();
            }

            if (childVar != null)
            {
                AddPredefinedNode(SystemContext, childVar);
            }

            return childVar;
        }
    }
}
