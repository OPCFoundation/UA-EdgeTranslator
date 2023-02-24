
namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Export;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public class UANodeManager : CustomNodeManager2
    {
        private long _lastUsedId = 0;

        private bool _shutdown = false;

        private Dictionary<string, BaseDataVariableState> _uaVariables = new();

        private Dictionary<string, IAsset> _assets = new();

        private List<AssetTag> _tags = new();

        private uint _counter = 0;

        private UACloudLibraryClient _uacloudLibraryClient = new();

        public UANodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            // add our default namespace
            List<string> namespaceUris = new List<string>
            {
                "http://opcfoundation.org/UA/EdgeTranslator/"
            };

            // add a seperate namespace for each asset from the WoT TD files
            IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.jsonld");
            foreach (string file in WoTFiles)
            {
                try
                {
                    string contents = File.ReadAllText(file);

                    // parse WoT TD files contents
                    ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents);

                    namespaceUris.Add("http://opcfoundation.org/UA/" + td.Name + "/");

                    FetchOPCUACompanionSpec(namespaceUris, td);
                }
                catch (Exception ex)
                {
                    // skip this file, but log an error
                    Log.Logger.Error(ex.Message, ex);
                }
            }

            NamespaceUris = namespaceUris;
        }

        private void FetchOPCUACompanionSpec(List<string> namespaceUris, ThingDescription td)
        {
            // check if an OPC UA companion spec is mentioned in the WoT TD file
            string opcuaCompanionSpecUrl = string.Empty;
            string opcuaCompanionSpecPath = string.Empty;
            foreach (Uri uris in td.Context)
            {
                // all known UA Cloud Libraries are supported
                if (uris.IsAbsoluteUri && (uris.AbsoluteUri.Contains("https://uacloudlibrary.opcfoundation.org") || uris.AbsoluteUri.Contains("https://cloudlib.cesmii.net")))
                {
                    opcuaCompanionSpecUrl = uris.AbsoluteUri;
                }
                else
                {
                    if (!uris.IsAbsoluteUri || (!uris.AbsoluteUri.Contains("http://") && !uris.AbsoluteUri.Contains("https://")))
                    {
                        opcuaCompanionSpecPath = uris.OriginalString;
                    }
                }
            }

            // support local Nodesets
            if (!string.IsNullOrEmpty(opcuaCompanionSpecPath))
            {
                string nodesetFile = string.Empty;
                if (Path.IsPathFullyQualified(opcuaCompanionSpecPath))
                {
                    // absolute file path
                    nodesetFile = opcuaCompanionSpecPath;
                }
                else
                {
                    // relative file path
                    nodesetFile = Path.Combine(Directory.GetCurrentDirectory(), opcuaCompanionSpecPath);
                }

                Log.Logger.Information("Loading nodeset from local file: " + nodesetFile);

                LoadNamespaceUrisFromStream(namespaceUris, nodesetFile);
            }

            // UA Cloud Library nodesets: Log into UA Cloud Library to download the companion spec and its dependencies and add their namespaces to our list
            if (!string.IsNullOrEmpty(opcuaCompanionSpecUrl))
            {
                _uacloudLibraryClient.Login(opcuaCompanionSpecUrl, Environment.GetEnvironmentVariable("UACLUsername"), Environment.GetEnvironmentVariable("UACLPassword"));

                Log.Logger.Information("Loading nodeset from Cloud Library URL: " + opcuaCompanionSpecUrl);

                foreach (string nodesetFile in _uacloudLibraryClient._nodeSetFilenames)
                {
                    LoadNamespaceUrisFromStream(namespaceUris, nodesetFile);
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

                IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.jsonld");
                foreach (string file in WoTFiles)
                {
                    try
                    {
                        AddAsset(references, file, out ThingDescription td, out FolderState assetFolder);

                        AddOPCUACompanionSpecNodes(td);

                        // create nodes for each TD property
                        foreach (KeyValuePair<string, Property> property in td.Properties)
                        {
                            foreach (object form in property.Value.Forms)
                            {
                                if (td.Base.ToLower().StartsWith("modbus://"))
                                {
                                    AddModbusNodes(td, assetFolder, property, form);
                                }
                            }
                        }

                        AddPredefinedNode(SystemContext, assetFolder);
                    }
                    catch (Exception ex)
                    {
                        // skip this file, but log an error
                        Log.Logger.Error(ex.Message, ex);
                    }
                }

                AddReverseReferences(externalReferences);
            }

            _ = Task.Run(() => UpdateNodeValues());
        }

        private void AddModbusNodes(ThingDescription td, FolderState assetFolder, KeyValuePair<string, Property> property, object form)
        {
            ModbusForm modbusForm = JsonConvert.DeserializeObject<ModbusForm>(form.ToString());

            // create a OPC UA variable
            if (!string.IsNullOrEmpty(modbusForm.OpcUaType))
            {
                string[] opcuaTypeParts = modbusForm.OpcUaType.Split(new char[] { '=', ';' });
                if ((opcuaTypeParts.Length > 3) && (opcuaTypeParts[0] == "nsu") && (opcuaTypeParts[2] == "i"))
                {
                    string namespaceURI = opcuaTypeParts[1];
                    uint nodeID = uint.Parse(opcuaTypeParts[3]);
                    _uaVariables.Add(property.Key, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(new NodeId(nodeID), namespaceURI), (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/" + td.Name + "/")));
                }
                else
                {
                    // default to float
                    _uaVariables.Add(property.Key, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(DataTypes.Float), (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/" + td.Name + "/")));
                }
            }
            else
            {
                // default to float
                _uaVariables.Add(property.Key, CreateVariable(assetFolder, property.Key, new ExpandedNodeId(DataTypes.Float), (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/" + td.Name + "/")));
            }

            // create an asset tag and add to our list
            AssetTag tag = new()
            {
                Name = property.Key,
                Address = modbusForm.Href,
                Type = modbusForm.ModbusType.ToString(),
                AssetName = td.Title + " [" + td.Name + "]",
                PollingInterval = (int)modbusForm.ModbusPollingTime,
                Entity = modbusForm.ModbusEntity.ToString(),
                MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[property.Key].NodeId, Server.NamespaceUris).ToString()
            };
            _tags.Add(tag);
        }

        private void AddOPCUACompanionSpecNodes(ThingDescription td)
        {
            string opcuaCompanionSpecDownloadUrl = string.Empty;
            foreach (Uri uris in td.Context)
            {
                if (uris.AbsoluteUri.Contains("https://uacloudlibrary.opcfoundation.org"))
                {
                    opcuaCompanionSpecDownloadUrl = uris.AbsoluteUri;
                }
            }

            // log into UA Cloud Library if a companion spec is mentioned in the WoT TD file
            if (!string.IsNullOrEmpty(opcuaCompanionSpecDownloadUrl))
            {
                _uacloudLibraryClient.Login(opcuaCompanionSpecDownloadUrl, Environment.GetEnvironmentVariable("UACLUsername"), Environment.GetEnvironmentVariable("UACLPassword"));

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
            }
        }

        private void AddAsset(IList<IReference> references, string file, out ThingDescription td, out FolderState assetFolder)
        {
            string contents = File.ReadAllText(file);

            // parse WoT TD files contents
            td = JsonConvert.DeserializeObject<ThingDescription>(contents);

            // create a connection to the asset
            if (td.Base.ToLower().StartsWith("modbus://"))
            {
                string[] modbusAddress = td.Base.Split(':');
                if (modbusAddress.Length != 3)
                {
                    throw new Exception("Expected Modbus address in the format modbus://ipaddress:port!");
                }

                ModbusTCPClient client = new();
                client.Connect(modbusAddress[1].TrimStart('/'), int.Parse(modbusAddress[2]));

                _assets.Add(td.Title + " [" + td.Name + "]", client);
            }

            // create a top-level OPC UA folder for the asset
            assetFolder = CreateFolder(null, td.Title + " [" + td.Name + "]", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/" + td.Name + "/"));
            assetFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, assetFolder.NodeId));
            assetFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(assetFolder);
        }

        private void AddAssetManagementNodes(IList<IReference> references)
        {
            // create our top-level asset management folder
            FolderState assetManagementFolder = CreateFolder(null, "AssetManagement", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
            assetManagementFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, assetManagementFolder.NodeId));
            assetManagementFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
            AddRootNotifier(assetManagementFolder);

            // create our methods
            MethodState configureAssetMethod = CreateMethod(assetManagementFolder, "ConfigureAsset", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
            configureAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(ConfigureAsset);
            configureAssetMethod.InputArguments = CreateInputArguments(configureAssetMethod, "WoTThingDescription", "The WoT Thing Description of the asset to be configured", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));

            MethodState deleteAssetMethod = CreateMethod(assetManagementFolder, "DeleteAsset", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
            deleteAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(DeleteAsset);
            deleteAssetMethod.InputArguments = CreateInputArguments(deleteAssetMethod, "AssetID", "The ID of the asset to be deleted", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));

            MethodState getAssetsMethod = CreateMethod(assetManagementFolder, "GetAssets", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));
            getAssetsMethod.OnCallMethod = new GenericMethodCalledEventHandler(GetAssets);
            getAssetsMethod.OutputArguments = CreateOutputArguments(getAssetsMethod, "AssetIDs", "The IDs of the assets currently defined", (ushort)Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/"));

            AddPredefinedNode(SystemContext, assetManagementFolder);
        }

        private PropertyState<Argument[]> CreateInputArguments(NodeState parent, string name, string description, ushort namespaceIndex)
        {
            PropertyState<Argument[]> arguments = new PropertyState<Argument[]>(parent)
            {
                NodeId = new NodeId(parent.BrowseName.Name + "InArgs", namespaceIndex),
                BrowseName = BrowseNames.InputArguments,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                DataType = DataTypeIds.Argument,
                ValueRank = ValueRanks.OneDimension,
                Value = new Argument[]
                {
                    new Argument { Name = name, Description = description, DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar }
                }
            };

            arguments.DisplayName = arguments.BrowseName.Name;

            return arguments;
        }

        private PropertyState<Argument[]> CreateOutputArguments(NodeState parent, string name, string description, ushort namespaceIndex) {
            PropertyState<Argument[]> arguments = new PropertyState<Argument[]>(parent) {
                NodeId = new NodeId(parent.BrowseName.Name + "OutArgs", namespaceIndex),
                BrowseName = BrowseNames.OutputArguments,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                DataType = DataTypeIds.Argument,
                ValueRank = ValueRanks.OneDimension,
                Value = new Argument[]
                {
                    new Argument { Name = name, Description = description, DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar }
                }
            };

            arguments.DisplayName = arguments.BrowseName.Name;

            return arguments;
        }

        private FolderState CreateFolder(NodeState parent, string name, ushort namespaceIndex)
        {
            FolderState folder = new FolderState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new Ua.LocalizedText("en", name),
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
                DisplayName = new Ua.LocalizedText("en", name),
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
                DisplayName = new Ua.LocalizedText("en", name),
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
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString() + ".jsonld"), inputArguments[0].ToString());

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

            Thread.Sleep(5000);

            Program.App.Stop();
            Program.App.Start(new UAServer()).GetAwaiter().GetResult();
        }

        private ServiceResult GetAssets(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (outputArguments.Count == 0)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            outputArguments[0] = string.Empty;
            foreach (string asset in _assets.Keys)
            {
                outputArguments[0] += asset + ",";
            }
            outputArguments[0] = ((string)outputArguments[0]).TrimEnd(',');

            return ServiceResult.Good;
        }

        private ServiceResult DeleteAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            if (inputArguments.Count == 0)
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }

            IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.jsonld");
            foreach (string file in WoTFiles)
            {
                try
                {
                    string contents = File.ReadAllText(file);

                    // parse WoT TD files contents
                    ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents);

                    if (inputArguments[0].ToString() == td.Title)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);
                    return new ServiceResult(ex);
                }
            }

            _ = Task.Run(() => HandleServerRestart());

            return ServiceResult.Good;
        }

        private void UpdateNodeValues()
        {
            while (!_shutdown)
            {
                Thread.Sleep(1000);

                _counter++;

                foreach (AssetTag tag in _tags)
                {
                    try
                    {
                        if (_assets.ContainsKey(tag.AssetName))
                        {
                            if (_assets[tag.AssetName] is ModbusTCPClient)
                            {
                                if (_counter * 1000 % tag.PollingInterval == 0)
                                {
                                    ModbusTCPClient.FunctionCode functionCode = ModbusTCPClient.FunctionCode.ReadCoilStatus;
                                    if (tag.Entity == "Holdingregister")
                                    {
                                        functionCode = ModbusTCPClient.FunctionCode.ReadHoldingRegisters;
                                    }

                                    string[] addressParts = tag.Address.Split(new char[] { '?', '&', '=' });

                                    if ((addressParts.Length > 4) && (addressParts[1] == "offset") && (addressParts[3] == "length"))
                                    {
                                        // read tag
                                        byte unitID = byte.Parse(addressParts[0].TrimStart('/'));
                                        uint offset = uint.Parse(addressParts[2]);
                                        ushort length = ushort.Parse(addressParts[4]);
                                        byte[] tagBytes = _assets[tag.AssetName].Read(unitID, functionCode.ToString(), offset, length).GetAwaiter().GetResult();

                                        if (tag.Type == "Float")
                                        {
                                            _uaVariables[tag.Name].Value = BitConverter.ToSingle(ByteSwapper.Swap(tagBytes));
                                            _uaVariables[tag.Name].Timestamp = DateTime.UtcNow;
                                            _uaVariables[tag.Name].ClearChangeMasks(SystemContext, false);
                                        }
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
