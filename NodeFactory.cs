namespace Opc.Ua.Edge.Translator
{
    using System;
    using System.Collections.Generic;

    public class NodeFactory
    {
        private readonly UANodeManager _manager;

        private const uint _cHasWoTComponent = 142;
        private const string _cWotCon = "http://opcfoundation.org/UA/WoT-Con/";

        public NodeFactory(UANodeManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        public MethodState CreateMethod(NodeState parent, string name)
        {
            MethodState method = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(name, _manager.NamespaceIndex),
                BrowseName = new QualifiedName(name, _manager.NamespaceIndex),
                DisplayName = new Opc.Ua.LocalizedText("en", name),
                Executable = true,
                UserExecutable = true
            };

            parent?.AddChild(method);

            return method;
        }

        public BaseObjectState CreateObject(NodeState parent, string name, ExpandedNodeId type)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type.ToString()))
            {
                throw new ArgumentNullException("Cannot create UA object with empty browse name or type definition!");
            }

            BaseObjectState obj = new(parent)
            {
                BrowseName = name,
                DisplayName = name,
                TypeDefinitionId = ExpandedNodeId.ToNodeId(type, _manager.Server.NamespaceUris)
            };

            obj.NodeId = _manager.New(_manager.SystemContext, obj);

            parent?.AddChild(obj);

            return obj;
        }

        public PropertyState CreateProperty(NodeState parent, string name, ExpandedNodeId type, ushort namespaceIndex, bool writeable = false, object value = null)
        {
            PropertyState property = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new Opc.Ua.LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                AccessLevel = AccessLevels.CurrentRead,
                DataType = ExpandedNodeId.ToNodeId(type, _manager.Server.NamespaceUris),
                Value = value,
                OnReadValue = _manager.OnReadValue
            };

            if (writeable)
            {
                property.AccessLevel = AccessLevels.CurrentReadOrWrite;
                property.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                property.UserWriteMask = AttributeWriteMask.ValueForVariableType;
                property.WriteMask = AttributeWriteMask.ValueForVariableType;
                property.OnWriteValue = _manager.OnWriteValue;
            }

            parent?.AddChild(property);

            return property;
        }

        public PropertyState<Argument[]> CreateMethodArguments(MethodState methodState, string[] names, string[] descriptions, ExpandedNodeId[] type, bool input, bool array = false, NodeId nodeId = null)
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
                    DataType = ExpandedNodeId.ToNodeId(type[i], _manager.Server.NamespaceUris),
                    ValueRank = array ? ValueRanks.OneDimension : ValueRanks.Scalar
                });
            }

            PropertyState<Argument[]> arguments = new(methodState)
            {
                NodeId = (nodeId == null)? new NodeId(browseName, _manager.NamespaceIndex) : nodeId,
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

        public BaseDataVariableState CreateVariable(NodeState parent, string name, ExpandedNodeId type, ushort namespaceIndex, bool writeable = false, object value = null)
        {
            BaseDataVariableState variable = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseVariableType,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new Opc.Ua.LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                AccessLevel = AccessLevels.CurrentRead,
                DataType = ExpandedNodeId.ToNodeId(type, _manager.Server.NamespaceUris),
                Value = value,
                OnReadValue = _manager.OnReadValue
            };

            if (writeable)
            {
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserWriteMask = AttributeWriteMask.ValueForVariableType;
                variable.WriteMask = AttributeWriteMask.ValueForVariableType;
                variable.OnWriteValue = _manager.OnWriteValue;
            }

            parent?.AddChild(variable);

            parent?.AddReference(ExpandedNodeId.ToNodeId(new ExpandedNodeId(_cHasWoTComponent, _cWotCon), _manager.Server.NamespaceUris), false, variable.NodeId);

            return variable;
        }
    }
}
