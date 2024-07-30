
namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using UANodeSet = Export.UANodeSet;

    public class UANodeManager : CustomNodeManager2
    {
        private long _lastUsedId = 0;

        private bool _shutdown = false;

        private readonly UAModel.WoT_Con.WoTAssetConnectionManagementTypeState _assetManagement = new(null);

        private readonly Dictionary<string, BaseDataVariableState> _uaVariables = new();

        private readonly Dictionary<string, IAsset> _assets = new();

        private readonly Dictionary<string, List<AssetTag>> _tags = new();

        private readonly UACloudLibraryClient _uacloudLibraryClient = new();

        private readonly Dictionary<NodeId, FileManager> _fileManagers = new();

        private uint _ticks = 0;


        public UANodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            // create our settings folder, if required
            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "settings")))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "settings"));
            }

            // in the node manager constructor, we add all namespaces
            List<string> namespaceUris = new()
            {
                "http://opcfoundation.org/UA/EdgeTranslator/"
            };

            // log into UA Cloud Library and download available namespaces
            _uacloudLibraryClient.Login(Environment.GetEnvironmentVariable("UACLURL"), Environment.GetEnvironmentVariable("UACLUsername"), Environment.GetEnvironmentVariable("UACLPassword"));

            LoadNamespaceUrisFromNodesetXml(namespaceUris, "Opc.Ua.WotCon.NodeSet2.xml");

            // add a seperate namespace for each asset from the WoT TD files
            IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
            foreach (string file in WoTFiles)
            {
                try
                {
                    string contents = File.ReadAllText(file);

                    // parse WoT TD file contents
                    ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents);


                    namespaceUris.Add("http://opcfoundation.org/UA/" + td.Name + "/");

                    AddNamespacesFromCompanionSpecs(namespaceUris, td);
                }
                catch (Exception ex)
                {
                    // skip this file, but log an error
                    Log.Logger.Error(ex.Message, ex);
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

        public void HandleServerRestart()
        {
            _shutdown = true;

            Program.App.Stop();
            Program.App.Start(new UAServer()).GetAwaiter().GetResult();
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                // in the create address space call, we add all our nodes

                IList<IReference> objectsFolderReferences = null;
                if (!externalReferences.TryGetValue(Ua.ObjectIds.ObjectsFolder, out objectsFolderReferences))
                {
                    externalReferences[Ua.ObjectIds.ObjectsFolder] = objectsFolderReferences = new List<IReference>();
                }

                AddNodesFromNodesetXml("Opc.Ua.WotCon.NodeSet2.xml");

                AddNodesForAssetManagement(objectsFolderReferences);

                IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
                foreach (string file in WoTFiles)
                {
                    try
                    {
                        string contents = File.ReadAllText(file);
                        string fileName = Path.GetFileNameWithoutExtension(file);

                        if (!CreateAssetNode(fileName, out NodeState assetNode))
                        {
                            throw new Exception("Asset already exists");
                        }

                        AddNodesForWoTProperties(assetNode, contents);
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
            ushort WoTConNamespaceIndex = (ushort)Server.NamespaceUris.GetIndex(UAModel.WoT_Con.Namespaces.WoT_Con);

            BaseObjectState assetManagementPassiveNode = (BaseObjectState)FindPredefinedNode(new NodeId(UAModel.WoT_Con.Objects.WoTAssetConnectionManagement, WoTConNamespaceIndex), typeof(BaseObjectState));
            _assetManagement.Create(SystemContext, assetManagementPassiveNode);

            MethodState createAssetPassiveNode = (MethodState)FindPredefinedNode(new NodeId(UAModel.WoT_Con.Methods.WoTAssetConnectionManagement_CreateAsset, WoTConNamespaceIndex), typeof(MethodState));
            _assetManagement.CreateAsset = new(null);
            _assetManagement.CreateAsset.Create(SystemContext, createAssetPassiveNode);
            _assetManagement.CreateAsset.OnCall = new UAModel.WoT_Con.CreateAssetMethodStateMethodCallHandler(OnCreateAsset);

            BaseVariableState createAssetInputArgumentsPassiveNode = (BaseVariableState)FindPredefinedNode(new NodeId(UAModel.WoT_Con.Variables.WoTAssetConnectionManagementType_CreateAsset_InputArguments, WoTConNamespaceIndex), typeof(BaseVariableState));
            _assetManagement.CreateAsset.InputArguments = new(null);
            _assetManagement.CreateAsset.InputArguments.Create(SystemContext, createAssetInputArgumentsPassiveNode);

            BaseVariableState createAssetOutputArgumentsPassiveNode = (BaseVariableState)FindPredefinedNode(new NodeId(UAModel.WoT_Con.Variables.WoTAssetConnectionManagementType_CreateAsset_OutputArguments, WoTConNamespaceIndex), typeof(BaseVariableState));
            _assetManagement.CreateAsset.OutputArguments = new(null);
            _assetManagement.CreateAsset.OutputArguments.Create(SystemContext, createAssetOutputArgumentsPassiveNode);

            MethodState deleteAssetPassiveNode = (MethodState)FindPredefinedNode(new NodeId(UAModel.WoT_Con.Methods.WoTAssetConnectionManagement_DeleteAsset, WoTConNamespaceIndex), typeof(MethodState));
            _assetManagement.DeleteAsset = new(null);
            _assetManagement.DeleteAsset.Create(SystemContext, deleteAssetPassiveNode);
            _assetManagement.DeleteAsset.OnCall = new UAModel.WoT_Con.DeleteAssetMethodStateMethodCallHandler(OnDeleteAsset);

            BaseVariableState deleteAssetInputArgumentsPassiveNode = (BaseVariableState)FindPredefinedNode(new NodeId(UAModel.WoT_Con.Variables.WoTAssetConnectionManagementType_DeleteAsset_InputArguments, WoTConNamespaceIndex), typeof(BaseVariableState));
            _assetManagement.DeleteAsset.InputArguments = new(null);
            _assetManagement.DeleteAsset.InputArguments.Create(SystemContext, deleteAssetInputArgumentsPassiveNode);

            // create a variable listing our supported WoT protocol bindings
            CreateVariable(_assetManagement, "SupportedWoTBindings", new ExpandedNodeId(DataTypes.UriString), WoTConNamespaceIndex, new string[2] { "https://www.w3.org/2019/wot/modbus", "https://www.w3.org/2019/wot/opcua" });

            // add everything to our server namespace
            objectsFolderReferences.Add(new NodeStateReference(ReferenceTypes.Organizes, false, _assetManagement.NodeId));
            AddPredefinedNode(SystemContext, _assetManagement);
        }

        private void AddNamespacesFromCompanionSpecs(List<string> namespaceUris, ThingDescription td)
        {
            // check if an OPC UA companion spec is mentioned in the WoT TD file
            foreach (object ns in td.Context)
            {
                if (ns.ToString().Contains("https://www.w3.org/") && !ns.ToString().Contains("opcua"))
                {
                    continue;
                }

                OpcUaNamespaces namespaces = JsonConvert.DeserializeObject<OpcUaNamespaces>(ns.ToString());
                foreach (Uri opcuaCompanionSpecUrl in namespaces.Namespaces)
                {
                    // support local Nodesets
                    if (!opcuaCompanionSpecUrl.IsAbsoluteUri || (!opcuaCompanionSpecUrl.AbsoluteUri.Contains("http://") && !opcuaCompanionSpecUrl.AbsoluteUri.Contains("https://")))
                    {
                        string nodesetFile = string.Empty;
                        if (Path.IsPathFullyQualified(opcuaCompanionSpecUrl.OriginalString))
                        {
                            // absolute file path
                            nodesetFile = opcuaCompanionSpecUrl.OriginalString;
                        }
                        else
                        {
                            // relative file path
                            nodesetFile = Path.Combine(Directory.GetCurrentDirectory(), opcuaCompanionSpecUrl.OriginalString);
                        }

                        Log.Logger.Information("Loading nodeset from local file: " + nodesetFile);
                        LoadNamespaceUrisFromNodesetXml(namespaceUris, nodesetFile);
                    }
                    else
                    {
                        if (_uacloudLibraryClient.DownloadNamespace(Environment.GetEnvironmentVariable("UACLURL"), opcuaCompanionSpecUrl.OriginalString))
                        {
                            Log.Logger.Information("Loaded nodeset from Cloud Library URL: " + opcuaCompanionSpecUrl);

                            foreach (string nodesetFile in _uacloudLibraryClient._nodeSetFilenames)
                            {
                                LoadNamespaceUrisFromNodesetXml(namespaceUris, nodesetFile);
                            }
                        }
                        else
                        {
                            Log.Logger.Warning($"Could not load nodeset {opcuaCompanionSpecUrl.OriginalString}");
                        }
                    }
                }

                string validationError = _uacloudLibraryClient.ValidateNamespacesAndModels(Environment.GetEnvironmentVariable("UACLURL"), true);
                if (!string.IsNullOrEmpty(validationError))
                {
                    Log.Logger.Error(validationError);
                }
            }
        }

        private void AddNodesFromCompanionSpecs(ThingDescription td)
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
                if (ns.ToString().Contains("https://www.w3.org/") && !ns.ToString().Contains("opcua"))
                {
                    continue;
                }

                OpcUaNamespaces namespaces = JsonConvert.DeserializeObject<OpcUaNamespaces>(ns.ToString());
                foreach (Uri opcuaCompanionSpecUrl in namespaces.Namespaces)
                {
                    // support local Nodesets
                    if (!opcuaCompanionSpecUrl.IsAbsoluteUri || (!opcuaCompanionSpecUrl.AbsoluteUri.Contains("http://") && !opcuaCompanionSpecUrl.AbsoluteUri.Contains("https://")))
                    {
                        string nodesetFile = string.Empty;
                        if (Path.IsPathFullyQualified(opcuaCompanionSpecUrl.OriginalString))
                        {
                            // absolute file path
                            nodesetFile = opcuaCompanionSpecUrl.OriginalString;
                        }
                        else
                        {
                            // relative file path
                            nodesetFile = Path.Combine(Directory.GetCurrentDirectory(), opcuaCompanionSpecUrl.OriginalString);
                        }

                        Log.Logger.Information("Adding node set from local nodeset file");
                        AddNodesFromNodesetXml(nodesetFile);
                    }
                }
            }
        }

        private void LoadNamespaceUrisFromNodesetXml(List<string> namespaceUris, string nodesetFile)
        {
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
                    catch (Exception)
                    {
                        // do nothing
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

        private ServiceResult OnCreateAsset(
            ISystemContext _context,
            MethodState _method,
            NodeId _objectId,
            string assetName,
            ref NodeId assetId)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                return StatusCodes.BadInvalidArgument;
            }

            bool success = CreateAssetNode(assetName, out NodeState assetNode);
            if (!success)
            {
                return new ServiceResult(StatusCodes.BadBrowseNameDuplicated, new LocalizedText(assetNode.NodeId.ToString()));
            }
            else
            {
                assetId = assetNode.NodeId;
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

                UAModel.WoT_Con.IWoTAssetTypeState asset = new(null);
                asset.Create(SystemContext, new NodeId(), new QualifiedName(assetName), null, true);

                _assetManagement.AddChild(asset);

                FileManager fileManager = new(this, asset.WoTFile);
                _fileManagers.Add(asset.NodeId, fileManager);

                AddPredefinedNode(SystemContext, asset);

                assetNode = asset;
                return true;
            }
        }

        private ServiceResult OnDeleteAsset(
            ISystemContext _context,
            MethodState _method,
            NodeId _objectId,
            NodeId assetId)
        {
            lock (Lock)
            {
                NodeState asset = FindPredefinedNode(assetId, typeof(UAModel.WoT_Con.IWoTAssetTypeState));
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
                        File.Delete(file);
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

        public void AddNodesForWoTProperties(NodeState parent, string contents)
        {
            // parse WoT TD file contents
            ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents);

            string newNamespace = "http://opcfoundation.org/UA/" + td.Name + "/";
            List<string> namespaceUris = new(NamespaceUris);
            if (!namespaceUris.Contains(newNamespace))
            {
                namespaceUris.Add(newNamespace);
            };

            foreach (object ns in td.Context)
            {
                if (!ns.ToString().Contains("https://www.w3.org/") && ns.ToString().Contains("opcua"))
                {
                    OpcUaNamespaces namespaces = JsonConvert.DeserializeObject<OpcUaNamespaces>(ns.ToString());
                    foreach (Uri opcuaCompanionSpecUrl in namespaces.Namespaces)
                    {
                        namespaceUris.Add(opcuaCompanionSpecUrl.ToString());
                    }
                }
            }

            AddNamespacesFromCompanionSpecs(namespaceUris, td);

            NamespaceUris = namespaceUris;

            AddNodesFromCompanionSpecs(td);

            string assetId = AssetConnectionTest(td, out byte unitId);

            // create nodes for each TD property
            foreach (KeyValuePair<string, Property> property in td.Properties)
            {
                foreach (object form in property.Value.Forms)
                {
                    if (td.Base.ToLower().StartsWith("modbus+tcp://"))
                    {
                        AddNodeForModbusRegister(parent, property, form, assetId, unitId);
                    }

                    if (td.Base.ToLower().StartsWith("opcua+tcp://"))
                    {
                        AddNodeForOPCUANode(parent, property, form, assetId, unitId);
                    }
                }
            }

            _ = Task.Factory.StartNew(UpdateNodeValues, assetId, TaskCreationOptions.LongRunning);

            Log.Logger.Information($"Successfully parsed WoT file for asset: {assetId}");
        }

        private void AddNodeForModbusRegister(NodeState assetFolder, KeyValuePair<string, Property> property, object form, string assetId, byte unitId)
        {
            ModbusForm modbusForm = JsonConvert.DeserializeObject<ModbusForm>(form.ToString());

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
                            if (((StructureDefinition)opcuaType?.DataTypeDefinition?.Body).Fields?.Count > 0)
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
                                    _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(new NodeId(nodeID), namespaceURI), assetFolder.NodeId.NamespaceIndex, complexTypeInstance));
                                }
                            }
                            else
                            {
                                // OPC UA type info not found, default to float
                                _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
                            }
                        }
                        else
                        {
                            // it's an OPC UA built-in type
                            _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(new NodeId(nodeID), namespaceURI), assetFolder.NodeId.NamespaceIndex));
                        }
                    }
                    else
                    {
                        // no namespace info, default to float
                        _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
                    }
                }
                else
                {
                    // can't parse type info, default to float
                    _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
                }
            }
            else
            {
                // no type info, default to float
                _uaVariables.Add(variableId, CreateVariable(assetFolder, variableName, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
            }


            // create an asset tag and add to our list
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

            // check if we need to create a new asset first
            if (!_tags.ContainsKey(assetId))
            {
                _tags.Add(assetId, new List<AssetTag>());
            }

            // add the tag to the asset
            _tags[assetId].Add(tag);
        }

        private void AddNodeForOPCUANode(NodeState assetFolder, KeyValuePair<string, Property> property, object form, string assetId, byte unitId)
        {
            // TODO
            throw new NotImplementedException();
        }

        private string AssetConnectionTest(ThingDescription td, out byte unitId)
        {
            unitId = 1;
            IAsset assetInterface = null;

            if (td.Base.ToLower().StartsWith("modbus+tcp://"))
            {
                string[] modbusAddress = td.Base.Split(new char[] { ':', '/' });
                if ((modbusAddress.Length != 6) && (modbusAddress[0] != "modbus+tcp"))
                {
                    throw new Exception("Expected Modbus server address in the format modbus+tcp://ipaddress:port/unitID!");
                }

                // check if we can reach the Modbus asset
                unitId = byte.Parse(modbusAddress[5]);
                ModbusTCPClient client = new();
                client.Connect(modbusAddress[3], int.Parse(modbusAddress[4]));

                assetInterface = client;
            }

            if (td.Base.ToLower().StartsWith("opcua+tcp://"))
            {
                string[] opcuaAddress = td.Base.Split(new char[] { ':', '/' });
                if ((opcuaAddress.Length != 5) && (opcuaAddress[0] != "opcua+tcp"))
                {
                    throw new Exception("Expected OPC UA server address in the format opcua+tcp://ipaddress:port!");
                }

                // check if we can reach the OPC UA asset
                UAClient client = new();
                client.Connect(opcuaAddress[3], int.Parse(opcuaAddress[4]));

                assetInterface = client;
            }

            _assets.Add(td.Name, assetInterface);

            return td.Name;
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

        private BaseDataVariableState CreateVariable(NodeState parent, string name, ExpandedNodeId type, ushort namespaceIndex, object value = null)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                AccessLevel = AccessLevels.CurrentRead,
                DataType = ExpandedNodeId.ToNodeId(type, Server.NamespaceUris),
                Value = value
            };
            parent?.AddChild(variable);
            parent?.AddReference(ExpandedNodeId.ToNodeId(UAModel.WoT_Con.ReferenceTypeIds.HasWoTComponent, Server.NamespaceUris), false, variable.NodeId);

            AddPredefinedNode(SystemContext, variable);

            return variable;
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
                                HandleModbusDataUpdate(tag, assetId);
                            }

                            if (_assets[assetId] is UAClient)
                            {
                                HandleOPCUADataUpdate(tag, assetId);
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

        private void HandleModbusDataUpdate(AssetTag tag, string assetId)
        {
            ModbusTCPClient.FunctionCode functionCode = ModbusTCPClient.FunctionCode.ReadCoilStatus;
            if (tag.Entity == "HoldingRegister")
            {
                functionCode = ModbusTCPClient.FunctionCode.ReadHoldingRegisters;
            }

            string[] addressParts = tag.Address.Split(new char[] { '?', '&', '=' });

            if ((addressParts.Length == 3) && (addressParts[1] == "quantity"))
            {
                // read tag
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

                if ((tagBytes != null) && (tag.Type == "Float"))
                {
                    float value = BitConverter.ToSingle(ByteSwapper.Swap(tagBytes));

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
                                                newValue = value;
                                            }

                                            encoder.WriteFloat(field.Name, newValue);

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

        private void HandleOPCUADataUpdate(AssetTag tag, string assetId)
        {
            // TODO
            throw new NotImplementedException();
        }
    }
}
