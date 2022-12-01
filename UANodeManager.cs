
namespace Station.Simulation
{
    using Opc.Ua;
    using Opc.Ua.Export;
    using Opc.Ua.Server;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    public enum StationStatus : int
    {
        Ready = 0,
        WorkInProgress = 1,
        Done = 2,
        Discarded = 3,
        Fault = 4
    }

    public class UANodeManager : CustomNodeManager2
    {
        private ushort m_namespaceIndex;
        private long m_lastUsedId;

        private NodeId m_NumberOfManufacturedProductsID;
        private object m_numberOfManufacturedProducts;

        public UANodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            List<string> namespaceUris = new List<string>();
            namespaceUris.Add("http://opcfoundation.org/UA/Station/");
            NamespaceUris = namespaceUris;

            m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[0]);
            m_lastUsedId = 0;
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

                ImportNodeset2Xml(externalReferences, "Station.NodeSet2.xml");

                AddReverseReferences(externalReferences);
            }
        }

        // Nodeset2 files can be edited using e.g. the SIEMENS OPC UA Modeling Editor (SiOME)
        // see https://support.industry.siemens.com/cs/document/109755133/siemens-opc-ua-modeling-editor-(siome)-for-implementing-opc-ua-companion-specification
        private void ImportNodeset2Xml(IDictionary<NodeId, IList<IReference>> externalReferences, string resourcepath)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();

            Stream stream = new FileStream(resourcepath, FileMode.Open);
            UANodeSet nodeSet = UANodeSet.Read(stream);

            foreach (string namespaceUri in nodeSet.NamespaceUris)
            {
                SystemContext.NamespaceUris.GetIndexOrAppend(namespaceUri);
            }

            nodeSet.Import(SystemContext, predefinedNodes);

            for (int i = 0; i < predefinedNodes.Count; i++)
            {
                AddPredefinedNode(SystemContext, predefinedNodes[i]);
            }
        }

        protected override NodeState AddBehaviourToPredefinedNode(ISystemContext context, NodeState predefinedNode)
        {
            // add behaviour to our methods
            MethodState methodState = predefinedNode as MethodState;
            if (( methodState != null) && (methodState.ModellingRuleId == null))
            {
                if (methodState.DisplayName == "ConfigureAsset")
                {
                    methodState.OnCallMethod = new GenericMethodCalledEventHandler(ConfigureAsset);

                    // define the method's input argument (the serial number)
                    methodState.InputArguments = new PropertyState<Argument[]>(methodState)
                    {
                        NodeId = new NodeId(methodState.BrowseName.Name + "InArgs", NamespaceIndex),
                        BrowseName = BrowseNames.InputArguments
                    };
                    methodState.InputArguments.DisplayName = methodState.InputArguments.BrowseName.Name;
                    methodState.InputArguments.TypeDefinitionId = VariableTypeIds.PropertyType;
                    methodState.InputArguments.ReferenceTypeId = ReferenceTypeIds.HasProperty;
                    methodState.InputArguments.DataType = DataTypeIds.Argument;
                    methodState.InputArguments.ValueRank = ValueRanks.OneDimension;

                    methodState.InputArguments.Value = new Argument[]
                    {
                        new Argument { Name = "SerialNumber", Description = "Serial number of the product to make.",  DataType = DataTypeIds.UInt64, ValueRank = ValueRanks.Scalar }
                    };

                    return predefinedNode;
                }
            }

            // also capture the nodeIDs of our instance variables (i.e. NOT the model!)
            BaseDataVariableState variableState = predefinedNode as BaseDataVariableState;
            if ((variableState != null) && (variableState.ModellingRuleId == null))
            {
                if (variableState.DisplayName == "NumberOfManufacturedProducts")
                {
                    m_NumberOfManufacturedProductsID = variableState.NodeId;
                }
            }

            return predefinedNode;
        }

        private ServiceResult ConfigureAsset(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            throw new NotImplementedException();
        }

        private ServiceResult OpenPressureReleaseValve(ISystemContext context, MethodState method, IList<object> inputArguments, IList<object> outputArguments)
        {
            UpdateNodeValues();

            return ServiceResult.Good;
        }

        private void UpdateNodeValues()
        {
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
