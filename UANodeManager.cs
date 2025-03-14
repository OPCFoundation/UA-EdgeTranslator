
namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.NetworkInformation;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using static System.Net.WebRequestMethods;
    using UANodeSet = Export.UANodeSet;

    public class UANodeManager : CustomNodeManager2
    {
        private long _lastUsedId = 0;

        private bool _shutdown = false;

        private BaseObjectState _assetManagement;

        private readonly Dictionary<string, BaseDataVariableState> _uaVariables = new();

        private readonly Dictionary<string, PropertyState> _uaProperties = new();

        private readonly Dictionary<string, IAsset> _assets = new();

        private readonly Dictionary<string, List<AssetTag>> _tags = new();

        private readonly UACloudLibraryClient _uacloudLibraryClient = new();

        private readonly Dictionary<NodeId, FileManager> _fileManagers = new();

        private uint _ticks = 0;

        private const string _cWotCon = "http://opcfoundation.org/UA/WoT-Con/";

        private const uint _cHasWoTComponent = 83;

        private const uint _cWoTAssetConnectionManagementType = 38;

        private const uint _cWoTAssetConfigurationType = 113;

        private const uint _cIWoTAssetType = 56;

        private const uint _cWoTAssetFileType = 86;

        public UANodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            // create our settings folder, if required
            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "settings")))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "settings"));
            }

            List<string> namespaceUris = new()
            {
                "http://opcfoundation.org/UA/EdgeTranslator/"
            };

            // log into UA Cloud Library and download available namespaces
            _uacloudLibraryClient.Login(Environment.GetEnvironmentVariable("UACLURL"), Environment.GetEnvironmentVariable("UACLUsername"), Environment.GetEnvironmentVariable("UACLPassword"));

            namespaceUris.AddRange(LoadNamespaceUrisFromNodesetXml("Opc.Ua.WotCon.NodeSet2.xml"));

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

                AddNodesFromNodesetXml("Opc.Ua.WotCon.NodeSet2.xml");

                AddNodesForAssetManagement(objectsFolderReferences);

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

                AddReverseReferences(externalReferences);
                base.CreateAddressSpace(externalReferences);
            }
        }

        private void AddNodesForAssetManagement(IList<IReference> objectsFolderReferences)
        {
            ushort WoTConNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex(_cWotCon);

            _assetManagement = CreateObject(
                FindNodeInAddressSpace(ObjectIds.ObjectsFolder),
                "WoTAssetConnectionManagement",
                new ExpandedNodeId(_cWoTAssetConnectionManagementType, _cWotCon));

            MethodState createAsset = CreateMethod(_assetManagement, "CreateAsset");
            createAsset.OnCallMethod = new GenericMethodCalledEventHandler(OnCreateAsset);
            createAsset.InputArguments = AddArguments(createAsset, ["AssetName"], ["A unique name for the asset."], new ExpandedNodeId(DataTypes.String), true);
            createAsset.OutputArguments = AddArguments(createAsset, ["AssetId"], ["The NodeId of the WoTAsset object, if call was successful."], new ExpandedNodeId(DataTypes.NodeId), false);
            AddPredefinedNode(SystemContext, createAsset);

            MethodState deleteAsset = CreateMethod(_assetManagement, "DeleteAsset");
            deleteAsset.OnCallMethod = new GenericMethodCalledEventHandler(OnDeleteAsset);
            deleteAsset.InputArguments = AddArguments(deleteAsset, ["AssetId"], ["The NodeId of the WoTAsset object."], new ExpandedNodeId(DataTypes.NodeId), true);
            AddPredefinedNode(SystemContext, deleteAsset);

            MethodState discoverAssets = CreateMethod(_assetManagement, "DiscoverAssets");
            discoverAssets.OnCallMethod = new GenericMethodCalledEventHandler(OnDiscoverAssets);
            discoverAssets.OutputArguments = AddArguments(discoverAssets, ["AssetEndpoints"], ["The list of discovered asset endpoints."], new ExpandedNodeId(DataTypes.String), false, true);
            AddPredefinedNode(SystemContext, discoverAssets);

            MethodState createAssetForEndpoint = CreateMethod(_assetManagement, "CreateAssetForEndpoint");
            createAssetForEndpoint.OnCallMethod = new GenericMethodCalledEventHandler(OnCreateAssetForEndpoint);
            createAssetForEndpoint.InputArguments = AddArguments(createAssetForEndpoint, ["AssetName", "AssetEndpoint"], ["The name to be assigned to the asset.", "The endpoint to the asset on the network."], new ExpandedNodeId(DataTypes.String), true);
            createAssetForEndpoint.OutputArguments = AddArguments(createAssetForEndpoint, ["AssetId"], ["The NodeId of the WoTAsset object, if call was successful."], new ExpandedNodeId(DataTypes.NodeId), false);
            AddPredefinedNode(SystemContext, createAssetForEndpoint);

            MethodState connectionTest = CreateMethod(_assetManagement, "ConnectionTest");
            connectionTest.OnCallMethod = new GenericMethodCalledEventHandler(OnConnectionTest);
            connectionTest.InputArguments = AddArguments(connectionTest, ["AssetEndpoint"], ["The endpoint description of the asset to test the connection to."], new ExpandedNodeId(DataTypes.String), true);
            connectionTest.OutputArguments = AddArguments(connectionTest, ["Success", "Status"], ["Returns TRUE if a connection could be established to the asset.", "If a connection was established successfully, an asset-specific status code string describing the current health of the asset is returned."], new ExpandedNodeId(DataTypes.String), false);
            AddPredefinedNode(SystemContext, connectionTest);

            // create a property listing our supported WoT protocol bindings
            _uaProperties.Add("SupportedWoTBindings", CreateProperty(_assetManagement, "SupportedWoTBindings", new ExpandedNodeId(DataTypes.UriString), WoTConNamespaceIndex, false, new string[7] {
                "https://www.w3.org/2019/wot/modbus",
                "https://www.w3.org/2019/wot/opcua",
                "https://www.w3.org/2019/wot/s7",
                "https://www.w3.org/2019/wot/mcp",
                "https://www.w3.org/2019/wot/eip",
                "https://www.w3.org/2019/wot/ads",
                "http://www.w3.org/2022/bacnet"
            }));

            BaseObjectState configuration = CreateObject(
                _assetManagement,
                "Configuration",
                new ExpandedNodeId(_cWoTAssetConfigurationType, _cWotCon));

            // create a property for the license key
            _uaProperties.Add("License", CreateProperty(configuration, "License", new ExpandedNodeId(DataTypes.String), WoTConNamespaceIndex, true, string.Empty));
        }

        private List<string> LoadNamespacesFromThingDescription(ThingDescription td)
        {
            List<string> namespaceUris = new() {
                "http://opcfoundation.org/UA/" + td.Name + "/"
            };

            // check if an OPC UA companion spec is mentioned in the WoT TD file
            foreach (object ns in td.Context)
            {
                try
                {
                    if (ns.ToString().Contains("https://www.w3.org/") && !ns.ToString().Contains("opcua"))
                    {
                        continue;
                    }

                    Uri namespaceUri = new(((JValue)((JProperty)((JContainer)ns).First).Value).Value.ToString());
                    if (ns != null)
                    {
                        // support local Nodesets
                        if (!namespaceUri.IsAbsoluteUri || (!namespaceUri.AbsoluteUri.Contains("http://") && !namespaceUri.AbsoluteUri.Contains("https://")))
                        {
                            string nodesetFile = string.Empty;
                            if (Path.IsPathFullyQualified(namespaceUri.OriginalString))
                            {
                                // absolute file path
                                nodesetFile = namespaceUri.OriginalString;
                            }
                            else
                            {
                                // relative file path
                                nodesetFile = Path.Combine(Directory.GetCurrentDirectory(), namespaceUri.OriginalString);
                            }

                            Log.Logger.Information("Loading nodeset from local file: " + nodesetFile);
                            namespaceUris.AddRange(LoadNamespaceUrisFromNodesetXml(nodesetFile));
                        }
                        else
                        {
                            if (_uacloudLibraryClient.DownloadNamespace(Environment.GetEnvironmentVariable("UACLURL"), namespaceUri.OriginalString))
                            {
                                Log.Logger.Information("Loaded nodeset from Cloud Library URL: " + namespaceUri);

                                foreach (string nodesetFile in _uacloudLibraryClient._nodeSetFilenames)
                                {
                                    namespaceUris.AddRange(LoadNamespaceUrisFromNodesetXml(nodesetFile));
                                }
                            }
                            else
                            {
                                Log.Logger.Warning($"Could not load nodeset {namespaceUri.OriginalString}");
                            }
                        }
                    }

                    string validationError = _uacloudLibraryClient.ValidateNamespacesAndModels(Environment.GetEnvironmentVariable("UACLURL"), true);
                    if (!string.IsNullOrEmpty(validationError))
                    {
                        Log.Logger.Error(validationError);
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);
                }
            }

            return namespaceUris;
        }

        private void AddNodesFromNodesetXml(ThingDescription td)
        {
            // we need as many passes as we have nodesetfiles to make sure all references can be resolved
            for (int i = 0; i < _uacloudLibraryClient._nodeSetFilenames.Count; i++)
            {
                foreach (string nodesetFile in _uacloudLibraryClient._nodeSetFilenames)
                {
                    AddNodesFromNodesetXml(nodesetFile);
                }
            }

            foreach (object ns in td.Context)
            {
                try
                {
                    if (ns.ToString().Contains("https://www.w3.org/") && !ns.ToString().Contains("opcua"))
                    {
                        continue;
                    }

                    Uri namespaceUri = new(((JValue)((JProperty)((JContainer)ns).First).Value).Value.ToString());
                    if (namespaceUri != null)
                    {
                        // support local Nodesets
                        if (!namespaceUri.IsAbsoluteUri || (!namespaceUri.AbsoluteUri.Contains("http://") && !namespaceUri.AbsoluteUri.Contains("https://")))
                        {
                            string nodesetFile = string.Empty;
                            if (Path.IsPathFullyQualified(namespaceUri.OriginalString))
                            {
                                // absolute file path
                                nodesetFile = namespaceUri.OriginalString;
                            }
                            else
                            {
                                // relative file path
                                nodesetFile = Path.Combine(Directory.GetCurrentDirectory(), namespaceUri.OriginalString);
                            }

                            Log.Logger.Information("Adding node set from local nodeset file");
                            AddNodesFromNodesetXml(nodesetFile);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);
                }
            }
        }

        private List<string> LoadNamespaceUrisFromNodesetXml(string nodesetFile)
        {
            List<string> namespaceUris = new();

            using (FileStream stream = new(nodesetFile, FileMode.Open, FileAccess.Read))
            {
                UANodeSet nodeSet = UANodeSet.Read(stream);

                if ((nodeSet.NamespaceUris != null) && (nodeSet.NamespaceUris.Length > 0))
                {
                    foreach (string ns in nodeSet.NamespaceUris)
                    {
                        if (!namespaceUris.Contains(ns))
                        {
                            namespaceUris.Add(ns);
                        }
                    }
                }
            }

            return namespaceUris;
        }

        private void AddNodesFromNodesetXml(string nodesetFile)
        {
            using (Stream stream = new FileStream(nodesetFile, FileMode.Open))
            {
                UANodeSet nodeSet = UANodeSet.Read(stream);

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
                        Log.Logger.Error(ex.Message, ex);
                    }
                }
            }
        }

        private MethodState CreateMethod(NodeState parent, string name)
        {
            MethodState method = new(parent) {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(name, NamespaceIndex),
                BrowseName = new QualifiedName(name, NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                Executable = true,
                UserExecutable = true
            };

            if (parent != null)
            {
                parent.AddChild(method);
            }

            return method;
        }

        private PropertyState<Argument[]> AddArguments(MethodState methodState, string[] names, string[] descriptions, ExpandedNodeId type, bool input, bool array = false)
        {
            string browseName = methodState.BrowseName.Name;
            if (input)
            {
                browseName += "InArgs";
            }
            else
            {
                browseName += "OutArgs";
            }

            List<Argument> argumentsList = new();
            for (int i = 0; i < names.Length; i++)
            {
                argumentsList.Add(new Argument()
                {
                    Name = names[i],
                    Description = descriptions[i],
                    DataType = ExpandedNodeId.ToNodeId(type, Server.NamespaceUris),
                    ValueRank = array ? ValueRanks.OneDimension : ValueRanks.Scalar
                });
            }

            PropertyState<Argument[]> arguments = new(methodState)
            {
                NodeId = new NodeId(browseName, NamespaceIndex),
                BrowseName = input ? BrowseNames.InputArguments : BrowseNames.OutputArguments,
                DisplayName = input ? BrowseNames.InputArguments : BrowseNames.OutputArguments,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                DataType = DataTypeIds.Argument,
                ValueRank = ValueRanks.OneDimension,
                Value = argumentsList.ToArray()
            };

            return arguments;
        }

        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                base.DeleteAddressSpace();
            }
        }

        private ServiceResult OnCreateAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (string.IsNullOrEmpty(inputArguments[0]?.ToString()))
            {
                return StatusCodes.BadInvalidArgument;
            }

            bool success = CreateAssetNode(inputArguments[0].ToString(), out NodeState assetNode);
            if (!success)
            {
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated, new LocalizedText(assetNode.NodeId.ToString()));
            }
            else
            {
                outputArguments[0] = assetNode.NodeId;
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

        private ServiceResult OnDeleteAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
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

                return ServiceResult.Good;
            }
        }

        private ServiceResult OnDiscoverAssets(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
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
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated, new LocalizedText(assetNode.NodeId.ToString()));
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

                string contents = JsonConvert.SerializeObject(td);

                _fileManagers[assetNode.NodeId].Write(context, Encoding.UTF8.GetBytes(contents));

                OnboardAssetFromWoTFile(assetNode, contents);

                System.IO.File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings", assetName + ".td.jsonld"), contents);

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

            AddNodesFromNodesetXml(td);

            byte unitId = 1;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_ASSET_CONNECTION_TEST")))
            {
                AssetConnectionTest(td, out unitId);
            }

            // create nodes for each TD property
            foreach (KeyValuePair<string, Property> property in td.Properties)
            {
                foreach (object form in property.Value.Forms)
                {
                    AddNodeForWoTForm(parent, td, property, form, td.Name, unitId);
                }
            }

            _ = Task.Factory.StartNew(UpdateNodeValues, td.Name, TaskCreationOptions.LongRunning);

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
                                    _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(new NodeId(nodeID), namespaceURI), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly, complexTypeInstance));
                                }
                            }
                            else
                            {
                                // OPC UA type info not found, default to float
                                _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly));
                            }
                        }
                        else
                        {
                            // it's an OPC UA built-in type
                            _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(new NodeId(nodeID), namespaceURI), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly));
                        }
                    }
                    else
                    {
                        // no namespace info, default to float
                        _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly));
                    }
                }
                else
                {
                    // can't parse type info, default to float
                    _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly));
                }
            }
            else
            {
                // no type info, default to float
                _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex, !property.Value.ReadOnly));
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
                    Entity = null,
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
                    Entity = null,
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
                    PollingInterval = 1000,
                    Entity = null,
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
                    Entity = null,
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
                    Entity = null,
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
                    PollingInterval = 1000,
                    Entity = null,
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

        public BaseObjectState CreateObject(NodeState parent, string name, ExpandedNodeId type)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type.ToString()))
            {
                throw new ArgumentNullException("Cannot create UA object with empty browsename or type definition!");
            }

            BaseObjectState obj = new(parent)
            {
                BrowseName = name,
                DisplayName = name,
                TypeDefinitionId = ExpandedNodeId.ToNodeId(type, Server.NamespaceUris)
            };

            obj.NodeId = New(SystemContext, obj);

            AddPredefinedNode(SystemContext, obj);

            if (parent != null)
            {
                parent.AddChild(obj);
            }

            return obj;
        }

        private BaseDataVariableState CreateVariable(NodeState parent, string name, ExpandedNodeId type, ushort namespaceIndex, bool writeable = false, object value = null)
        {
            BaseDataVariableState variable = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseVariableType,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                AccessLevel = AccessLevels.CurrentRead,
                DataType = ExpandedNodeId.ToNodeId(type, Server.NamespaceUris),
                Value = value,
                OnReadValue = OnReadValue
            };

            if (writeable)
            {
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserWriteMask = AttributeWriteMask.ValueForVariableType;
                variable.WriteMask = AttributeWriteMask.ValueForVariableType;
                variable.OnWriteValue = OnWriteValue;
            }

            parent?.AddChild(variable);

            parent?.AddReference(ExpandedNodeId.ToNodeId(new ExpandedNodeId(_cHasWoTComponent, _cWotCon), Server.NamespaceUris), false, variable.NodeId);

            AddPredefinedNode(SystemContext, variable);

            return variable;
        }

        private PropertyState CreateProperty(NodeState parent, string name, ExpandedNodeId type, ushort namespaceIndex, bool writeable = false, object value = null)
        {
            PropertyState property = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                AccessLevel = AccessLevels.CurrentRead,
                DataType = ExpandedNodeId.ToNodeId(type, Server.NamespaceUris),
                Value = value,
                OnReadValue = OnReadValue
            };

            if (writeable)
            {
                property.AccessLevel = AccessLevels.CurrentReadOrWrite;
                property.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                property.UserWriteMask = AttributeWriteMask.ValueForVariableType;
                property.WriteMask = AttributeWriteMask.ValueForVariableType;
                property.OnWriteValue = OnWriteValue;
            }

            parent?.AddChild(property);

            AddPredefinedNode(SystemContext, property);

            return property;
        }

        private ServiceResult OnReadValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
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
                    value = _uaProperties[node.DisplayName.Text].Value;
                    timestamp = _uaProperties[node.DisplayName.Text].Timestamp;
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
            if (variable == null)
            {

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

        private ServiceResult OnWriteValue(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
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

                    return ServiceResult.Good;
                }
            }

            BaseDataVariableState variable = node as BaseDataVariableState;
            if (variable == null)
            {
                foreach (KeyValuePair<string, List<AssetTag>> tags in _tags)
                {
                    string assetId = tags.Key;

                    foreach (AssetTag tag in tags.Value)
                    {
                        try
                        {
                            if (tag.MappedUAExpandedNodeID.ToString() == NodeId.ToExpandedNodeId(variable.NodeId, context.NamespaceUris).ToString())
                            {
                                if (_assets[assetId] is ModbusTCPClient)
                                {
                                    HandleModbusDataWrite(tag, assetId, value.ToString());
                                }

                                if (_assets[assetId] is UAClient)
                                {
                                    HandleOPCUADataWrite(tag, assetId, value.ToString());
                                }

                                if (_assets[assetId] is SiemensClient)
                                {
                                    HandleSiemensDataWrite(tag, assetId, value.ToString());
                                }

                                if (_assets[assetId] is MitsubishiClient)
                                {
                                    HandleMitsubishiDataWrite(tag, assetId, value.ToString());
                                }

                                if (_assets[assetId] is RockwellClient)
                                {
                                    HandleRockwellDataWrite(tag, assetId, value.ToString());
                                }

                                if (_assets[assetId] is BeckhoffClient)
                                {
                                    HandleBeckhoffDataWrite(tag, assetId, value.ToString());
                                }

                                if (_assets[assetId] is BACNetClient)
                                {
                                    HandleBACNetDataWrite(tag, assetId, value.ToString());
                                }

                                _uaVariables[tag.Name].Value = value;
                                _uaVariables[tag.Name].Timestamp = DateTime.UtcNow;
                                _uaVariables[tag.Name].ClearChangeMasks(SystemContext, false);

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

        private void UpdateNodeValues(object assetNameObject)
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
                            if (_assets[assetId] is ModbusTCPClient)
                            {
                                HandleModbusDataRead(tag, assetId);
                            }

                            if (_assets[assetId] is UAClient)
                            {
                                HandleOPCUADataRead(tag, assetId);
                            }

                            if (_assets[assetId] is SiemensClient)
                            {
                                HandleSiemensDataRead(tag, assetId);
                            }

                            if (_assets[assetId] is MitsubishiClient)
                            {
                                HandleMitsubishiDataRead(tag, assetId);
                            }

                            if (_assets[assetId] is RockwellClient)
                            {
                                HandleRockwellDataRead(tag, assetId);
                            }

                            if (_assets[assetId] is BeckhoffClient)
                            {
                                HandleBeckhoffDataRead(tag, assetId);
                            }

                            if (_assets[assetId] is BACNetClient)
                            {
                                HandleBACNetDataRead(tag, assetId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // skip this tag, but log an error
                        Log.Logger.Error(ex.Message, ex);
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
                                        // overwrite existing value with our upated value
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
                                        // overwrite existing value with our upated value
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
                                        // overwrite existing value with our upated value
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
                                        // overwrite existing value with our upated value
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

        private void HandleModbusDataRead(AssetTag tag, string assetId)
        {
            ModbusTCPClient.FunctionCode functionCode = ModbusTCPClient.FunctionCode.ReadCoilStatus;
            if (tag.Entity == "HoldingRegister")
            {
                functionCode = ModbusTCPClient.FunctionCode.ReadHoldingRegisters;
            }

            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if ((addressParts.Length == 3) && (addressParts[1] == "quantity"))
            {
                byte[] tagBytes = null;
                try
                {
                    ushort quantity = ushort.Parse(addressParts[2]);
                    tagBytes = _assets[assetId].Read(addressParts[0], tag.UnitID, functionCode.ToString(), quantity).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);

                    // try reconnecting
                    string[] remoteEndpoint = _assets[assetId].GetRemoteEndpoint().Split(':');
                    _assets[assetId].Disconnect();
                    _assets[assetId].Connect(remoteEndpoint[0], int.Parse(remoteEndpoint[1]));
                }

                if ((tagBytes != null) && (tagBytes.Length > 0))
                {
                    object value = null;
                    if (tag.Type == "Float")
                    {
                        value = BitConverter.ToSingle(ByteSwapper.Swap(tagBytes));
                    }
                    else if  (tag.Type == "Boolean")
                    {
                        value = BitConverter.ToBoolean(tagBytes);
                    }
                    else if (tag.Type == "Integer")
                    {
                        value = BitConverter.ToInt32(ByteSwapper.Swap(tagBytes));
                    }
                    else if (tag.Type == "String")
                    {
                        value = Encoding.UTF8.GetString(tagBytes);
                    }
                    else
                    {
                        throw new ArgumentException("Type not supported by Modbus.");
                    }

                    UpdateUAServerVariable(tag, value);
                }
            }
        }

        private void HandleModbusDataWrite(AssetTag tag, string assetId, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            ushort quantity = ushort.Parse(addressParts[2]);
            byte[] tagBytes = null;

            if ((tag.Type == "Float") && (quantity == 2))
            {
                tagBytes = ByteSwapper.Swap(BitConverter.GetBytes(float.Parse(value)));
            }
            else if ((tag.Type == "Boolean") && (quantity == 1))
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if ((tag.Type == "Integer") && (quantity == 2))
            {
                tagBytes = ByteSwapper.Swap(BitConverter.GetBytes(int.Parse(value)));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by Modbus.");
            }

            _assets[assetId].Write(addressParts[0], tag.UnitID, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }

        private void HandleOPCUADataRead(AssetTag tag, string assetId)
        {
            byte[] tagBytes = null;
            try
            {
                tagBytes = _assets[assetId].Read(tag.Address, 0, null, 0).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);

                // try reconnecting
                string[] remoteEndpoint = _assets[assetId].GetRemoteEndpoint().Split(':');
                _assets[assetId].Disconnect();
                _assets[assetId].Connect(remoteEndpoint[0], int.Parse(remoteEndpoint[1]));
            }

            if ((tagBytes != null) && (tagBytes.Length > 0))
            {
                object value = null;
                if (tag.Type == "Float")
                {
                    value = BitConverter.ToSingle(tagBytes);
                }
                else if (tag.Type == "Boolean")
                {
                    value = BitConverter.ToBoolean(tagBytes);
                }
                else if (tag.Type == "Integer")
                {
                    value = BitConverter.ToInt32(tagBytes);
                }
                else if (tag.Type == "String")
                {
                    value = Encoding.UTF8.GetString(tagBytes);
                }
                else
                {
                    throw new ArgumentException("Type not supported by OPC UA.");
                }

                UpdateUAServerVariable(tag, value);
            }
        }

        private void HandleOPCUADataWrite(AssetTag tag, string assetId, string value)
        {
            byte[] tagBytes = null;
            if (tag.Type == "Float")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "Boolean")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "Integer")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by OPC UA.");
            }

            _assets[assetId].Write(tag.Address, 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }

        private void HandleSiemensDataRead(AssetTag tag, string assetId)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if (addressParts.Length == 2)
            {
                byte[] tagBytes = null;
                try
                {
                    tagBytes = _assets[assetId].Read(addressParts[0], 0, null, ushort.Parse(addressParts[1])).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);

                    // try reconnecting
                    string[] remoteEndpoint = _assets[assetId].GetRemoteEndpoint().Split(':');
                    _assets[assetId].Disconnect();
                    _assets[assetId].Connect(remoteEndpoint[0], int.Parse(remoteEndpoint[1]));
                }

                if ((tagBytes != null) && (tagBytes.Length > 0))
                {
                    object value = null;
                    if (tag.Type == "Float")
                    {
                        value = BitConverter.ToSingle(tagBytes);
                    }
                    else if (tag.Type == "Boolean")
                    {
                        value = BitConverter.ToBoolean(tagBytes);
                    }
                    else if (tag.Type == "Integer")
                    {
                        value = BitConverter.ToInt32(tagBytes);
                    }
                    else if (tag.Type == "String")
                    {
                        value = Encoding.UTF8.GetString(tagBytes);
                    }
                    else
                    {
                        throw new ArgumentException("Type not supported by Siemens.");
                    }

                    UpdateUAServerVariable(tag, value);
                }
            }
        }

        private void HandleSiemensDataWrite(AssetTag tag, string assetId, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "Float")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "Boolean")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "Integer")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by Siemens.");
            }

            _assets[assetId].Write(addressParts[0], 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }

        private void HandleMitsubishiDataRead(AssetTag tag, string assetId)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if (addressParts.Length == 2)
            {
                byte[] tagBytes = null;
                try
                {
                    tagBytes = _assets[assetId].Read(addressParts[0], 0, null, ushort.Parse(addressParts[1])).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);

                    // try reconnecting
                    string[] remoteEndpoint = _assets[assetId].GetRemoteEndpoint().Split(':');
                    _assets[assetId].Disconnect();
                    _assets[assetId].Connect(remoteEndpoint[0], int.Parse(remoteEndpoint[1]));
                }

                if ((tagBytes != null) && (tagBytes.Length > 0))
                {
                    object value = null;
                    if (tag.Type == "Float")
                    {
                        value = BitConverter.ToSingle(tagBytes);
                    }
                    else if (tag.Type == "Boolean")
                    {
                        value = BitConverter.ToBoolean(tagBytes);
                    }
                    else if (tag.Type == "Integer")
                    {
                        value = BitConverter.ToInt32(tagBytes);
                    }
                    else if (tag.Type == "String")
                    {
                        value = Encoding.UTF8.GetString(tagBytes);
                    }
                    else
                    {
                        throw new ArgumentException("Type not supported by Mitsubishi.");
                    }

                    UpdateUAServerVariable(tag, value);
                }
            }
        }

        private void HandleMitsubishiDataWrite(AssetTag tag, string assetId, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "Float")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "Boolean")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "Integer")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by Mitsubishi.");
            }

            _assets[assetId].Write(addressParts[0], 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }

        private void HandleRockwellDataRead(AssetTag tag, string assetId)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if (addressParts.Length == 2)
            {
                if (addressParts[0].StartsWith("Cxn:Standard:"))
                {
                    UpdateUAServerVariable(tag, addressParts[1]);
                }
                else
                {
                    byte[] tagBytes = null;
                    try
                    {
                        tagBytes = _assets[assetId].Read(addressParts[0], byte.Parse(addressParts[1]), tag.Type, 0).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error(ex.Message, ex);

                        // try reconnecting
                        string remoteEndpoint = _assets[assetId].GetRemoteEndpoint();
                        _assets[assetId].Disconnect();
                        _assets[assetId].Connect(remoteEndpoint, 0);
                    }

                    if ((tagBytes != null) && (tagBytes.Length > 0))
                    {
                        object value = null;

                        if (tag.Type == "BOOL")
                        {
                            value = BitConverter.ToBoolean(tagBytes);
                        }
                        else if (tag.Type == "SINT")
                        {
                            value = BitConverter.ToChar(tagBytes);
                        }
                        else if (tag.Type == "INT")
                        {
                            value = BitConverter.ToInt16(tagBytes);
                        }
                        else if (tag.Type == "DINT")
                        {
                            value = BitConverter.ToInt32(tagBytes);
                        }
                        else if (tag.Type == "LINT")
                        {
                            value = BitConverter.ToInt64(tagBytes);
                        }
                        else if (tag.Type == "USINT")
                        {
                            value = BitConverter.ToChar(tagBytes);
                        }
                        else if (tag.Type == "UINT")
                        {
                            value = BitConverter.ToUInt16(tagBytes);
                        }
                        else if (tag.Type == "UDINT")
                        {
                            value = BitConverter.ToInt32(tagBytes);
                        }
                        else if (tag.Type == "ULINT")
                        {
                            value = BitConverter.ToUInt64(tagBytes);
                        }
                        else if (tag.Type == "REAL")
                        {
                            value = BitConverter.ToSingle(tagBytes);
                        }
                        else if (tag.Type == "LREAL")
                        {
                            value = BitConverter.ToDouble(tagBytes);
                        }
                        else
                        {
                            throw new ArgumentException("Type not supported by Ethernet/IP.");
                        }

                        UpdateUAServerVariable(tag, value);
                    }
                }
            }
        }

        private void HandleRockwellDataWrite(AssetTag tag, string assetId, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "BOOL")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "SINT")
            {
                tagBytes = BitConverter.GetBytes(char.Parse(value));
            }
            else if (tag.Type == "INT")
            {
                tagBytes = BitConverter.GetBytes(short.Parse(value));
            }
            else if (tag.Type == "DINT")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "LINT")
            {
                tagBytes = BitConverter.GetBytes(Int64.Parse(value));
            }
            else if (tag.Type == "USINT")
            {
                tagBytes = BitConverter.GetBytes(char.Parse(value));
            }
            else if (tag.Type == "UINT")
            {
                tagBytes = BitConverter.GetBytes(ushort.Parse(value));
            }
            else if (tag.Type == "UDINT")
            {
                tagBytes = BitConverter.GetBytes(uint.Parse(value));
            }
            else if (tag.Type == "ULINT")
            {
                tagBytes = BitConverter.GetBytes(UInt64.Parse(value));
            }
            else if (tag.Type == "REAL")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "LREAL")
            {
                tagBytes = BitConverter.GetBytes(double.Parse(value));
            }
            else
            {
                throw new ArgumentException("Type not supported by Rockwell.");
            }

            _assets[assetId].Write(addressParts[0], byte.Parse(addressParts[1]), tag.Type, tagBytes, false).GetAwaiter().GetResult();
        }

        private void HandleBeckhoffDataRead(AssetTag tag, string assetId)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if (addressParts.Length == 2)
            {
                byte[] tagBytes = null;
                try
                {
                    tagBytes = _assets[assetId].Read(addressParts[0], 0, null, ushort.Parse(addressParts[1])).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);

                    // try reconnecting
                    string[] remoteEndpoint = _assets[assetId].GetRemoteEndpoint().Split(':');
                    _assets[assetId].Disconnect();
                    _assets[assetId].Connect(remoteEndpoint[0] + ":" + remoteEndpoint[1], int.Parse(remoteEndpoint[2]));
                }

                if ((tagBytes != null) && (tagBytes.Length > 0))
                {
                    object value = null;
                    if (tag.Type == "Float")
                    {
                        value = BitConverter.ToSingle(tagBytes);
                    }
                    else if (tag.Type == "Boolean")
                    {
                        value = BitConverter.ToBoolean(tagBytes);
                    }
                    else if (tag.Type == "Integer")
                    {
                        value = BitConverter.ToInt32(tagBytes);
                    }
                    else if (tag.Type == "String")
                    {
                        value = Encoding.UTF8.GetString(tagBytes);
                    }
                    else
                    {
                        throw new ArgumentException("Type not supported by Beckhoff.");
                    }

                    UpdateUAServerVariable(tag, value);
                }
            }
        }

        private void HandleBeckhoffDataWrite(AssetTag tag, string assetId, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "Float")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "Boolean")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "Integer")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by Beckhoff.");
            }

            _assets[assetId].Write(addressParts[0], 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }

        private void HandleBACNetDataRead(AssetTag tag, string assetId)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if (addressParts.Length == 2)
            {
                byte[] tagBytes = null;
                try
                {
                    tagBytes = _assets[assetId].Read(addressParts[0], 0, null, ushort.Parse(addressParts[1])).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);

                    // try reconnecting
                    string[] remoteEndpoint = _assets[assetId].GetRemoteEndpoint().Split(':');
                    _assets[assetId].Disconnect();
                    _assets[assetId].Connect(remoteEndpoint[0] + ":" + remoteEndpoint[1], int.Parse(remoteEndpoint[2]));
                }

                if ((tagBytes != null) && (tagBytes.Length > 0))
                {
                    object value = null;
                    if (tag.Type == "Float")
                    {
                        value = BitConverter.ToSingle(tagBytes);
                    }
                    else if (tag.Type == "Boolean")
                    {
                        value = BitConverter.ToBoolean(tagBytes);
                    }
                    else if (tag.Type == "Integer")
                    {
                        value = BitConverter.ToInt32(tagBytes);
                    }
                    else if (tag.Type == "String")
                    {
                        value = Encoding.UTF8.GetString(tagBytes);
                    }
                    else
                    {
                        throw new ArgumentException("Type not supported by BACNet.");
                    }

                    UpdateUAServerVariable(tag, value);
                }
            }
        }

        private void HandleBACNetDataWrite(AssetTag tag, string assetId, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "Float")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "Boolean")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "Integer")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by BACNet.");
            }

            _assets[assetId].Write(addressParts[0], 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }
    }
}
