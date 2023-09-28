
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
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using UANodeSet = Opc.Ua.Export.UANodeSet;

    public class UANodeManager : CustomNodeManager2
    {
        private long _lastUsedId = 0;

        private bool _shutdown = false;

        private Dictionary<string, BaseDataVariableState> _uaVariables = new();

        private Dictionary<string, IAsset> _assets = new();

        private Dictionary<string, List<AssetTag>> _tags = new();

        private uint _counter = 0;

        private UACloudLibraryClient _uacloudLibraryClient = new();

        private readonly string _wotNodeset = Path.Combine(Directory.GetCurrentDirectory(), "Nodesets", "Opc.Ua.WoT.NodeSet2.xml");
        private readonly bool _useWotNodeset = false;

        public UANodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            // add our default namespace
            List<string> namespaceUris = new List<string>
            {
                "http://opcfoundation.org/UA/EdgeTranslator/"
            };

            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "settings")))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "settings"));
            }

            // log into UA Cloud Library and download available Nodeset files
            _uacloudLibraryClient.Login(Environment.GetEnvironmentVariable("UACLURL"), Environment.GetEnvironmentVariable("UACLUsername"), Environment.GetEnvironmentVariable("UACLPassword"));

            _useWotNodeset = File.Exists(_wotNodeset);
            if (_useWotNodeset)
            {
                LoadNamespaceUrisFromStream(namespaceUris, _wotNodeset);
            }

            // add a seperate namespace for each asset from the WoT TD files
            IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
            foreach (string file in WoTFiles)
            {
                try
                {
                    string contents = File.ReadAllText(file);

                    // check file type (WoT TD or DTDL)
                    if (contents.Contains("\"@context\": \"dtmi:dtdl:context;2\""))
                    {
                        // parse DTDL contents and convert to WoT
                        contents = WoT2DTDLMapper.DTDL2WoT(contents);
                    }

                    // parse WoT TD files contents
                    ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents);

                    namespaceUris.Add("http://opcfoundation.org/UA/" + td.Name + "/");

                    FetchOPCUACompanionSpecs(namespaceUris, td);
                }
                catch (Exception ex)
                {
                    // skip this file, but log an error
                    Log.Logger.Error(ex.Message, ex);
                }
            }

            NamespaceUris = namespaceUris;
        }

        private void FetchOPCUACompanionSpecs(List<string> namespaceUris, ThingDescription td)
        {
            // check if an OPC UA companion spec is mentioned in the WoT TD file
            foreach (Uri opcuaCompanionSpecUrl in td.Context)
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
                        var assetId = Path.GetFileNameWithoutExtension(file);
                        Log.Logger.Information($"Configuring asset: {assetId}");

                        ParseAsset(file, out ThingDescription td);
                        AddOPCUACompanionSpecNodes(td);
                        AddAsset(references, td, out BaseObjectState assetFolder, assetId);
                        
                        // create nodes for each TD property
                        foreach (KeyValuePair<string, Property> property in td.Properties)
                        {
                            foreach (object form in property.Value.Forms)
                            {
                                if (td.Base.ToLower().StartsWith("modbus://"))
                                {
                                    AddModbusNodes(td, assetFolder, property, form, assetId);
                                }
                            }
                        }

                        AddPredefinedNode(SystemContext, assetFolder);

                        _ = Task.Factory.StartNew(UpdateNodeValues, assetId/*td.Title + " [" + td.Name + "]"*/, TaskCreationOptions.LongRunning);
                    }
                    catch (Exception ex)
                    {
                        // skip this file, but log an error
                        Log.Logger.Error(ex.Message, ex);
                    }
                }
                
                AddReverseReferences(externalReferences);
            }
        }

        private void AddModbusNodes(ThingDescription td, BaseObjectState assetFolder, KeyValuePair<string, Property> property, object form, string assetId)
        {
            ModbusForm modbusForm = JsonConvert.DeserializeObject<ModbusForm>(form.ToString());
            var variableId = $"{assetId}:{property.Key}";

            // Check if the Modbus node has a predefined variable node to use.
            var variableNode = (BaseDataVariableState)Find(ExpandedNodeId.ToNodeId(ParseExpandedNodeId(modbusForm.OpcUaVariableNode), Server.NamespaceUris));
            if (variableNode != null)
            {
                Log.Logger.Information($"Mapping to existing variable node {variableNode.NodeId}/{variableNode.BrowseName}");
                _uaVariables.Add(variableId, variableNode);
            }
            else
            {
                // create an OPC UA variable optionally with a specified variable type.
                if (!string.IsNullOrEmpty(modbusForm.OpcUaType))
                {
                    string[] opcuaTypeParts = modbusForm.OpcUaType.Split(new char[] { '=', ';' });
                    if ((opcuaTypeParts.Length > 3) && (opcuaTypeParts[0] == "nsu") && (opcuaTypeParts[2] == "i"))
                    {
                        string namespaceURI = opcuaTypeParts[1];
                        uint nodeID = uint.Parse(opcuaTypeParts[3]);

                        if (NamespaceUris.Contains(namespaceURI))
                        {
							// TODO: Check if this variable is part of a complex type and we need to load the complex type first and then assign a part of it to the new variable.
							// This is not yet supported in the current OPC Foundation .Net Standard OPC UA stack.
							// Waiting for OPCFoundation.NetStandard.Opc.Ua.Server.ComplexTypes to become available!
						
                            _uaVariables.Add(variableId, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(new NodeId(nodeID), namespaceURI), assetFolder.NodeId.NamespaceIndex));
                        }
                        else
                        {
                            // default to float
                            _uaVariables.Add(variableId, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
                        }
                    }
                    else
                    {
                        // default to float
                        _uaVariables.Add(variableId, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
                    }
                }
                else
                {
                    // default to float
                    _uaVariables.Add(variableId, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(DataTypes.Float), assetFolder.NodeId.NamespaceIndex));
                }
            }

            // create an asset tag and add to our list
            AssetTag tag = new()
            {
                Name = variableId,
                Address = modbusForm.Href,
                Type = modbusForm.ModbusType.ToString(),
                PollingInterval = (int)modbusForm.ModbusPollingTime,
                Entity = modbusForm.ModbusEntity.ToString(),
                MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[variableId].NodeId, Server.NamespaceUris).ToString()
            };
            
            if (!_tags.ContainsKey(assetId))
            {
                _tags.Add(assetId, new List<AssetTag>());
            }

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
            
            foreach (var opcuaCompanionSpecUrl in td.Context)
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

            // generate DTDL content, convert back to WoT TD and compare to original
            string dtdlContent = WoT2DTDLMapper.WoT2DTDL(contents);
            string convertedWoTTDContent = WoT2DTDLMapper.DTDL2WoT(dtdlContent);
            //Debug.Assert(JObject.DeepEquals(JObject.Parse(convertedWoTTDContent), JObject.Parse(contents)));
        }

        private void AddAsset(IList<IReference> references, ThingDescription td, out BaseObjectState assetFolder, string assetId)
        {
            // create a connection to the asset
            if (td.Base.ToLower().StartsWith("modbus://"))
            {
                string[] modbusAddress = td.Base.Split(':');
                if (modbusAddress.Length != 3)
                {
                    throw new Exception("Expected Modbus address in the format modbus://ipaddress:port!");
                }

                // check if we can reach the Modbus asset
                ModbusTCPClient client = new();
                client.Connect(modbusAddress[1].TrimStart('/'), int.Parse(modbusAddress[2]));

                _assets.Add(assetId, client);
            }

            var objectNodeId = ParseExpandedNodeId(td.OpcUaObjectNode);

            // If the asset has defined a target node in the address space, link to that node, otherwise create a top-level OPC UA folder for the asset.
            if (objectNodeId != null)
            {
                Log.Logger.Information($"Map asset to node: ns={objectNodeId.NamespaceIndex}, i={objectNodeId.Identifier}.");
                assetFolder = (BaseObjectState)Find(ExpandedNodeId.ToNodeId(objectNodeId, Server.NamespaceUris));
                assetFolder.Description = new LocalizedText("en", td.Title + " [" + td.Name + "]");
            }
            else
            {
                var parentNodeId = ParseExpandedNodeId(td.OpcUaParentNode);
                if (parentNodeId != null)
                {
                    Log.Logger.Information($"Set asset parent node: ns={parentNodeId.NamespaceIndex}, i={parentNodeId.Identifier}.");
                }

                var typeNodeId = ParseExpandedNodeId(td.OpcUaObjectType);
                if (typeNodeId != null)
                {
                    Log.Logger.Information($"Set asset type definition: ns={typeNodeId.NamespaceIndex}, i={typeNodeId.Identifier}.");
                }

                assetFolder = CreateObject(null, td.Title + " [" + td.Name + "]", assetId, (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/" + td.Name + "/"), ExpandedNodeId.ToNodeId(typeNodeId, Server.NamespaceUris));
                assetFolder.AddReference(ReferenceTypes.Organizes, true, parentNodeId ?? ObjectIds.ObjectsFolder);
            }
            
            assetFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(assetFolder);
        }

        private ExpandedNodeId? ParseExpandedNodeId(string nodeString)
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
            // If the WoT Nodeset is modeled, use that instead of creating new objects for the asset management.
            if (_useWotNodeset)
            {
                NodeStateCollection predefinedNodes = new NodeStateCollection();
                predefinedNodes.LoadFromBinaryResource(SystemContext, "Nodesets/Opc.Ua.WoT.PredefinedNodes.uanodes", this.GetType().GetTypeInfo().Assembly, true);
                
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
                
                var assetManagement = (BaseObjectState)Find(ExpandedNodeId.ToNodeId(new ExpandedNodeId(WoT.Objects.AssetManagement, WoT.Namespaces.WoT), Server.NamespaceUris));

                var configureAsset = (MethodState)assetManagement.FindChild(SystemContext, new QualifiedName("ConfigureAsset", (ushort)Server.NamespaceUris.GetIndex(WoT.Namespaces.WoT)));
                configureAsset.OnCallMethod = ConfigureAsset;

                var deleteAsset = (MethodState)assetManagement.FindChild(SystemContext, new QualifiedName("DeleteAsset", (ushort)Server.NamespaceUris.GetIndex(WoT.Namespaces.WoT)));
                deleteAsset.OnCallMethod = DeleteAsset;

                var getAssets = (MethodState)assetManagement.FindChild(SystemContext, new QualifiedName("GetConfiguredAssets", (ushort)Server.NamespaceUris.GetIndex(WoT.Namespaces.WoT)));
                getAssets.OnCallMethod = GetConfiguredAssets;

                AddPredefinedNode(SystemContext, assetManagement);
            }
            else
            {
                // create our top-level asset management folder
                var assetManagementFolder = CreateObject(null, "AssetManagement", null,(ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
                assetManagementFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, assetManagementFolder.NodeId));
                assetManagementFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
                AddRootNotifier(assetManagementFolder);

                // create our methods
                var configureAssetMethod = CreateMethod(assetManagementFolder, "ConfigureAsset", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
                configureAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(ConfigureAsset);
                configureAssetMethod.InputArguments = CreateInputArguments(configureAssetMethod, "WoTTD", "The WoT Thing Description of the asset to be configured", DataTypeIds.String, (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
                configureAssetMethod.OutputArguments = CreateOutputArguments(configureAssetMethod, "AssetId", "The ID of the created asset", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));

                var deleteAssetMethod = CreateMethod(assetManagementFolder, "DeleteAsset", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
                deleteAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(DeleteAsset);
                deleteAssetMethod.InputArguments = CreateInputArguments(deleteAssetMethod, "AssetId", "The ID of the asset to be deleted", DataTypeIds.Guid, (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));

                var getAssetsMethod = CreateMethod(assetManagementFolder, "GetConfiguredAssets", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
                getAssetsMethod.OnCallMethod = new GenericMethodCalledEventHandler(GetConfiguredAssets);
                getAssetsMethod.OutputArguments = CreateOutputArguments(getAssetsMethod, "AssetIds", "The IDs of the assets currently defined", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"), true);
                AddPredefinedNode(SystemContext, assetManagementFolder);
            }
            
        }

        private PropertyState<Argument[]> CreateInputArguments(NodeState parent, string name, string description, NodeId dataType, ushort namespaceIndex)
        {
            var arguments = new PropertyState<Argument[]>(parent)
            {
                NodeId = new NodeId(parent.BrowseName.Name + "InArgs", namespaceIndex),
                BrowseName = BrowseNames.InputArguments,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                DataType = DataTypeIds.Argument,
                ValueRank = ValueRanks.OneDimension,
                Value = new Argument[]
                {
                    new Argument { Name = name, Description = description, DataType = dataType, ValueRank = ValueRanks.Scalar }
                }
            };

            arguments.DisplayName = arguments.BrowseName.Name;

            return arguments;
        }

        private PropertyState<Argument[]> CreateOutputArguments(NodeState parent, string name, string description, ushort namespaceIndex, bool isArray = false) {
            var arguments = new PropertyState<Argument[]>(parent) {
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
                Description = new LocalizedText(null, description),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };
            parent?.AddChild(folder);

            return folder;
        }

        private BaseDataVariableState CreateVariable(NodeState parent, string name, ExpandedNodeId type, ushort namespaceIndex)
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
                DataType = ExpandedNodeId.ToNodeId(type, Server.NamespaceUris)
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
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                Executable = true,
                UserExecutable = true
            };

            parent?.AddChild(method);

            return method;
        }

        private ServiceResult ConfigureAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
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

                // create a connection to the asset
                if (td.Base.ToLower().StartsWith("modbus://"))
                {
                    string[] modbusAddress = td.Base.Split(':');
                    if (modbusAddress.Length != 3)
                    {
                        throw new Exception("Expected Modbus address in the format modbus://ipaddress:port!");
                    }

                    // check if we can reach the Modbus asset
                    ModbusTCPClient client = new();
                    client.Connect(modbusAddress[1].TrimStart('/'), int.Parse(modbusAddress[2]));
                    client.Disconnect();
                }

                var assetId = Guid.NewGuid();
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "settings", assetId + ".jsonld"), contents);

                outputArguments[0] = assetId;

                _ = Task.Run(() => HandleServerRestart());

                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return new ServiceResult(ex);
            }
        }

        private void HandleServerRestart()
        {
            _shutdown = true;

            Program.App.Stop();
            Program.App.Start(new UAServer()).GetAwaiter().GetResult();
        }

        private ServiceResult GetConfiguredAssets(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (outputArguments.Count == 0)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            outputArguments[0] = _assets.Keys.ToArray();

            return ServiceResult.Good;
        }

        private ServiceResult DeleteAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (inputArguments.Count == 0)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            var isDeleted = false;
            IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Path.Combine(Directory.GetCurrentDirectory(), "settings"), "*.jsonld");
            foreach (string file in WoTFiles)
            {
                try
                {
                    string assetId = Path.GetFileNameWithoutExtension(file);
                    
                    if (inputArguments[0].ToString() == assetId)
                    {
                        File.Delete(file);
                        isDeleted = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);
                    return new ServiceResult(ex);
                }
            }

            if (isDeleted)
            {
                _ = Task.Run(() => HandleServerRestart());

                return ServiceResult.Good;
            }

            return new ServiceResult(StatusCodes.BadNotFound);
        }

        private void UpdateNodeValues(object assetNameObject)
        {
            while (!_shutdown)
            {
                Thread.Sleep(1000);

                _counter++;

                string assetId = (string) assetNameObject;
                if (string.IsNullOrEmpty(assetId) || !_tags.ContainsKey(assetId) || !_assets.ContainsKey(assetId))
                {
                    throw new Exception("Cannot find asset: " +  assetId);
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
                                if (tag.Entity == "Holdingregister")
                                {
                                    functionCode = ModbusTCPClient.FunctionCode.ReadHoldingRegisters;
                                }

                                string[] addressParts = tag.Address.Split(new char[] { '?', '&', '=' });

                                if ((addressParts.Length > 4) && (addressParts[1] == "address") && (addressParts[3] == "quantity"))
                                {
                                    // read tag
                                    byte unitID = byte.Parse(addressParts[0].TrimStart('/'));
                                    uint address = uint.Parse(addressParts[2]);
                                    ushort quantity = ushort.Parse(addressParts[4]);

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
                                        _uaVariables[tag.Name].Value = BitConverter.ToSingle(ByteSwapper.Swap(tagBytes));
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
