
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

    public class UANodeManager : CustomNodeManager2
    {
        private ushort m_namespaceIndex;
        private long m_lastUsedId;

        private Timer m_timer;

        private Dictionary<string, BaseDataVariableState> _uaVariables = new();

        private Dictionary<string, IAsset> _assets = new();

        private List<AssetTag> _tags = new();

        public UANodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            List<string> namespaceUris = new List<string>
            {
                "http://opcfoundation.org/UA/EdgeTranslator/"
            };

            NamespaceUris = namespaceUris;

            m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[0]);
            m_lastUsedId = 0;

            m_timer = new Timer(UpdateNodeValues, null, 1000, 1000);
        }

        public override NodeId New(ISystemContext context, NodeState node)
        {
            return new NodeId(Utils.IncrementIdentifier(ref m_lastUsedId), m_namespaceIndex);
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

                // create our top-level asset management folder
                FolderState assetManagementFolder = CreateFolder(null, "AssetManagement", NamespaceIndex);
                assetManagementFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, assetManagementFolder.NodeId));
                assetManagementFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
                AddRootNotifier(assetManagementFolder);

                // create our methods
                MethodState configureAssetMethod = CreateMethod(assetManagementFolder, "ConfigureAsset", NamespaceIndex);
                configureAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(ConfigureAsset);
                configureAssetMethod.InputArguments = CreateInputArguments(configureAssetMethod, "WoTThingDescription", "The WoT Thing Description of the asset to be configured");

                MethodState deleteAssetMethod = CreateMethod(assetManagementFolder, "DeleteAsset", NamespaceIndex);
                deleteAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(DeleteAsset);
                deleteAssetMethod.InputArguments = CreateInputArguments(deleteAssetMethod, "AssetID", "The ID of the asset to be deleted");

                MethodState getAssetsMethod = CreateMethod(assetManagementFolder, "GetAssets", NamespaceIndex);
                getAssetsMethod.OnCallMethod = new GenericMethodCalledEventHandler(GetAssets);

                AddPredefinedNode(SystemContext, assetManagementFolder);

                IEnumerable<string> WoTFiles = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.jsonld");
                foreach (string file in WoTFiles)
                {
                    try
                    {
                        string contents = File.ReadAllText(file);

                        // parse WoT TD files contents
                        ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents);

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

                            _assets.Add(td.Title, client);
                        }

                        // create a top-level OPC UA folder for the asset
                        FolderState assetFolder = CreateFolder(null, td.Title, NamespaceIndex);
                        assetFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                        references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, assetFolder.NodeId));
                        assetFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
                        AddRootNotifier(assetFolder);

                        // create nodes for each TD property
                        foreach (KeyValuePair<string, Property> property in td.Properties)
                        {
                            foreach (object form in property.Value.Forms)
                            {
                                if (td.Base.ToLower().StartsWith("modbus://"))
                                {
                                    ModbusForm modbusForm = JsonConvert.DeserializeObject<ModbusForm>(form.ToString());

                                    // create a OPC UA variable
                                    _uaVariables.Add(property.Key, CreateVariable(assetFolder, property.Key, NamespaceIndex));
                                    _uaVariables[property.Key].Value = 0.0f;

                                    // create an asset tag and add to our list
                                    AssetTag tag = new()
                                    {
                                        Name = property.Key,
                                        Address = modbusForm.Href,
                                        Type = modbusForm.ModbusType.ToString(),
                                        AssetName = td.Title,
                                        PollingInterval = (int)modbusForm.ModbusPollingTime,
                                        Entity = modbusForm.ModbusEntity.ToString(),
                                        MappedUAExpandedNodeID = NodeId.ToExpandedNodeId(_uaVariables[property.Key].NodeId, Server.NamespaceUris).ToString()
                                    };
                                    _tags.Add(tag);
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
        }

        private PropertyState<Argument[]> CreateInputArguments(NodeState parent, string name, string description)
        {
            PropertyState<Argument[]> arguments = new PropertyState<Argument[]>(parent)
            {
                NodeId = new NodeId(parent.BrowseName.Name + "InArgs", NamespaceIndex),
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

        private FolderState CreateFolder(NodeState parent, string name, ushort namespaceIndex)
        {
            FolderState folder = new FolderState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };
            parent?.AddChild(folder);

            return folder;
        }

        private BaseDataVariableState CreateVariable(NodeState parent, string name, ushort namespaceIndex, bool isString = false)
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
                DataType = isString ? DataTypes.String : DataTypes.Float
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
            Thread.Sleep(1000);

            Program.App.Stop();
            Program.App.Start(new UAServer()).GetAwaiter().GetResult();
        }

        private ServiceResult GetAssets(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            foreach (string asset in _assets.Keys)
            {
                outputArguments.Add(asset);
            }

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

        private void UpdateNodeValues(object state)
        {
            foreach (AssetTag tag in _tags)
            {
                try
                {
                    if (_assets.ContainsKey(tag.AssetName))
                    {
                        if (_assets[tag.AssetName] is ModbusTCPClient)
                        {
                            // read tag
                            byte unitID = 1;
                            ModbusTCPClient.FunctionCode functionCode = ModbusTCPClient.FunctionCode.ReadHoldingRegisters;
                            uint address = uint.Parse(tag.Address);
                            ushort count = ushort.Parse(tag.Address);

                            byte[] tagBytes = _assets[tag.AssetName].Read(unitID, functionCode.ToString(), address, count).GetAwaiter().GetResult();

                            _uaVariables[tag.Name].Value = BitConverter.ToSingle(ByteSwapper.Swap(tagBytes));
                        }
                    }
                }
                catch (Exception ex)
                {
                    // skip this tag, but log an error
                    Log.Logger.Error(ex.Message, ex);
                }
            }

            foreach (BaseDataVariableState variable in _uaVariables.Values.ToList())
            {
                variable.Timestamp = DateTime.UtcNow;
                variable.ClearChangeMasks(SystemContext, false);
            }
        }
    }
}
