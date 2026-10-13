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

        public MethodState CreateMethod(NodeState parent, string name, ushort namespaceIndex)
        {
            MethodState method = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new Opc.Ua.LocalizedText("en", name),
                Executable = true,
                UserExecutable = true
            };

            parent?.AddChild(method);

            return method;
        }

        public BaseObjectState CreateObject(NodeState parent, string name, ExpandedNodeId type, ushort namespaceIndex, string nodeIdentifier = null)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type.ToString()))
            {
                throw new ArgumentNullException("Cannot create UA object with empty browse name or type definition!");
            }

            // Keep the object's NodeId and BrowseName in the caller-provided namespace
            // (e.g. the asset's namespace) so every node under an asset shares the same
            // index. Falling back to _manager.New() here would place the object in the
            // EdgeTranslator namespace, inconsistent with its sibling variables.
            BaseObjectState obj = new(parent)
            {
                NodeId = new NodeId(nodeIdentifier ?? name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = ExpandedNodeId.ToNodeId(type, _manager.Server.NamespaceUris)
            };

            parent?.AddChild(obj);

            return obj;
        }

        // The well-known DataTypes.* / DataTypeIds.* constants are uint identifiers.
        // SDK 2.0.0 made NodeId a struct with no implicit conversion from uint, so these
        // overloads keep the numeric call sites working without wrapping at each one.
        public PropertyState CreateProperty(NodeState parent, string name, uint dataType, ushort namespaceIndex, bool writeable = false, object value = null)
        {
            return CreateProperty(parent, name, new NodeId(dataType), namespaceIndex, writeable, value);
        }

        public PropertyState CreateProperty(NodeState parent, string name, NodeId dataType, ushort namespaceIndex, bool writeable = false, object value = null)
        {
            PropertyState property = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = new NodeId(ReferenceTypes.Organizes),
                TypeDefinitionId = VariableTypeIds.PropertyType,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new Opc.Ua.LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                AccessLevel = AccessLevels.CurrentRead,
                DataType = dataType,
                // Variant.From requires a compile-time type (its generic overload is
                // constrained to value types) but this factory is deliberately
                // runtime-typed: callers pass string, double, int, bool and IEncodeable
                // instances through the same object parameter. The (object, TypeInfo)
                // constructor is the only API that accepts a runtime-typed value, so the
                // obsoletion is suppressed narrowly here rather than repeated at every
                // call site. Revisit if the SDK adds a non-obsolete equivalent.
#pragma warning disable CS0618 // Type or member is obsolete
                Value = new Variant(value, TypeInfo.Construct(value)),
#pragma warning restore CS0618
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

        public PropertyState<ArrayOf<Argument>> CreateMethodArguments(MethodState methodState, string[] names, string[] descriptions, ExpandedNodeId[] type, bool input, bool array = false, NodeId nodeId = default)
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
                    Description = new LocalizedText(descriptions[i]),
                    DataType = ExpandedNodeId.ToNodeId(type[i], _manager.Server.NamespaceUris),
                    ValueRank = array ? ValueRanks.OneDimension : ValueRanks.Scalar
                });
            }

            // PropertyState<T> became abstract in SDK 2.0.0 and the InputArguments /
            // OutputArguments properties are now strongly typed as
            // PropertyState<ArrayOf<Argument>>. MethodState exposes dedicated factories
            // that build the correctly-typed child, so use those instead of constructing
            // the property directly. assignInstanceNodeIds is false because this method
            // assigns the NodeId itself (either the caller-supplied one or a derived
            // browse-name based id), matching the previous behaviour.
            PropertyState<ArrayOf<Argument>> arguments = input
                ? methodState.CreateOrReplaceInputArguments(_manager.SystemContext, null, false)
                : methodState.CreateOrReplaceOutputArguments(_manager.SystemContext, null, false);

            arguments.NodeId = nodeId.IsNull ? new NodeId(browseName, methodState.NodeId.NamespaceIndex) : nodeId;
            arguments.BrowseName = new QualifiedName(input ? BrowseNames.InputArguments : BrowseNames.OutputArguments);
            arguments.DisplayName = new LocalizedText(input ? BrowseNames.InputArguments : BrowseNames.OutputArguments);
            arguments.TypeDefinitionId = VariableTypeIds.PropertyType;
            arguments.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            arguments.DataType = DataTypeIds.Argument;
            arguments.ValueRank = ValueRanks.OneDimension;
            arguments.Value = new ArrayOf<Argument>(argumentsList.ToArray());

            return arguments;
        }

        public BaseDataVariableState CreateVariable(NodeState parent, string name, uint dataType, ushort namespaceIndex, bool writeable = false, object value = null)
        {
            return CreateVariable(parent, name, new NodeId(dataType), namespaceIndex, writeable, value);
        }

        public BaseDataVariableState CreateVariable(NodeState parent, string name, NodeId dataType, ushort namespaceIndex, bool writeable = false, object value = null)
        {
            BaseDataVariableState variable = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = new NodeId(ReferenceTypes.Organizes),
                NodeId = new NodeId(name, namespaceIndex),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new Opc.Ua.LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                AccessLevel = AccessLevels.CurrentRead,
                DataType = dataType,
                // See the comment in CreateProperty: this factory is runtime-typed, so the
                // obsolete (object, TypeInfo) constructor is the only applicable overload.
#pragma warning disable CS0618 // Type or member is obsolete
                Value = new Variant(value, TypeInfo.Construct(value)),
#pragma warning restore CS0618
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

            if (parent != null)
            {
                NodeId hasWoTComponent = ExpandedNodeId.ToNodeId(new ExpandedNodeId(_cHasWoTComponent, _cWotCon), _manager.Server.NamespaceUris);
                if (!parent.ReferenceExists(hasWoTComponent, false, variable.NodeId))
                {
                    parent.AddReference(hasWoTComponent, false, variable.NodeId);
                }
            }

            return variable;
        }
    }
}
