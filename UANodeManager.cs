
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using System;
    using System.Collections.Generic;
    using System.Threading;

    public class UANodeManager : CustomNodeManager2
    {
        private ushort m_namespaceIndex;
        private long m_lastUsedId;

        private Timer m_timer;

        private NodeId m_NumberOfManufacturedProductsID = null;
        private object m_numberOfManufacturedProducts = 0;

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

                // create our top-level folder
                FolderState assetManagementFolder = CreateFolder(null, "AssetManagement", "AssetManagement");
                assetManagementFolder.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, assetManagementFolder.NodeId));
                assetManagementFolder.EventNotifier = EventNotifiers.SubscribeToEvents;
                AddRootNotifier(assetManagementFolder);

                // create our methods
                MethodState configureAssetMethod = CreateMethod(assetManagementFolder, "ConfigureAsset", "ConfigureAsset");
                configureAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(ConfigureAsset);
                configureAssetMethod.InputArguments = CreateInputArguments(configureAssetMethod, "WoTThingDescription", "The WoT Thing Description of the asset to be configured");

                MethodState deleteAssetMethod = CreateMethod(assetManagementFolder, "DeleteAsset", "DeleteAsset");
                deleteAssetMethod.OnCallMethod = new GenericMethodCalledEventHandler(DeleteAsset);
                deleteAssetMethod.InputArguments = CreateInputArguments(deleteAssetMethod, "AssetID", "The ID of the asset to be deleted");

                MethodState getAssetsMethod = CreateMethod(assetManagementFolder, "GetAssets", "GetAssets");
                getAssetsMethod.OnCallMethod = new GenericMethodCalledEventHandler(GetAssets);

                // add everyting to our nodeset
                AddPredefinedNode(SystemContext, assetManagementFolder);
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

        private FolderState CreateFolder(NodeState parent, string path, string name)
        {
            FolderState folder = new FolderState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(path, NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };
            parent?.AddChild(folder);

            return folder;
        }

        private MethodState CreateMethod(NodeState parent, string path, string name)
        {
            MethodState method = new MethodState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(path, NamespaceIndex),
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
            // TODO: update node values for all configured assets
            NodeState node = Find(m_NumberOfManufacturedProductsID);
            BaseDataVariableState variableState = node as BaseDataVariableState;
            if (variableState != null)
            {
                variableState.Value = m_numberOfManufacturedProducts;
                variableState.Timestamp = DateTime.UtcNow;
                variableState.ClearChangeMasks(SystemContext, false);
            }
        }
    }
}
