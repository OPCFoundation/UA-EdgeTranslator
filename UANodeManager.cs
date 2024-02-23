
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
    using System.Threading;
    using System.Threading.Tasks;
    using UANodeSet = Opc.Ua.Export.UANodeSet;

    public class UANodeManager : CustomNodeManager2
    {
        private long _lastUsedId = 0;

        private bool _shutdown = false;

        private readonly Dictionary<string, BaseDataVariableState> _uaVariables = new();

        private readonly Dictionary<string, IAsset> _assets = new();

        private readonly Dictionary<string, List<AssetTag>> _tags = new();

        private uint _counter = 0;

        private readonly UACloudLibraryClient _uacloudLibraryClient = new();

        public UANodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            // add our default namespace
            List<string> namespaceUris = new()
            {
                "http://opcfoundation.org/UA/EdgeTranslator/"
            };

            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "settings")))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "settings"));
            }

            // log into UA Cloud Library and download available Nodeset files
            _uacloudLibraryClient.Login(Environment.GetEnvironmentVariable("UACLURL"), Environment.GetEnvironmentVariable("UACLUsername"), Environment.GetEnvironmentVariable("UACLPassword"));

            // add a seperate namespace for each asset from the WoT TD files
            IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
            foreach (string file in WoTFiles)
            {
                try
                {
                    string contents = File.ReadAllText(file);

                    // check file type (WoT TD or DTDL) and convert to WoT if required
                    if (contents.Contains("\"@context\": \"dtmi:dtdl:context;2\""))
                    {
                        // parse DTDL contents and convert to WoT
                        contents = WoT2DTDLMapper.DTDL2WoT(contents);
                    }

                    // parse WoT TD files contents
                    ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents);

                    // add all namespaces
                    namespaceUris.Add("http://opcfoundation.org/UA/" + td.Name + "/");

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

                    LoadNamespacesFromOPCUACompanionSpecs(namespaceUris, td);
                }
                catch (Exception ex)
                {
                    // skip this file, but log an error
                    Log.Logger.Error(ex.Message, ex);
                }
            }

            NamespaceUris = namespaceUris;
        }

        private void LoadNamespacesFromOPCUACompanionSpecs(List<string> namespaceUris, ThingDescription td)
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
                        LoadNamespaceUrisFromStream(namespaceUris, nodesetFile);
                    }
                    else
                    {
                        if (_uacloudLibraryClient.DownloadNamespace(Environment.GetEnvironmentVariable("UACLURL"), opcuaCompanionSpecUrl.OriginalString))
                        {
                            Log.Logger.Information("Loaded nodeset from Cloud Library URL: " + opcuaCompanionSpecUrl);

                            foreach (string nodesetFile in _uacloudLibraryClient._nodeSetFilenames)
                            {
                                LoadNamespaceUrisFromStream(namespaceUris, nodesetFile);
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

        private void LoadNamespaceUrisFromStream(List<string> namespaceUris, string nodesetFile)
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

        public override NodeId New(ISystemContext context, NodeState node)
        {
            // for new nodes we create, pick our default namespace
            return new NodeId(Utils.IncrementIdentifier(ref _lastUsedId), (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                IList<IReference> references = null;
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
                }

                AddAssetManagementNodes(references);

                if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "settings")))
                {
                    Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "settings"));
                }

                IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
                foreach (string file in WoTFiles)
                {
                    try
                    {
                        ParseAsset(file, out ThingDescription td);

                        AddOPCUACompanionSpecNodes(td);

                        string assetId = AddAsset(td, out BaseObjectState assetFolder, out byte unitId);

                        // create nodes for each TD property
                        foreach (KeyValuePair<string, Property> property in td.Properties)
                        {
                            foreach (object form in property.Value.Forms)
                            {
                                if (td.Base.ToLower().StartsWith("modbus+tcp://"))
                                {
                                    AddUANodeForModbusRegister(assetFolder, property, form, assetId, unitId);
                                }
                            }
                        }

                        AddPredefinedNode(SystemContext, assetFolder);

                        _ = Task.Factory.StartNew(UpdateNodeValues, assetId, TaskCreationOptions.LongRunning);

                        Log.Logger.Information($"Configured asset: {assetId}");
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

        private void AddUANodeForModbusRegister(BaseObjectState assetFolder, KeyValuePair<string, Property> property, object form, string assetId, byte unitId)
        {
            ModbusForm modbusForm = JsonConvert.DeserializeObject<ModbusForm>(form.ToString());
            string variableId = $"{assetId}:{property.Key}";
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
                        if (!string.IsNullOrEmpty(modbusForm.OpcUaFieldPath))
                        {
                            DataTypeState opcuaType = (DataTypeState)Find(ExpandedNodeId.ToNodeId(ParseExpandedNodeId(property.Value.OpcUaType), Server.NamespaceUris));
                            if (((StructureDefinition)opcuaType?.DataTypeDefinition?.Body).Fields?.Count > 0)
                            {
                                ExtensionObject complexTypeInstance = new();
                                complexTypeInstance.TypeId = opcuaType.NodeId;

                                BinaryEncoder encoder = new(ServiceMessageContext.GlobalContext);
                                foreach (StructureField field in ((StructureDefinition)opcuaType?.DataTypeDefinition?.Body).Fields)
                                {
                                    // check which built-in type the complex type field is. See https://reference.opcfoundation.org/Core/Part6/v104/docs/5.1.2
                                    switch (field.DataType.ToString())
                                    {
                                        case "i=10": encoder.WriteFloat(field.Name, 0); break;
                                        default: throw new NotImplementedException("Complex type field data type " + field.DataType.ToString() + " not yet supported!");
                                    }

                                    if (field.Name == modbusForm.OpcUaFieldPath)
                                    {
                                        // add the field path to make sure we can distinguish the tag during data updates
                                        fieldPath = field.Name;
                                    }
                                }

                                complexTypeInstance.Body = encoder.CloseAndReturnBuffer();

                                // now add it, if it doesn't already exist
                                if (!_uaVariables.ContainsKey(variableId))
                                {
                                    _uaVariables.Add(variableId, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(new NodeId(nodeID), namespaceURI), assetFolder.NodeId.NamespaceIndex, complexTypeInstance));
                                }
                            }
                            else
                            {
                                // OPC UA type info not found, default to float
                                _uaVariables.Add(variableId, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
                            }
                        }
                        else
                        {
                            // it's an OPC UA built-in type
                            _uaVariables.Add(variableId, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(new NodeId(nodeID), namespaceURI), assetFolder.NodeId.NamespaceIndex));
                        }
                    }
                    else
                    {
                        // no namespace info, default to float
                        _uaVariables.Add(variableId, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
                    }
                }
                else
                {
                    // can't parse type info, default to float
                    _uaVariables.Add(variableId, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
                }
            }
            else
            {
                // no type info, default to float
                _uaVariables.Add(variableId, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
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

        private void AddOPCUACompanionSpecNodes(ThingDescription td)
        {
            // we need as many passes as we have nodesetfiles to make sure all references can be resolved
            for (int i = 0; i < _uacloudLibraryClient._nodeSetFilenames.Count; i++)
            {
                foreach (string nodesetFile in _uacloudLibraryClient._nodeSetFilenames)
                {
                    using (Stream stream = new FileStream(nodesetFile, FileMode.Open))
                    {
                        UANodeSet nodeSet = UANodeSet.Read(stream);

                        NodeStateCollection predefinedNodes = new NodeStateCollection();
                        nodeSet.Import(SystemContext, predefinedNodes);

                        for (int j = 0; j < predefinedNodes.Count; j++)
                        {
                            try
                            {
                                AddPredefinedNode(SystemContext, predefinedNodes[j]);
                            }
                            catch (Exception)
                            {
                                // do nothing
                            }
                        }
                    }
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
                }
            }
        }

        private void ParseAsset(string file, out ThingDescription td)
        {
            string contents = File.ReadAllText(file);

            // check file type (WoT TD or DTDL)
            if (contents.Contains("\"@context\": \"dtmi:dtdl:context;2\""))
            {
                // parse DTDL contents and convert to WoT
                contents = WoT2DTDLMapper.DTDL2WoT(contents);
            }

            // parse WoT TD file contents
            td = JsonConvert.DeserializeObject<ThingDescription>(contents);

            // to test our lossless conversion between DTDL and WoT, generate DTDL content, convert back to WoT and compare to original
            string dtdlContent = WoT2DTDLMapper.WoT2DTDL(contents);
            string convertedWoTTDContent = WoT2DTDLMapper.DTDL2WoT(dtdlContent);

            // uncomment this line to check if the files are exactly the same, otherwise compare them in a comparer tool like Beyond Compare:
            // Debug.Assert(JObject.DeepEquals(JObject.Parse(convertedWoTTDContent), JObject.Parse(contents)));
        }

        private string AddAsset(ThingDescription td, out BaseObjectState assetFolder, out byte unitId)
        {
            // create a connection to the asset
            unitId = 1;
            IAsset assetInterface = null;
            if (td.Base.ToLower().StartsWith("modbus+tcp://"))
            {
                string[] modbusAddress = td.Base.Split(new char[] { ':','/' });
                if ((modbusAddress.Length != 6) && (modbusAddress[0] != "modbus+tcp"))
                {
                    throw new Exception("Expected Modbus address in the format modbus+tcp://ipaddress:port/unitID!");
                }

                // check if we can reach the Modbus asset
                unitId = byte.Parse(modbusAddress[5]);
                ModbusTCPClient client = new();
                client.Connect(modbusAddress[3], int.Parse(modbusAddress[4]));

                assetInterface = client;
            }

            ExpandedNodeId typeNodeId = ParseExpandedNodeId(td.OpcUaType);
            if (typeNodeId != null)
            {
                Log.Logger.Information($"Set asset type definition: ns={typeNodeId.NamespaceIndex}, i={typeNodeId.Identifier}.");
            }

            assetFolder = CreateObject(null, td.Title + " [" + td.Name + "]", "Asset", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/" + td.Name + "/"), ExpandedNodeId.ToNodeId(typeNodeId, Server.NamespaceUris));
            assetFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);

            assetFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(assetFolder);

            string assetId = NodeId.ToExpandedNodeId(assetFolder.NodeId, Server.NamespaceUris).ToString();
            _assets.Add(assetId, assetInterface);

            return assetId;
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

        private void AddAssetManagementNodes(IList<IReference> references)
        {
            // create our top-level WoT asset connection management folder
            BaseObjectState assetManagementFolder = CreateObject(null, "WoTAssetConnectionManagement", null, (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
            assetManagementFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, assetManagementFolder.NodeId));
            assetManagementFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(assetManagementFolder);

            // create our WoT asset connection management methods
            MethodState configureAssetMethod = CreateMethod(assetManagementFolder, "CreateAsset", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
            configureAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(CreateAsset);
            configureAssetMethod.InputArguments = CreateInputArguments(configureAssetMethod, "WoTTD", "The WoT Thing Description of the asset to be configured", DataTypeIds.String, (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
            configureAssetMethod.OutputArguments = CreateOutputArguments(configureAssetMethod, "AssetId", "The ID of the created asset", DataTypeIds.String, (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));

            MethodState deleteAssetMethod = CreateMethod(assetManagementFolder, "DeleteAsset", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
            deleteAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(DeleteAsset);
            deleteAssetMethod.InputArguments = CreateInputArguments(deleteAssetMethod, "AssetId", "The ID of the asset to be deleted", DataTypeIds.String, (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));

            MethodState getAssetsMethod = CreateMethod(assetManagementFolder, "GetAssetsIds", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
            getAssetsMethod.OnCallMethod = new GenericMethodCalledEventHandler(GetAssetIds);
            getAssetsMethod.OutputArguments = CreateOutputArguments(getAssetsMethod, "AssetIds", "The IDs of the assets currently defined", DataTypeIds.String, (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"), true);

            MethodState getAssetMethod = CreateMethod(assetManagementFolder, "GetAsset", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
            getAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(GetAsset);
            getAssetMethod.InputArguments = CreateInputArguments(getAssetMethod, "AssetId", "The ID of the asset for which to retrieve the WoT Thing Description", DataTypeIds.String, (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));

            // add everything to our server namespace
            AddPredefinedNode(SystemContext, assetManagementFolder);
        }

        private PropertyState<Argument[]> CreateInputArguments(NodeState parent, string name, string description, NodeId dataType, ushort namespaceIndex, bool isArray = false)
        {
            PropertyState<Argument[]> arguments = new(parent)
            {
                NodeId = new NodeId(parent.BrowseName.Name + "InArgs", namespaceIndex),
                BrowseName = BrowseNames.InputArguments,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                DataType = DataTypeIds.Argument,
                ValueRank = ValueRanks.OneDimension,
                Value = new Argument[]
                {
                    new Argument { Name = name, Description = description, DataType = dataType, ValueRank = isArray ? ValueRanks.OneDimension : ValueRanks.Scalar }
                }
            };

            arguments.DisplayName = arguments.BrowseName.Name;

            return arguments;
        }

        private PropertyState<Argument[]> CreateOutputArguments(NodeState parent, string name, string description, NodeId dataType, ushort namespaceIndex, bool isArray = false)
        {
            PropertyState<Argument[]> arguments = new(parent)
            {
                NodeId = new NodeId(parent.BrowseName.Name + "OutArgs", namespaceIndex),
                BrowseName = BrowseNames.OutputArguments,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                DataType = DataTypeIds.Argument,
                ValueRank = ValueRanks.OneDimension,
                Value = new Argument[]
                {
                    new Argument { Name = name, Description = description, DataType = DataTypeIds.Guid, ValueRank = isArray ? ValueRanks.OneDimension : ValueRanks.Scalar }
                }
            };

            arguments.DisplayName = arguments.BrowseName.Name;

            return arguments;
        }

        private BaseObjectState CreateObject(NodeState parent, string name, string description, ushort namespaceIndex, NodeId typeDefinition = null)
        {
            BaseObjectState folder = new BaseObjectState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = typeDefinition ?? ObjectTypeIds.BaseObjectType,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                Description = new Opc.Ua.LocalizedText(null, description),
                DisplayName = new Opc.Ua.LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };
            parent?.AddChild(folder);

            return folder;
        }

        private BaseDataVariableState CreateVariable(NodeState parent, string name, ExpandedNodeId type, ushort namespaceIndex, object value = null, NodeId nodeId = null)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                NodeId = (nodeId == null)? new NodeId(name, namespaceIndex) : nodeId,
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new Opc.Ua.LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                AccessLevel = AccessLevels.CurrentRead,
                DataType = ExpandedNodeId.ToNodeId(type, Server.NamespaceUris),
                Value = value
            };
            parent?.AddChild(variable);

            return variable;
        }

        private MethodState CreateMethod(NodeState parent, string name, ushort namespaceIndex)
        {
            MethodState method = new MethodState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new Opc.Ua.LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                Executable = true,
                UserExecutable = true
            };

            parent?.AddChild(method);

            return method;
        }

        private ServiceResult CreateAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (inputArguments.Count == 0)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            try
            {
                // test if we can parse the content and connect to the asset
                string contents = inputArguments[0].ToString();

                // check file type (WoT TD or DTDL)
                if (contents.Contains("\"@context\": \"dtmi:dtdl:context;2\""))
                {
                    // parse DTDL contents and convert to WoT
                    contents = WoT2DTDLMapper.DTDL2WoT(contents);
                }

                // parse WoT TD file contents
                ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents);

                List<string> namespaceUris = new(NamespaceUris);
                namespaceUris.Add("http://opcfoundation.org/UA/" + td.Name + "/");

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

                LoadNamespacesFromOPCUACompanionSpecs(namespaceUris, td);

                NamespaceUris = namespaceUris;

                AddOPCUACompanionSpecNodes(td);

                string assetId = AddAsset(td, out BaseObjectState assetFolder, out byte unitId);

                // create nodes for each TD property
                foreach (KeyValuePair<string, Property> property in td.Properties)
                {
                    foreach (object form in property.Value.Forms)
                    {
                        if (td.Base.ToLower().StartsWith("modbus+tcp://"))
                        {
                            AddUANodeForModbusRegister(assetFolder, property, form, assetId, unitId);
                        }
                    }
                }

                AddPredefinedNode(SystemContext, assetFolder);

                _ = Task.Factory.StartNew(UpdateNodeValues, assetId, TaskCreationOptions.LongRunning);

                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings", Guid.NewGuid().ToString() + ".jsonld"), contents);

                outputArguments[0] = assetId;

                _ = Task.Run(() => HandleServerRestart());

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                outputArguments[0] = string.Empty;
                return new ServiceResult(ex, StatusCodes.BadInternalError, ex.Message);
            }
        }

        private ServiceResult DeleteAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (inputArguments.Count == 0)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
            foreach (string file in WoTFiles)
            {
                try
                {
                    string assetId = Path.GetFileNameWithoutExtension(file);

                    if (inputArguments[0].ToString() == assetId)
                    {
                        File.Delete(file);

                        _ = Task.Run(() => HandleServerRestart());

                        return ServiceResult.Good;
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);
                    return new ServiceResult(ex);
                }
            }

            return new ServiceResult(StatusCodes.BadNotFound);
        }

        private ServiceResult GetAssetIds(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (outputArguments.Count == 0)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            outputArguments[0] = _assets.Keys.ToArray();

            return ServiceResult.Good;
        }

        private ServiceResult GetAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (inputArguments.Count == 0)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
            foreach (string file in WoTFiles)
            {
                try
                {
                    string assetId = Path.GetFileNameWithoutExtension(file);

                    if (inputArguments[0].ToString() == assetId)
                    {
                        outputArguments[0] = File.ReadAllText(file);

                        return ServiceResult.Good;
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);
                    return new ServiceResult(ex);
                }
            }

            return new ServiceResult(StatusCodes.BadNotFound);
        }

        private void HandleServerRestart()
        {
            _shutdown = true;

            Program.App.Stop();
            Program.App.Start(new UAServer()).GetAwaiter().GetResult();
        }

        private void UpdateNodeValues(object assetNameObject)
        {
            while (!_shutdown)
            {
                Thread.Sleep(1000);

                _counter++;

                string assetId = (string)assetNameObject;
                if (string.IsNullOrEmpty(assetId) || !_tags.ContainsKey(assetId) || !_assets.ContainsKey(assetId))
                {
                    throw new Exception("Cannot find asset: " + assetId);
                }

                foreach (AssetTag tag in _tags[assetId])
                {
                    try
                    {
                        if (_assets[assetId] is ModbusTCPClient)
                        {
                            if (_counter * 1000 % tag.PollingInterval == 0)
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
                                    byte unitID = tag.UnitID;
                                    uint address = uint.Parse(addressParts[0]);
                                    ushort quantity = ushort.Parse(addressParts[2]);

                                    byte[] tagBytes = null;
                                    try
                                    {
                                        tagBytes = _assets[assetId].Read(unitID, functionCode.ToString(), address, quantity).GetAwaiter().GetResult();
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
    }
}
