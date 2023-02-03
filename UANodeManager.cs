
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    public class UANodeManager : CustomNodeManager2
    {
        private ushort m_namespaceIndex;
        private long m_lastUsedId;

        private Timer m_timer;

        private Dictionary<string, BaseDataVariableState> _uaVariables = new();

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

                // create a top-level asset folder
                FolderState assetFolder = CreateFolder(null, "Asset1", NamespaceIndex);
                assetFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, assetFolder.NodeId));
                assetFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
                AddRootNotifier(assetFolder);

                // create our variables
                _uaVariables.Add("Temperature", CreateVariable(assetFolder, "Temperature", NamespaceIndex));
                _uaVariables["Temperature"].Value = 0.0f;

                AddPredefinedNode(SystemContext, assetFolder);

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
            // TODO
            throw new NotImplementedException();
        }

        private ServiceResult GetAssets(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            // TODO
            throw new NotImplementedException();
        }

        private ServiceResult DeleteAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            // TODO
            throw new NotImplementedException();
        }

        private void UpdateNodeValues(object state)
        {
            _uaVariables["Temperature"].Value = (float)_uaVariables["Temperature"].Value + 0.1f;

            foreach (BaseDataVariableState variable in _uaVariables.Values.ToList())
            {
                variable.Timestamp = DateTime.UtcNow;
                variable.ClearChangeMasks(SystemContext, false);
            }
        }
    }
}
