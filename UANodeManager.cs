
namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.NetworkInformation;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UANodeSet = Export.UANodeSet;

    public class UANodeManager : CustomNodeManager2
    {
        private long _lastUsedId = 0;

        private bool _shutdown = false;

        private bool _wotAssetManagementLoaded = false;

        private readonly NodeFactory _nodeFactory;

        private BaseObjectState _assetManagement;

        private readonly Dictionary<string, BaseDataVariableState> _uaVariables = new();

        private readonly Dictionary<string, PropertyState> _uaProperties = new();

        private readonly Dictionary<string, IAsset> _assets = new();

        private readonly Dictionary<string, List<AssetTag>> _tags = new();

        private readonly UACloudLibraryClient _uacloudLibraryClient = new();

        private readonly Dictionary<NodeId, FileManager> _fileManagers = new();

        private readonly LoRaWANNetworkServer _lorawanNetworkServer = new();

        private readonly OCPPCentralSystem _ocppCentralSystem = new();

        private uint _ticks = 0;

        private const string _cWotCon = "http://opcfoundation.org/UA/WoT-Con/";

        private const uint _cWoTAssetManagement = 31;
        private const uint _cWoTCreateAsset = 32;
        private const uint _cWoTCreateAssetInputArguments = 33;
        private const uint _cWoTCreateAssetOutputArguments = 34;
        private const uint _cWoTDeleteAsset = 35;
        private const uint _cWoTDeleteAssetInputArguments = 36;
        private const uint _cIWoTAssetType = 56;
        private const uint _cWoTAssetFileType = 86;
        private const uint _cWoTAssetConfigurationType = 105;

        public UANodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            _nodeFactory = new NodeFactory(this);

            // create our settings folder, if required
            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "settings")))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "settings"));
            }

            List<string> namespaceUris = new()
            {
                "http://opcfoundation.org/UA/EdgeTranslator/"
            };

            _uacloudLibraryClient.Login();

            // load namespaces from downloaded nodesets
            List<string> namespacesFromDownloadedNodesets = _uacloudLibraryClient.GetNamespacesFromDownloadedNodesets();
            foreach (string namespaceFromDownloadedNodeset in namespacesFromDownloadedNodesets)
            {
                if (!namespaceUris.Contains(namespaceFromDownloadedNodeset))
                {
                    namespaceUris.Add(namespaceFromDownloadedNodeset);
                }
            }

            NamespaceUris = namespaceUris;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
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
        }

        public override NodeId New(ISystemContext context, NodeState node)
        {
            // for new nodes we create, pick our default namespace
            return new NodeId(Utils.IncrementIdentifier(ref _lastUsedId), (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                // in the create address space call, we add all our nodes
                IList<IReference> objectsFolderReferences = null;
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out objectsFolderReferences))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = objectsFolderReferences = new List<IReference>();
                }

                AddAllNodesFromDownloadedNodesetFiles();

                _wotAssetManagementLoaded = true;

                AddNodesForAssetManagement();

                LoadLocalWoTFiles();

                AddReverseReferences(externalReferences);
                base.CreateAddressSpace(externalReferences);
            }
        }

        private void LoadLocalWoTFiles()
        {
            IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
            foreach (string file in WoTFiles)
            {
                try
                {
                    string contents = System.IO.File.ReadAllText(file);
                    string fileName = Path.GetFileNameWithoutExtension(file);

                    if (!CreateAssetNode(fileName, out NodeState assetNode))
                    {
                        throw new Exception("Asset already exists");
                    }

                    OnboardAssetFromWoTFile(assetNode, contents);
                }
                catch (Exception ex)
                {
                    // skip this file, but log an error
                    Log.Logger.Error(ex.Message, ex);
                }
            }
        }

        private void AddNodesForAssetManagement()
        {
            ushort WoTConNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex(_cWotCon);

            _assetManagement = (BaseObjectState)FindPredefinedNode(new NodeId(_cWoTAssetManagement, WoTConNamespaceIndex), typeof(BaseObjectState));

            MethodState createAsset = (MethodState)FindPredefinedNode(new NodeId(_cWoTCreateAsset, WoTConNamespaceIndex), typeof(MethodState));
            createAsset.OnCallMethod = new GenericMethodCalledEventHandler(OnCreateAsset);
            createAsset.InputArguments = _nodeFactory.CreateMethodArguments(createAsset, ["AssetName"], ["A unique name for the asset."], [new ExpandedNodeId(DataTypes.String)], true, false, new NodeId(_cWoTCreateAssetInputArguments, WoTConNamespaceIndex));
            createAsset.OutputArguments = _nodeFactory.CreateMethodArguments(createAsset, ["AssetId"], ["The NodeId of the WoTAsset object, if call was successful."], [new ExpandedNodeId(DataTypes.NodeId)], false, false, new NodeId(_cWoTCreateAssetOutputArguments, WoTConNamespaceIndex));

            MethodState deleteAsset = (MethodState)FindPredefinedNode(new NodeId(_cWoTDeleteAsset, WoTConNamespaceIndex), typeof(MethodState));
            deleteAsset.OnCallMethod = new GenericMethodCalledEventHandler(OnDeleteAsset);
            deleteAsset.InputArguments = _nodeFactory.CreateMethodArguments(deleteAsset, ["AssetId"], ["The NodeId of the WoTAsset object."], [new ExpandedNodeId(DataTypes.NodeId)], true, false, new NodeId(_cWoTDeleteAssetInputArguments, WoTConNamespaceIndex));

            MethodState discoverAssets = _nodeFactory.CreateMethod(_assetManagement, "DiscoverAssets");
            discoverAssets.OnCallMethod = new GenericMethodCalledEventHandler(OnDiscoverAssets);
            discoverAssets.OutputArguments = _nodeFactory.CreateMethodArguments(discoverAssets, ["AssetEndpoints"], ["The list of discovered asset endpoints."], [new ExpandedNodeId(DataTypes.String)], false, true);
            AddPredefinedNode(SystemContext, discoverAssets);

            MethodState createAssetForEndpoint = _nodeFactory.CreateMethod(_assetManagement, "CreateAssetForEndpoint");
            createAssetForEndpoint.OnCallMethod = new GenericMethodCalledEventHandler(OnCreateAssetForEndpoint);
            createAssetForEndpoint.InputArguments = _nodeFactory.CreateMethodArguments(createAssetForEndpoint, ["AssetName", "AssetEndpoint"], ["The name to be assigned to the asset.", "The endpoint to the asset on the network."], [new ExpandedNodeId(DataTypes.String), new ExpandedNodeId(DataTypes.String)], true);
            createAssetForEndpoint.OutputArguments = _nodeFactory.CreateMethodArguments(createAssetForEndpoint, ["AssetId"], ["The NodeId of the WoTAsset object, if call was successful."], [new ExpandedNodeId(DataTypes.NodeId)], false);
            AddPredefinedNode(SystemContext, createAssetForEndpoint);

            MethodState connectionTest = _nodeFactory.CreateMethod(_assetManagement, "ConnectionTest");
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
            PropertyState supportedWoTBindingsProperty = _nodeFactory.CreateProperty(_assetManagement, "SupportedWoTBindings", new ExpandedNodeId(DataTypes.UriString), WoTConNamespaceIndex, false);
            AddPredefinedNode(SystemContext, supportedWoTBindingsProperty);

            // create a property listing our supported OPC UA nodesets to map to
            PropertyState supportedOPCUAInfoModelsProperty = _nodeFactory.CreateProperty(_assetManagement, "SupportedOPCUAInfoModels", new ExpandedNodeId(DataTypes.UriString), WoTConNamespaceIndex, false);
            AddPredefinedNode(SystemContext, supportedOPCUAInfoModelsProperty);

            BaseObjectState configuration = _nodeFactory.CreateObject(
                _assetManagement,
                "Configuration",
                new ExpandedNodeId(_cWoTAssetConfigurationType, _cWotCon));
            AddPredefinedNode(SystemContext, configuration);

            // create a property for the license key
            PropertyState licenseProperty = _nodeFactory.CreateProperty(configuration, "License", new ExpandedNodeId(DataTypes.String), WoTConNamespaceIndex, true, string.Empty);
            _uaProperties.Add("License", licenseProperty);
            AddPredefinedNode(SystemContext, licenseProperty);

            // create a variable for the current memory working set
            BaseDataVariableState variable = _nodeFactory.CreateVariable(_assetManagement, "MemoryWorkingSet(MB)", new ExpandedNodeId(DataTypes.Int32), WoTConNamespaceIndex);
            AddPredefinedNode(SystemContext, variable);
        }

        private List<string> LoadNamespacesFromThingDescription(ThingDescription td)
        {
            // add a new namespace for the Thing Description itself
            List<string> namespaceUris = new() {
                "http://opcfoundation.org/UA/" + td.Name + "/"
            };

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
                        // check if namespace URI is already loaded
                        if (namespaceUris.Contains(namespaceUri.OriginalString))
                        {
                            continue;
                        }
                        else
                        {
                            List<string> dependentNamespaces = _uacloudLibraryClient.DownloadNodeset(namespaceUri.OriginalString);
                            foreach (string dependentNamespace in dependentNamespaces)
                            {
                                if (!namespaceUris.Contains(dependentNamespace))
                                {
                                    namespaceUris.Add(dependentNamespace);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);
                }
            }

            return namespaceUris;
        }

        private void AddAllNodesFromDownloadedNodesetFiles()
        {
            // we need as many passes as we have downloaded nodeset files to make sure all references can be resolved
            // because the nodeset files may reference each other and we need to load them in the correct order
            string[] nodesetFiles = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "nodesets"));
            if (nodesetFiles.Length > 0)
            {
                for (int i = 0; i < nodesetFiles.Length; i++)
                {
                    foreach (string nodesetFile in nodesetFiles)
                    {
                        using (Stream stream = new FileStream(nodesetFile, FileMode.Open))
                        {
                            UANodeSet nodeSet = UANodeSet.Read(stream);

                            if ((nodeSet.Models?[0]?.ModelUri == _cWotCon) && _wotAssetManagementLoaded)
                            {
                                // skip the WoT asset management nodeset file, we already loaded it
                                continue;
                            }

                            NodeStateCollection predefinedNodes = new NodeStateCollection();

                            nodeSet.Import(SystemContext, predefinedNodes);

                            for (int j = 0; j < predefinedNodes.Count; j++)
                            {
                                try
                                {
                                    AddPredefinedNode(SystemContext, predefinedNodes[j]);
                                }
                                catch (Exception ex)
                                {
                                    Log.Logger.Error(ex.Message, ex);
                                }
                            }
                        }
                    }
                }
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
            if (string.IsNullOrEmpty(inputArguments[0]?.ToString()))
            {
                return StatusCodes.BadInvalidArgument;
            }

            bool success = CreateAssetNode(inputArguments[0].ToString(), out NodeState assetNode);
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
                INodeBrowser browser = _assetManagement.CreateBrowser(
                    SystemContext,
                    null,
                    null,
                    false,
                    BrowseDirection.Forward,
                    null,
                    null,
                    true);

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

            ev.Initialize(SystemContext, null, EventSeverity.Medium, new LocalizedText("Model change"));
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
                NodeState asset = FindPredefinedNode(assetId, typeof(BaseInterfaceState));
                if (asset == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                string assetName = asset.DisplayName.Text;

                _fileManagers.Remove(assetId);

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
                    _tags.Remove(assetName);
                }

                if (_assets.ContainsKey(assetName))
                {
                    _assets.Remove(assetName);
                }

                int i = 0;
                while (i < _uaVariables.Count)
                {
                    if (_uaVariables.Keys.ToArray()[i].StartsWith(assetName + ":"))
                    {
                        _uaVariables.Remove(_uaVariables.Keys.ToArray()[i]);
                    }
                    else
                    {
                        i++;
                    }
                }

                RaiseModelChangedEvent(asset.NodeId, ModelChangeStructureVerbMask.NodeDeleted);

                return ServiceResult.Good;
            }
        }

        public ServiceResult OnDiscoverAssets(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            List<string> allAddresses = new();

            try
            {
                allAddresses.AddRange(new BACNetClient().Discover());
                allAddresses.AddRange(new BeckhoffClient().Discover());
                allAddresses.AddRange(new MitsubishiClient().Discover());
                allAddresses.AddRange(new ModbusTCPClient().Discover());
                allAddresses.AddRange(new RockwellClient().Discover());
                allAddresses.AddRange(new SiemensClient().Discover());
                allAddresses.AddRange(new UAClient().Discover());
                allAddresses.AddRange(new IEC61850Client().Discover());
                allAddresses.AddRange(_lorawanNetworkServer.Discover());
                allAddresses.AddRange(_ocppCentralSystem.Discover());
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return new ServiceResult(StatusCodes.BadTimeout, ex);
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
            bool success = CreateAssetNode(assetName, out NodeState assetNode);
            if (!success)
            {
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated, new Opc.Ua.LocalizedText(assetNode.NodeId.ToString()));
            }
            else
            {
                outputArguments[0] = assetNode.NodeId;

                // generate WoT File contents
                string assetEndpoint = inputArguments[1].ToString();
                ThingDescription td = null;
                if (assetEndpoint.StartsWith("modbus+tcp://"))
                {
                    td = new ModbusTCPClient().BrowseAndGenerateTD(assetName, assetEndpoint);
                }

                if (assetEndpoint.StartsWith("opc.tcp://"))
                {
                    td = new UAClient().BrowseAndGenerateTD(assetName, assetEndpoint);
                }

                if (assetEndpoint.StartsWith("s7://"))
                {
                    td = new SiemensClient().BrowseAndGenerateTD(assetName, assetEndpoint);
                }

                if (assetEndpoint.StartsWith("mcp://"))
                {
                    td = new MitsubishiClient().BrowseAndGenerateTD(assetName, assetEndpoint);
                }

                if (assetEndpoint.StartsWith("eip://"))
                {
                    td = new RockwellClient().BrowseAndGenerateTD(assetName, assetEndpoint);
                }

                if (assetEndpoint.StartsWith("ads://"))
                {
                    td = new BeckhoffClient().BrowseAndGenerateTD(assetName, assetEndpoint);
                }

                if (assetEndpoint.StartsWith("bacnet://"))
                {
                    td = new BACNetClient().BrowseAndGenerateTD(assetName, assetEndpoint);
                }

                if (assetEndpoint.StartsWith("iec61850://"))
                {
                    td = new IEC61850Client().BrowseAndGenerateTD(assetName, assetEndpoint);
                }

                if (assetEndpoint.StartsWith("lorawan://"))
                {
                    td = _lorawanNetworkServer.BrowseAndGenerateTD(assetName, assetEndpoint);
                }

                if (assetEndpoint.StartsWith("ocpp://"))
                {
                    td = _ocppCentralSystem.BrowseAndGenerateTD(assetName, assetEndpoint);
                }

                string contents = JsonConvert.SerializeObject(td);

                _fileManagers[assetNode.NodeId].Write(context, Encoding.UTF8.GetBytes(contents));

                OnboardAssetFromWoTFile(assetNode, contents);

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

            string ipAddress = inputArguments[0].ToString();

            Ping pingSender = new Ping();

            PingReply reply = pingSender.Send(ipAddress);
            if (reply.Status == IPStatus.Success)
            {
                Log.Logger.Information($"Ping to {ipAddress} successful. Roundtrip time: {reply.RoundtripTime} ms.");

                outputArguments[0] = true;
                outputArguments[1] = $"Ping to {ipAddress} successful. Roundtrip time: {reply.RoundtripTime} ms.";

                return ServiceResult.Good;
            }
            else
            {
                Log.Logger.Warning($"Ping to {ipAddress} failed: {reply.Status}");

                outputArguments[0] = false;
                outputArguments[1] = $"Ping to {ipAddress} failed: {reply.Status}";

                return StatusCodes.BadNotFound;
            }
        }

        public void OnboardAssetFromWoTFile(NodeState parent, string contents)
        {
            // parse WoT TD file contents
            ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents);

            List<string> newNamespaceUris = LoadNamespacesFromThingDescription(td);
            List<string> existingNamespaceUris = NamespaceUris.ToList();

            foreach (string ns in newNamespaceUris)
            {
                if (!existingNamespaceUris.Contains(ns))
                {
                    lock (Lock)
                    {
                        existingNamespaceUris.Add(ns);

                        // update the table used by this NodeManager
                        SetNamespaces(existingNamespaceUris.ToArray());

                        // register the new URI with the MasterNodeManager
                        Server.NodeManager.RegisterNamespaceManager(ns, this);
                    }
                }
            }

            AddAllNodesFromDownloadedNodesetFiles();

            byte unitId = 1;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_ASSET_CONNECTION_TEST")))
            {
                AssetConnectionTest(td, out unitId);
            }

            // create nodes for each TD property
            foreach (KeyValuePair<string, Property> property in td.Properties)
            {
                if ((property.Value.Forms != null) && (property.Value.Forms.Length > 0))
                {
                    foreach (object form in property.Value.Forms)
                    {
                        AddNodeForWoTForm(parent, td, property, form, td.Name, unitId);
                    }
                }
            }

            // create nodes for each TD action
            if ((td.Actions != null) && (td.Actions.Count > 0))
            {
                foreach (KeyValuePair<string, TDAction> action in td.Actions)
                {
                    MethodState method = _nodeFactory.CreateMethod(parent, action.Key);
                    method.OnCallMethod = new GenericMethodCalledEventHandler(OnTDActionCalled);

                    if ((action.Value.Input != null) && (action.Value.Input.Count > 0))
                    {
                        // create expanded node IDs for each input argument type
                        ExpandedNodeId[] inputArgumentTypes = new ExpandedNodeId[action.Value.Input.Count];
                        for (int i = 0; i < action.Value.Input.Count; i++)
                        {
                            if (action.Value.Input.Values.ElementAt(i).Type == TypeEnum.String)
                            {
                                inputArgumentTypes[i] = new ExpandedNodeId(DataTypes.String);
                            }
                            else if (action.Value.Input.Values.ElementAt(i).Type == TypeEnum.Number)
                            {
                                inputArgumentTypes[i] = new ExpandedNodeId(DataTypes.Double);
                            }
                            else if (action.Value.Input.Values.ElementAt(i).Type == TypeEnum.Boolean)
                            {
                                inputArgumentTypes[i] = new ExpandedNodeId(DataTypes.Boolean);
                            }
                            else
                            {
                                inputArgumentTypes[i] = new ExpandedNodeId(DataTypes.String); // default to string
                            }
                        }

                        method.InputArguments = _nodeFactory.CreateMethodArguments(method, action.Value.Input.Keys.ToArray(), action.Value.Input.Keys.ToArray(), inputArgumentTypes, true);
                    }

                    if ((action.Value.Output != null) && (action.Value.Output.Count > 0))
                    {
                        // create expanded node IDs for each output argument type
                        ExpandedNodeId[] outputArgumentTypes = new ExpandedNodeId[action.Value.Output.Count];
                        for (int i = 0; i < action.Value.Output.Count; i++)
                        {
                            if (action.Value.Output.Values.ElementAt(i).Type == TypeEnum.String)
                            {
                                outputArgumentTypes[i] = new ExpandedNodeId(DataTypes.String);
                            }
                            else if (action.Value.Output.Values.ElementAt(i).Type == TypeEnum.Number)
                            {
                                outputArgumentTypes[i] = new ExpandedNodeId(DataTypes.Double);
                            }
                            else if (action.Value.Output.Values.ElementAt(i).Type == TypeEnum.Boolean)
                            {
                                outputArgumentTypes[i] = new ExpandedNodeId(DataTypes.Boolean);
                            }
                            else
                            {
                                outputArgumentTypes[i] = new ExpandedNodeId(DataTypes.String); // default to string
                            }
                        }

                        method.OutputArguments = _nodeFactory.CreateMethodArguments(method, action.Value.Output.Keys.ToArray(), action.Value.Output.Keys.ToArray(), outputArgumentTypes, false, true);
                    }

                    parent.AddChild(method);
                    AddPredefinedNode(SystemContext, method);
                }
            }

            _ = Task.Factory.StartNew(ReadAssetTags, td.Name, TaskCreationOptions.LongRunning);

            Log.Logger.Information($"Successfully parsed WoT file for asset: {td.Name}");
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

            string fieldPath = string.Empty;

            // create an OPC UA variable optionally with a specified type.
            if (!string.IsNullOrEmpty(property.Value.OpcUaType))
            {
                string[] opcuaTypeParts = property.Value.OpcUaType.Split(new char[] { '=', ';' });
                if ((opcuaTypeParts.Length > 3) && (opcuaTypeParts[0] == "nsu") && (opcuaTypeParts[2] == "i"))
                {
                    string namespaceURI = opcuaTypeParts[1];
                    uint nodeID = uint.Parse(opcuaTypeParts[3]);

                    if (NamespaceUris.Contains(namespaceURI))
                    {
                        // check if this variable is part of a complex type and we need to load the complex type first and then assign a part of it to the new variable.
                        if (!string.IsNullOrEmpty(property.Value.OpcUaFieldPath))
                        {
                            DataTypeState opcuaType = (DataTypeState)Find(ExpandedNodeId.ToNodeId(ParseExpandedNodeId(property.Value.OpcUaType), Server.NamespaceUris));
                            if ((opcuaType?.DataTypeDefinition?.Body is StructureDefinition) && (((StructureDefinition)opcuaType?.DataTypeDefinition?.Body)?.Fields?.Count > 0))
                            {
                                ExtensionObject complexTypeInstance = new()
                                {
                                    TypeId = opcuaType.NodeId
                                };

                                BinaryEncoder encoder = new(ServiceMessageContext.GlobalContext);
                                foreach (StructureField field in ((StructureDefinition)opcuaType?.DataTypeDefinition?.Body).Fields)
                                {
                                    // check which built-in type the complex type field is. See https://reference.opcfoundation.org/Core/Part6/v104/docs/5.1.2
                                    switch (field.DataType.ToString())
                                    {
                                        case "i=10": encoder.WriteFloat(field.Name, 0); break;
                                        case "i=1": encoder.WriteBoolean(field.Name, false); break;
                                        case "i=6": encoder.WriteInt32(field.Name, 0); break;
                                        case "i=12": encoder.WriteString(field.Name, string.Empty); break;
                                        default: throw new NotImplementedException("Complex type field data type " + field.DataType.ToString() + " not yet supported!");
                                    }

                                    if (field.Name == property.Value.OpcUaFieldPath)
                                    {
                                        // add the field path to make sure we can distinguish the tag during data updates
                                        fieldPath = field.Name;
                                    }
                                }

                                complexTypeInstance.Body = encoder.CloseAndReturnBuffer();

                                // now add it, if it doesn't already exist
                                if (!_uaVariables.ContainsKey(variableId))
                                {
                                    BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, new ExpandedNodeId(new NodeId(nodeID), namespaceURI), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly, complexTypeInstance);
                                    _uaVariables.Add(variableId, variable);
                                    AddPredefinedNode(SystemContext, variable);
                                }
                            }
                            else
                            {
                                // OPC UA type info not found, default to float
                                BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly);
                                _uaVariables.Add(variableId, variable);
                                AddPredefinedNode(SystemContext, variable);
                            }
                        }
                        else
                        {
                            // it's an OPC UA built-in type
                            BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, new ExpandedNodeId(new NodeId(nodeID), namespaceURI), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly);
                            _uaVariables.Add(variableId, variable);
                            AddPredefinedNode(SystemContext, variable);
                        }
                    }
                    else
                    {
                        // no namespace info, default to float
                        BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly);
                        _uaVariables.Add(variableId, variable);
                        AddPredefinedNode(SystemContext, variable);
                    }
                }
                else
                {
                    // can't parse type info, default to float
                    BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly);
                    _uaVariables.Add(variableId, variable);
                    AddPredefinedNode(SystemContext, variable);
                }
            }
            else
            {
                // no type info, default to float
                BaseDataVariableState variable = _nodeFactory.CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly);
                _uaVariables.Add(variableId, variable);
                AddPredefinedNode(SystemContext, variable);
            }

            // check if we need to create a new asset first
            if (!_tags.ContainsKey(assetId))
            {
                _tags.Add(assetId, new List<AssetTag>());
            }

            AddTag(td, form, assetId, unitId, variableId, fieldPath);
        }

        private void AddTag(ThingDescription td, object form, string assetId, byte unitId, string variableId, string fieldPath)
        {
            if (td.Base.ToLower().StartsWith("modbus+tcp://"))
            {
                // create an asset tag and add to our list
                ModbusForm modbusForm = JsonConvert.DeserializeObject<ModbusForm>(form.ToString());
                AssetTag tag = new()
                {
                    Name = variableId,
                    Address = modbusForm.Href,
                    UnitID = unitId,
                    Type = modbusForm.ModbusType.ToString(),
                    PollingInterval = (int)modbusForm.ModbusPollingTime,
                    Entity = modbusForm.ModbusEntity.ToString(),
                    IsBigEndian = modbusForm.MostSignificantByte || modbusForm.MostSignificantWord,
                    SwapPerWord = modbusForm.MostSignificantWord,
                    MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                    MappedUAFieldPath = fieldPath
                };

                _tags[assetId].Add(tag);
            }

            if (td.Base.ToLower().StartsWith("opc.tcp://"))
            {
                // create an asset tag and add to our list
                GenericForm opcuaForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());
                AssetTag tag = new()
                {
                    Name = variableId,
                    Address = opcuaForm.Href,
                    UnitID = unitId,
                    Type = opcuaForm.Type.ToString(),
                    PollingInterval = (int)opcuaForm.PollingTime,
                    MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                    MappedUAFieldPath = fieldPath
                };

                _tags[assetId].Add(tag);
            }

            if (td.Base.ToLower().StartsWith("s7://"))
            {
                // create an asset tag and add to our list
                S7Form s7Form = JsonConvert.DeserializeObject<S7Form>(form.ToString());
                AssetTag tag = new()
                {
                    Name = variableId,
                    Address = s7Form.Href,
                    UnitID = unitId,
                    Type = s7Form.Type.ToString(),
                    PollingInterval = (int)s7Form.PollingTime,
                    MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                    MappedUAFieldPath = fieldPath
                };

                _tags[assetId].Add(tag);
            }

            if (td.Base.ToLower().StartsWith("mcp://"))
            {
                // create an asset tag and add to our list
                GenericForm mitsubishiForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());
                AssetTag tag = new()
                {
                    Name = variableId,
                    Address = mitsubishiForm.Href,
                    UnitID = unitId,
                    Type = mitsubishiForm.Type.ToString(),
                    MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                    MappedUAFieldPath = fieldPath
                };

                _tags[assetId].Add(tag);
            }

            if (td.Base.ToLower().StartsWith("eip://"))
            {
                // create an asset tag and add to our list
                EIPForm eipForm = JsonConvert.DeserializeObject<EIPForm>(form.ToString());
                AssetTag tag = new()
                {
                    Name = variableId,
                    Address = eipForm.Href,
                    UnitID = unitId,
                    Type = eipForm.Type.ToString(),
                    PollingInterval = (int)eipForm.PollingTime,
                    MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                    MappedUAFieldPath = fieldPath
                };

                _tags[assetId].Add(tag);
            }

            if (td.Base.ToLower().StartsWith("ads://"))
            {
                // create an asset tag and add to our list
                GenericForm adsForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());
                AssetTag tag = new()
                {
                    Name = variableId,
                    Address = adsForm.Href,
                    UnitID = unitId,
                    Type = adsForm.Type.ToString(),
                    PollingInterval = (int)adsForm.PollingTime,
                    MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                    MappedUAFieldPath = fieldPath
                };

                _tags[assetId].Add(tag);
            }

            if (td.Base.ToLower().StartsWith("bacnet://"))
            {
                // create an asset tag and add to our list
                GenericForm bacnetForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());
                AssetTag tag = new()
                {
                    Name = variableId,
                    Address = bacnetForm.Href,
                    UnitID = unitId,
                    Type = bacnetForm.Type.ToString(),
                    MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                    MappedUAFieldPath = fieldPath
                };

                _tags[assetId].Add(tag);
            }

            if (td.Base.ToLower().StartsWith("iec61850://"))
            {
                // create an asset tag and add to our list
                GenericForm iec61850Form = JsonConvert.DeserializeObject<GenericForm>(form.ToString());
                AssetTag tag = new()
                {
                    Name = variableId,
                    Address = iec61850Form.Href,
                    UnitID = unitId,
                    Type = iec61850Form.Type.ToString(),
                    MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                    MappedUAFieldPath = fieldPath
                };

                _tags[assetId].Add(tag);
            }

            if (td.Base.ToLower().StartsWith("lorawan://"))
            {
                // create an asset tag and add to our list
                AssetTag tag;
                if (td.Base.ToLower().EndsWith("routerconfig"))
                {
                    tag = new()
                    {
                        Name = variableId,
                        Address = td.Base.ToLower(),
                        UnitID = unitId,
                        Type = TypeString.String.ToString(),
                        MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                        MappedUAFieldPath = fieldPath
                    };
                }
                else
                {
                    LoRaWANForm lorawanForm = JsonConvert.DeserializeObject<LoRaWANForm>(form.ToString());
                    tag = new()
                    {
                        Name = variableId,
                        Address = lorawanForm.Href,
                        UnitID = unitId,
                        Type = lorawanForm.Type.ToString(),
                        IsBigEndian = lorawanForm.MostSignificantByte || lorawanForm.MostSignificantWord,
                        SwapPerWord = lorawanForm.MostSignificantWord,
                        Multiplier = lorawanForm.Multiplier?? 1.0f,
                        BitMask = lorawanForm.BitMask,
                        MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                        MappedUAFieldPath = fieldPath
                    };
                }

                _tags[assetId].Add(tag);
            }

            if (td.Base.ToLower().StartsWith("ocpp://"))
            {
                // create an asset tag and add to our list
                GenericForm ocppForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());
                AssetTag tag = new()
                {
                    Name = variableId,
                    Address = ocppForm.Href,
                    UnitID = unitId,
                    Type = ocppForm.Type.ToString(),
                    MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString(),
                    MappedUAFieldPath = fieldPath
                };

                _tags[assetId].Add(tag);
            }

        }

        private void AssetConnectionTest(ThingDescription td, out byte unitId)
        {
            unitId = 1;
            IAsset assetInterface = null;

            if (td.Base.ToLower().StartsWith("modbus+tcp://"))
            {
                string[] address = td.Base.Split(new char[] { ':', '/' });
                if ((address.Length != 6) || (address[0] != "modbus+tcp"))
                {
                    throw new Exception("Expected Modbus server address in the format modbus+tcp://ipaddress:port/unitID!");
                }

                // check if we can reach the Modbus asset
                unitId = byte.Parse(address[5]);
                ModbusTCPClient client = new();
                client.Connect(address[3], int.Parse(address[4]));

                assetInterface = client;
            }

            if (td.Base.ToLower().StartsWith("opc.tcp://"))
            {
                string[] address = td.Base.Split(new char[] { ':', '/' });
                if ((address.Length != 5) || (address[0] != "opc.tcp"))
                {
                    throw new Exception("Expected OPC UA server address in the format opc.tcp://ipaddress:port!");
                }

                // check if we can reach the OPC UA asset
                UAClient client = new();
                client.Connect(address[3], int.Parse(address[4]));

                assetInterface = client;
            }

            if (td.Base.ToLower().StartsWith("s7://"))
            {
                string[] address = td.Base.Split(new char[] { ':', '/' });
                if ((address.Length != 5) || (address[0] != "s7"))
                {
                    throw new Exception("Expected S7 PLC address in the format s7://ipaddress:port!");
                }

                // check if we can reach the Siemens asset
                SiemensClient client = new();
                client.Connect(address[3], int.Parse(address[4]));

                assetInterface = client;
            }

            if (td.Base.ToLower().StartsWith("mcp://"))
            {
                string[] address = td.Base.Split(new char[] { ':', '/' });
                if ((address.Length != 5) || (address[0] != "mcp"))
                {
                    throw new Exception("Expected Mitsubishi PLC address in the format mcp://ipaddress:port!");
                }

                // check if we can reach the Mitsubishi asset
                MitsubishiClient client = new();
                client.Connect(address[3], int.Parse(address[4]));

                assetInterface = client;
            }

            if (td.Base.ToLower().StartsWith("eip://"))
            {
                string[] address = td.Base.Split(new char[] { ':', '/' });
                if ((address.Length != 4) || (address[0] != "eip"))
                {
                    throw new Exception("Expected Rockwell PLC address in the format eip://ipaddress!");
                }

                // check if we can reach the Ethernet/IP asset
                RockwellClient client = new();
                client.Connect(address[3], 0);

                assetInterface = client;
            }

            if (td.Base.ToLower().StartsWith("ads://"))
            {
                string[] address = td.Base.Split(new char[] { ':', '/' });
                if ((address.Length != 6) || (address[0] != "ads"))
                {
                    throw new Exception("Expected Beckhoff PLC address in the format ads://ipaddress:port!");
                }

                // check if we can reach the Beckhoff asset
                BeckhoffClient client = new();
                client.Connect(address[3] + ":" + address[4], int.Parse(address[5]));

                assetInterface = client;
            }

            if (td.Base.ToLower().StartsWith("bacnet://"))
            {
                string[] address = td.Base.Split(new char[] { ':', '/' });
                if ((address.Length != 5) || (address[0] != "bacnet"))
                {
                    throw new Exception("Expected BACNet device address in the format bacnet://ipaddress/deviceId!");
                }

                // check if we can reach the BACNet asset
                BACNetClient client = new();
                client.Connect(address[3] + "/" + address[4], 0);

                assetInterface = client;
            }

            if (td.Base.ToLower().StartsWith("iec61850://"))
            {
                string[] address = td.Base.Split(new char[] { ':', '/' });
                if ((address.Length != 6) || (address[0] != "iec61850"))
                {
                    throw new Exception("Expected IEC61850 device address in the format iec61850://ipaddress:port!");
                }

                // check if we can reach the IEC61850 asset
                IEC61850Client client = new();
                client.Connect(address[3] + ":" + address[4], int.Parse(address[5]));

                assetInterface = client;
            }

            if (td.Base.ToLower().StartsWith("lorawan://"))
            {
                string[] address = td.Base.Split(new char[] { ':', '/' });
                if ((address.Length != 6) || (address[0] != "lorawan"))
                {
                    throw new Exception("Expected LoRaWAN address in the format lorawan://deviceeui/appkey/device or lorawan://deviceeui/gatewaymodel/routerconfig!");
                }

                _lorawanNetworkServer.Connect(td.Base, 0);

                assetInterface = _lorawanNetworkServer;
            }

            if (td.Base.ToLower().StartsWith("ocpp://"))
            {
                string[] address = td.Base.Split(new char[] { ':', '/' });
                if ((address.Length != 4) || (address[0] != "ocpp"))
                {
                    throw new Exception("Expected OCPP Gateway address in the format ocpp://assetname!");
                }

                // in the case of OCPP, we don't check if we can reach the gateway as the gateway needs to contact us during onboarding
                assetInterface = _ocppCentralSystem;
            }

            _assets.Add(td.Name, assetInterface);
        }

        private ExpandedNodeId ParseExpandedNodeId(string nodeString)
        {
            if (!string.IsNullOrEmpty(nodeString))
            {
                string[] parentNodeDetails = nodeString.Split('=', ';');
                if (parentNodeDetails.Length > 3 && parentNodeDetails[0] == "nsu" && parentNodeDetails[2] == "i")
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

        public ServiceResult OnReadValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
        {
            bool provisioningMode = (Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "certs")).Count() == 0);
            if (provisioningMode && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE")))
            {
                return new ServiceResult(StatusCodes.BadNotReadable, "Access to UA Edge Translator is limited while in provisioning mode!");
            }

            PropertyState property = node as PropertyState;
            if (property != null)
            {
                if (node.DisplayName.Text == "SupportedWoTBindings")
                {
                    value = new string[10] {
                        "https://www.w3.org/2019/wot/modbus",
                        "https://www.w3.org/2019/wot/opcua",
                        "https://www.w3.org/2019/wot/s7",
                        "https://www.w3.org/2019/wot/mcp",
                        "https://www.w3.org/2019/wot/eip",
                        "https://www.w3.org/2019/wot/ads",
                        "https://www.w3.org/2019/wot/iec61850",
                        "http://www.w3.org/2022/bacnet",
                        "https://www.w3.org/2019/wot/lorawan",
                        "https://www.w3.org/2019/wot/ocpp"
                    };

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
                    value = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                    timestamp = DateTime.UtcNow;
                    statusCode = StatusCodes.Good;

                    return ServiceResult.Good;
                }

                foreach (KeyValuePair<string, List<AssetTag>> tags in _tags)
                {
                    string assetId = tags.Key;

                    foreach (AssetTag tag in tags.Value)
                    {
                        try
                        {
                            if (tag.MappedUAExpandedNodeID.ToString() == NodeId.ToExpandedNodeId(variable.NodeId, context.NamespaceUris).ToString())
                            {
                                value = _uaVariables[tag.Name].Value;
                                timestamp = _uaVariables[tag.Name].Timestamp;
                                statusCode = StatusCodes.Good;

                                return ServiceResult.Good;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Error(ex.Message, ex);

                            return new ServiceResult(ex);
                        }
                    }
                }
            }

            return ServiceResult.Good;
        }

        public ServiceResult OnWriteValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
        {
            bool provisioningMode = (Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "certs")).Count() == 0);
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

                if (node.DisplayName.Text == "SupportedOPCUAInfoModels")
                {
                    statusCode = StatusCodes.Good;

                    return ServiceResult.Good;
                }

                if (node.DisplayName.Text == "License")
                {
                    // validate license key provided
                    if (string.IsNullOrEmpty(value.ToString()))
                    {
                        return new ServiceResult(StatusCodes.BadInvalidArgument, "License key cannot be empty!");
                    }

                    if (value.ToString() != "themajiclicensekey")
                    {
                        // in a commercial product, you would validate the license key here using some algorithm
                        // and only when the key is valid switch out of provisioning mode
                        return new ServiceResult(StatusCodes.BadInvalidArgument, "Invalid license key!");
                    }

                    _uaVariables["License"].Value = value;
                    _uaVariables["License"].Timestamp = DateTime.UtcNow;
                    _uaVariables["License"].ClearChangeMasks(SystemContext, false);
                    statusCode = StatusCodes.Good;

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

                // find the tag that matches the variable node ID
                foreach (KeyValuePair<string, List<AssetTag>> tags in _tags)
                {
                    string assetId = tags.Key;

                    foreach (AssetTag tag in tags.Value)
                    {
                        try
                        {
                            if (tag.MappedUAExpandedNodeID.ToString() == NodeId.ToExpandedNodeId(variable.NodeId, context.NamespaceUris).ToString())
                            {
                                _assets[assetId].Write(tag, value.ToString());

                                _uaVariables[tag.Name].Value = value;
                                _uaVariables[tag.Name].Timestamp = DateTime.UtcNow;
                                _uaVariables[tag.Name].ClearChangeMasks(SystemContext, false);
                                statusCode = StatusCodes.Good;

                                return ServiceResult.Good;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Error(ex.Message, ex);

                            return new ServiceResult(ex);
                        }
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
                string[] actionInputArgs = inputArguments.Select(arg => arg?.ToString()).ToArray();
                string[] actionOutputArgs = outputArguments.Select(arg => arg?.ToString()).ToArray();

                string result = _assets[assetId].ExecuteAction(method, actionInputArgs, ref actionOutputArgs);

                outputArguments = actionOutputArgs;

                return new ServiceResult(StatusCodes.Good, new LocalizedText(result));
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return new ServiceResult(ex);
            }
        }

        private void ReadAssetTags(object assetNameObject)
        {
            bool assetDeleted = false;
            while (!_shutdown && !assetDeleted)
            {
                Thread.Sleep(1000);

                _ticks++;

                string assetId = (string)assetNameObject;
                if (string.IsNullOrEmpty(assetId) || !_tags.ContainsKey(assetId) || !_assets.ContainsKey(assetId))
                {
                    assetDeleted = true;
                    continue;
                }

                foreach (AssetTag tag in _tags[assetId])
                {
                    try
                    {
                        if (_ticks * 1000 % tag.PollingInterval == 0)
                        {
                            UpdateUAServerVariable(tag, _assets[assetId].Read(tag));
                        }
                    }
                    catch (Exception ex)
                    {
                        // skip this tag, but log an error
                        Log.Logger.Error(ex.Message, ex);

                        // try reconnecting
                        try
                        {
                            string[] remoteEndpoint = _assets[assetId].GetRemoteEndpoint().Split(':');
                            _assets[assetId].Disconnect();
                            _assets[assetId].Connect(remoteEndpoint[0], int.Parse(remoteEndpoint[1]));
                        }
                        catch (Exception)
                        {
                            // do nothing
                        }
                    }
                }
            }
        }

        private void UpdateUAServerVariable(AssetTag tag, object value)
        {
            // check for complex type
            if (_uaVariables[tag.Name].Value is ExtensionObject)
            {
                // decode existing values and re-encode them with our updated value
                BinaryDecoder decoder = new((byte[])((ExtensionObject)_uaVariables[tag.Name].Value).Body, ServiceMessageContext.GlobalContext);
                BinaryEncoder encoder = new(ServiceMessageContext.GlobalContext);

                DataTypeState opcuaType = (DataTypeState)Find(_uaVariables[tag.Name].DataType);
                if ((opcuaType != null) && ((StructureDefinition)opcuaType?.DataTypeDefinition?.Body).Fields?.Count > 0)
                {
                    foreach (StructureField field in ((StructureDefinition)opcuaType?.DataTypeDefinition?.Body).Fields)
                    {
                        // check which built-in type the complex type field is. See https://reference.opcfoundation.org/Core/Part6/v104/docs/5.1.2
                        switch (field.DataType.ToString())
                        {
                            case "i=10":
                                {
                                    float newValue = decoder.ReadFloat(field.Name);

                                    if (field.Name == tag.MappedUAFieldPath)
                                    {
                                        // overwrite existing value with our updated value
                                        newValue = (float)value;
                                    }

                                    encoder.WriteFloat(field.Name, newValue);

                                    break;
                                }
                            case "i=1":
                                {
                                    bool newValue = decoder.ReadBoolean(field.Name);

                                    if (field.Name == tag.MappedUAFieldPath)
                                    {
                                        // overwrite existing value with our updated value
                                        newValue = (bool)value;
                                    }

                                    encoder.WriteBoolean(field.Name, newValue);

                                    break;
                                }
                            case "i=6":
                                {
                                    int newValue = decoder.ReadInt32(field.Name);

                                    if (field.Name == tag.MappedUAFieldPath)
                                    {
                                        // overwrite existing value with our updated value
                                        newValue = (int)value;
                                    }

                                    encoder.WriteInt32(field.Name, newValue);

                                    break;
                                }
                            case "i=12":
                                {
                                    string newValue = decoder.ReadString(field.Name);

                                    if (field.Name == tag.MappedUAFieldPath)
                                    {
                                        // overwrite existing value with our updated value
                                        newValue = (string)value;
                                    }

                                    encoder.WriteString(field.Name, newValue);

                                    break;
                                }
                            default: throw new NotImplementedException("Complex type field data type " + field.DataType.ToString() + " not yet supported!");
                        }
                    }

                    ((ExtensionObject)_uaVariables[tag.Name].Value).Body = encoder.CloseAndReturnBuffer();
                }
            }
            else
            {
                _uaVariables[tag.Name].Value = value;
            }

            _uaVariables[tag.Name].Timestamp = DateTime.UtcNow;
            _uaVariables[tag.Name].ClearChangeMasks(SystemContext, false);
        }
    }
}
