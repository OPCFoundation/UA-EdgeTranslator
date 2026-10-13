namespace Opc.Ua.Edge.Translator.WotCon
{
    using Opc.Ua.Encoders;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;

    /// <summary>
    /// Reads, from the running server's address space, what a
    /// <c>uav:mapToType</c> names, so a 1.02-style mapping target can be created
    /// the way the 1.02 node manager created it.
    /// </summary>
    /// <remarks>
    /// Companion models (for example PNEM) are loaded as nodes, but no CLR type is
    /// generated for their structures. The registry's structured-field mapping needs
    /// an encodeable type, so <see cref="EnsureStructureRegistered"/> registers the
    /// SDK's dynamic <see cref="Structure"/> built from the DataType's
    /// <c>DataTypeDefinition</c>.
    /// </remarks>
    internal sealed class WotMappingTypeModel
    {
        private readonly IServerInternal _server;

        private readonly HashSet<NodeId> _registered = new();

        private readonly object _gate = new();

        public WotMappingTypeModel(IServerInternal server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <summary>The server's namespace table.</summary>
        public NamespaceTable NamespaceUris => _server.NamespaceUris;

        /// <summary>Reads the NodeClass of <paramref name="nodeId"/>, or Unspecified when unknown.</summary>
        public NodeClass ReadNodeClass(NodeId nodeId)
        {
            DataValue value = Read(nodeId, Attributes.NodeClass);
            return StatusCode.IsGood(value.StatusCode) && value.WrappedValue.TryGetValue(out int nodeClass)
                ? (NodeClass)nodeClass
                : NodeClass.Unspecified;
        }

        /// <summary>
        /// Registers a dynamic encodeable type for the structure DataType
        /// <paramref name="dataTypeId"/> when the server knows no type for it.
        /// Returns false when the DataType is not a structure the server describes.
        /// </summary>
        public bool EnsureStructureRegistered(NodeId dataTypeId)
        {
            lock (_gate)
            {
                if (_registered.Contains(dataTypeId))
                {
                    return true;
                }
            }

            ExpandedNodeId typeId = NodeId.ToExpandedNodeId(dataTypeId, _server.NamespaceUris);
#pragma warning disable UA_NETStandard_1 // the dynamic-type registration API is marked experimental
            if (((IEncodeableTypeLookup)_server.Factory).TryGetEncodeableType(typeId, out IEncodeableType _))
            {
                return true;
            }
#pragma warning restore UA_NETStandard_1

            DataValue definitionValue = Read(dataTypeId, Attributes.DataTypeDefinition);
            if (!StatusCode.IsGood(definitionValue.StatusCode)
                || !definitionValue.WrappedValue.TryGetStructure(out StructureDefinition definition)
                || definition.Fields.IsNull || definition.Fields.Count == 0)
            {
                return false;
            }

            Dictionary<string, BuiltInType> fieldTypes = new(StringComparer.Ordinal);
            foreach (StructureField field in definition.Fields)
            {
                fieldTypes[field.Name] = TypeInfo.GetBuiltInType(field.DataType);
            }

            ExpandedNodeId binaryEncodingId = FindEncoding(dataTypeId, BrowseNames.DefaultBinary);
            ExpandedNodeId xmlEncodingId = FindEncoding(dataTypeId, BrowseNames.DefaultXml);
            QualifiedName browseName = ReadBrowseName(dataTypeId);

            Structure structure = new(
                new XmlQualifiedName(browseName.Name ?? dataTypeId.ToString(), _server.NamespaceUris.GetString(dataTypeId.NamespaceIndex) ?? string.Empty),
                typeId,
                binaryEncodingId,
                xmlEncodingId,
                definition,
                fieldTypes);

            #pragma warning disable UA_NETStandard_1 // the dynamic-type registration API is marked experimental
            IEncodeableFactoryBuilder builder = _server.Factory.Builder;
            builder.AddEncodeableType(typeId, structure);
            if (!binaryEncodingId.IsNull)
            {
                builder.AddEncodeableType(binaryEncodingId, structure);
            }

            if (!xmlEncodingId.IsNull)
            {
                builder.AddEncodeableType(xmlEncodingId, structure);
            }

            builder.Commit();
#pragma warning restore UA_NETStandard_1

            lock (_gate)
            {
                _registered.Add(dataTypeId);
            }

            Log.Logger.Debug("Registered a dynamic structure type for DataType {DataType} ({Name}).", dataTypeId, browseName.Name);
            return true;
        }

        /// <summary>
        /// Finds the Variable declaration of <paramref name="objectTypeId"/> whose
        /// name matches <paramref name="fieldPath"/> (placeholder brackets ignored).
        /// </summary>
        public bool TryFindVariableDeclaration(NodeId objectTypeId, string fieldPath, out QualifiedName browseName, out NodeId dataType, out bool isProperty)
        {
            browseName = QualifiedName.Null;
            dataType = NodeId.Null;
            isProperty = false;

            string wanted = (fieldPath ?? string.Empty).Trim('<', '>');
            NodeId current = objectTypeId;

            // Walk the supertype chain, as an instance carries inherited declarations too.
            for (int depth = 0; depth < 16 && !current.IsNull; depth++)
            {
                foreach (ReferenceDescription reference in Browse(current, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Forward))
                {
                    if (reference.NodeClass != NodeClass.Variable)
                    {
                        continue;
                    }

                    string name = reference.BrowseName.Name ?? string.Empty;
                    if (!string.Equals(name.Trim('<', '>'), wanted, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    NodeId declarationId = ExpandedNodeId.ToNodeId(reference.NodeId, _server.NamespaceUris);
                    DataValue dataTypeValue = Read(declarationId, Attributes.DataType);
                    dataType = dataTypeValue.WrappedValue.TryGetValue(out NodeId id) ? id : DataTypeIds.BaseDataType;
                    browseName = new QualifiedName(name.Trim('<', '>'), reference.BrowseName.NamespaceIndex);
                    isProperty = reference.ReferenceTypeId == ReferenceTypeIds.HasProperty;
                    return true;
                }

                current = SuperTypeOf(current);
            }

            return false;
        }

        private NodeId SuperTypeOf(NodeId typeId)
        {
            foreach (ReferenceDescription reference in Browse(typeId, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse))
            {
                return ExpandedNodeId.ToNodeId(reference.NodeId, _server.NamespaceUris);
            }

            return NodeId.Null;
        }

        private ExpandedNodeId FindEncoding(NodeId dataTypeId, string encodingName)
        {
            foreach (ReferenceDescription reference in Browse(dataTypeId, ReferenceTypeIds.HasEncoding, BrowseDirection.Forward))
            {
                if (string.Equals(reference.BrowseName.Name, encodingName, StringComparison.Ordinal))
                {
                    return reference.NodeId;
                }
            }

            return ExpandedNodeId.Null;
        }

        private QualifiedName ReadBrowseName(NodeId nodeId)
        {
            DataValue value = Read(nodeId, Attributes.BrowseName);
            return value.WrappedValue.TryGetValue(out QualifiedName name) ? name : QualifiedName.Null;
        }

        private DataValue Read(NodeId nodeId, uint attributeId)
        {
            ArrayOf<ReadValueId> reads = new[] { new ReadValueId { NodeId = nodeId, AttributeId = attributeId } };
            (ArrayOf<DataValue> values, _) = AsyncBridge.RunSync(async () =>
                await _server.NodeManager.ReadAsync(CreateContext(), 0, TimestampsToReturn.Neither, reads, CancellationToken.None).ConfigureAwait(false));

            return values.IsNull || values.Count == 0 ? DataValue.FromStatusCode(StatusCodes.BadNoData) : values[0];
        }

        private IEnumerable<ReferenceDescription> Browse(NodeId nodeId, NodeId referenceTypeId, BrowseDirection direction)
        {
            ArrayOf<BrowseDescription> nodes = new[]
            {
                new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = direction,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                }
            };

            (ArrayOf<BrowseResult> results, _) = AsyncBridge.RunSync(async () =>
                await _server.NodeManager.BrowseAsync(CreateContext(), new ViewDescription(), 0, nodes, CancellationToken.None).ConfigureAwait(false));

            if (results.IsNull || results.Count == 0 || StatusCode.IsBad(results[0].StatusCode) || results[0].References.IsNull)
            {
                return Array.Empty<ReferenceDescription>();
            }

            return results[0].References.ToArray();
        }

        private static OperationContext CreateContext()
        {
            RequestHeader header = new() { Timestamp = DateTime.UtcNow, TimeoutHint = 10000 };
            return new OperationContext(header, new SecureChannelContext(string.Empty, null, RequestEncoding.Binary), RequestType.Browse, RequestLifetime.None, new UserIdentity());
        }
    }
}
