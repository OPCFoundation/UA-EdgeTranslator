namespace Opc.Ua.Edge.Translator.Diagnostics
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Read-only, in-process browser over the running server's address space.
    /// <para>
    /// The UA Cloud Library explorer this is modelled on talks to a *remote*
    /// server over a client session. The translator hosts its own server, so
    /// rather than looping back through a real OPC UA connection (which would
    /// need certificate trust plus credentials) we go straight at the
    /// <see cref="MasterNodeManager"/> that the server already exposes. That
    /// keeps browsing free of network and security round-trips.
    /// </para>
    /// <para>
    /// Like <see cref="DiagnosticsService"/>, this type holds no state and is
    /// defensive throughout: a failure to browse or read one node must never
    /// throw the page, it degrades to an empty result or an error string.
    /// </para>
    /// </summary>
    public sealed class AddressSpaceService
    {
        // Guard against a pathological/looping address space starving the UI.
        private const uint _maxReferencesPerNode = 1000;

        /// <summary>
        /// True when the OPC UA server is up and its address space can be browsed.
        /// </summary>
        public bool IsAvailable => TryGetServer(out _);

        /// <summary>
        /// Returns the namespaces registered in the live namespace table. Index 0
        /// is always the OPC UA core namespace.
        /// </summary>
        public IReadOnlyList<NamespaceInfo> GetNamespaces()
        {
            List<NamespaceInfo> namespaces = [];

            try
            {
                if (!TryGetServer(out IServerInternal server))
                {
                    return namespaces;
                }

                NamespaceTable table = server.NamespaceUris;
                string[] uris = table?.ToArray() ?? [];

                for (int i = 0; i < uris.Length; i++)
                {
                    namespaces.Add(new NamespaceInfo(i, uris[i] ?? string.Empty));
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to enumerate the server namespace table for the Explorer.");
            }

            return namespaces;
        }

        /// <summary>
        /// Returns the root of the tree.
        /// <para>
        /// This is the server's Root folder rather than the Objects folder, so
        /// the Types tree (ObjectTypes, VariableTypes, DataTypes,
        /// ReferenceTypes) and Views are browsable alongside the instance
        /// hierarchy.
        /// </para>
        /// </summary>
        public async Task<IReadOnlyList<AddressSpaceNode>> GetRootNodesAsync(CancellationToken cancellationToken = default)
        {
            AddressSpaceNode root = await GetNodeAsync(ObjectIds.RootFolder, cancellationToken).ConfigureAwait(false);

            return root is null ? [] : [root];
        }

        /// <summary>
        /// Node ids the tree should open on first load. Rooted at Root, the
        /// Objects folder is the instance hierarchy an operator wants to see
        /// first, so it is opened for them rather than costing an extra click.
        /// </summary>
        public IReadOnlyList<string> GetDefaultExpandedNodeIds() =>
        [
            ObjectIds.RootFolder.ToString(),
            ObjectIds.ObjectsFolder.ToString()
        ];

        /// <summary>
        /// Browses the hierarchical children of <paramref name="nodeId"/>.
        /// </summary>
        public async Task<IReadOnlyList<AddressSpaceNode>> BrowseChildrenAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || !TryParseNodeId(nodeId, out NodeId parsed))
            {
                return [];
            }

            return await BrowseChildrenAsync(parsed, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the current value of many variable nodes in one operation.
        /// <para>
        /// The Explorer polls this on a timer to show live values next to the
        /// nodes in the tree, so it is deliberately a single batched read: one
        /// call per tick regardless of how many nodes are visible, rather than
        /// one round-trip per node.
        /// </para>
        /// </summary>
        /// <returns>
        /// A map of node id to formatted value. Nodes that are not variables,
        /// or that cannot be read, are omitted so the caller simply shows
        /// nothing for them.
        /// </returns>
        public async Task<IReadOnlyDictionary<string, string>> ReadValuesAsync(IEnumerable<string> nodeIds, CancellationToken cancellationToken = default)
        {
            Dictionary<string, string> values = new(StringComparer.Ordinal);

            if (nodeIds is null)
            {
                return values;
            }

            try
            {
                if (!TryGetServer(out IServerInternal server))
                {
                    return values;
                }

                List<string> ids = [];
                ReadValueIdCollection reads = [];

                foreach (string nodeId in nodeIds)
                {
                    if (string.IsNullOrWhiteSpace(nodeId) || !TryParseNodeId(nodeId, out NodeId parsed))
                    {
                        continue;
                    }

                    ids.Add(nodeId);

                    // Value and DataType are read as a pair: a structure value is
                    // an opaque ExtensionObject, and its DataType is what lets us
                    // resolve the StructureDefinition needed to decode it. Both
                    // still travel in the same single batched read.
                    reads.Add(NewRead(parsed, Attributes.Value));
                    reads.Add(NewRead(parsed, Attributes.DataType));
                }

                if (reads.Count == 0)
                {
                    return values;
                }

                DataValueCollection results = await ReadAsync(server, reads, cancellationToken).ConfigureAwait(false);

                if (results is null)
                {
                    return values;
                }

                for (int i = 0; i < ids.Count; i++)
                {
                    int valueIndex = i * 2;
                    int dataTypeIndex = valueIndex + 1;

                    if (valueIndex >= results.Count)
                    {
                        break;
                    }

                    DataValue result = results[valueIndex];

                    // Non-variable nodes come back as BadAttributeIdInvalid /
                    // BadNodeIdUnknown. Those are expected (the caller passes in
                    // whatever is on screen), so skip them silently rather than
                    // decorating Objects and Methods with an error string.
                    if (result is null || IsMissingValueAttribute(result.StatusCode))
                    {
                        continue;
                    }

                    if (result.Value is ExtensionObject extension && dataTypeIndex < results.Count)
                    {
                        IReadOnlyList<StructureFieldValue> fields =
                            DecodeStructure(server, extension, results[dataTypeIndex].Value as NodeId);

                        if (fields.Count > 0)
                        {
                            values[ids[i]] = FormatStructureSummary(fields);
                            continue;
                        }
                    }

                    values[ids[i]] = FormatValue(result);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to batch-read values for the Explorer.");
            }

            return values;
        }

        private static bool IsMissingValueAttribute(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadAttributeIdInvalid
                || statusCode == StatusCodes.BadNodeIdUnknown
                || statusCode == StatusCodes.BadNodeIdInvalid;
        }

        private async Task<IReadOnlyList<AddressSpaceNode>> BrowseChildrenAsync(NodeId nodeId, CancellationToken cancellationToken)
        {
            List<AddressSpaceNode> children = [];

            try
            {
                if (!TryGetServer(out IServerInternal server))
                {
                    return children;
                }

                ReferenceDescriptionCollection references = await BrowseAsync(server, nodeId, cancellationToken).ConfigureAwait(false);

                // A node can be reached through more than one hierarchical
                // reference (for example both Organizes and HasComponent), so
                // the same target can come back several times. The tree keys
                // siblings by node id, and duplicate keys are a hard render
                // error in Blazor, so collapse them to the first occurrence.
                HashSet<string> seen = new(StringComparer.Ordinal);
                List<NodeId> targets = [];

                foreach (ReferenceDescription reference in references)
                {
                    NodeId targetId = ExpandedNodeId.ToNodeId(reference.NodeId, server.NamespaceUris);
                    if (NodeId.IsNull(targetId))
                    {
                        // External/unreachable target (a server-relative id we cannot resolve).
                        continue;
                    }

                    string id = targetId.ToString();
                    if (!seen.Add(id))
                    {
                        continue;
                    }

                    children.Add(new AddressSpaceNode
                    {
                        Id = id,
                        Text = GetNodeText(reference),
                        NodeClass = reference.NodeClass.ToString(),
                        NamespaceIndex = targetId.NamespaceIndex,

                        // Provisionally true; resolved accurately below so the
                        // tree never offers an expander that opens onto nothing.
                        HasChildren = true
                    });

                    targets.Add(targetId);
                }

                await ResolveHasChildrenAsync(server, children, targets, cancellationToken).ConfigureAwait(false);

                // Tie-break on the node id so that siblings sharing a display
                // name keep a stable, repeatable order between refreshes.
                children.Sort(static (a, b) =>
                {
                    int byText = string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);

                    return byText != 0 ? byText : string.CompareOrdinal(a.Id, b.Id);
                });
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to browse children of {NodeId} for the Explorer.", nodeId);
            }

            return children;
        }

        /// <summary>
        /// Determines, for each freshly browsed child, whether it actually has
        /// hierarchical children of its own.
        /// <para>
        /// This is what lets the tree hide the expander on leaf nodes instead of
        /// offering one that opens onto "(no children)". The whole set is
        /// resolved in a single Browse call — <c>BrowseAsync</c> accepts a
        /// collection of descriptions — so one expansion still costs one extra
        /// round-trip rather than one per child. Only the first reference of
        /// each node is needed, hence <c>maxReferencesPerNode: 1</c>.
        /// </para>
        /// </summary>
        private static async Task ResolveHasChildrenAsync(
            IServerInternal server,
            List<AddressSpaceNode> children,
            List<NodeId> targets,
            CancellationToken cancellationToken)
        {
            if (targets.Count == 0)
            {
                return;
            }

            try
            {
                BrowseDescriptionCollection nodesToBrowse = [];

                foreach (NodeId target in targets)
                {
                    nodesToBrowse.Add(new BrowseDescription
                    {
                        NodeId = target,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.None
                    });
                }

                (BrowseResultCollection results, _) = await server.NodeManager.BrowseAsync(
                    CreateContext(),
                    null,
                    1,
                    nodesToBrowse,
                    cancellationToken).ConfigureAwait(false);

                if (results is null)
                {
                    return;
                }

                for (int i = 0; i < children.Count && i < results.Count; i++)
                {
                    BrowseResult result = results[i];

                    // On a bad status leave the expander in place: better to
                    // offer an expansion that turns out empty than to hide a
                    // branch the user cannot then reach.
                    if (result is null || StatusCode.IsBad(result.StatusCode))
                    {
                        continue;
                    }

                    children[i].HasChildren = result.References is { Count: > 0 };
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to resolve child counts for the Explorer.");
            }
        }

        /// <summary>
        /// Reads the attribute detail set shown in the Explorer's right-hand panel.
        /// </summary>
        public async Task<NodeDetail> GetNodeDetailAsync(string nodeId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || !TryParseNodeId(nodeId, out NodeId parsed))
            {
                return new NodeDetail { NodeId = nodeId ?? string.Empty, Error = "The node id could not be parsed." };
            }

            try
            {
                if (!TryGetServer(out IServerInternal server))
                {
                    return new NodeDetail { NodeId = nodeId, Error = "The OPC UA server is not running." };
                }

                // Attributes common to every node class.
                ReadValueIdCollection reads =
                [
                    NewRead(parsed, Attributes.NodeClass),
                    NewRead(parsed, Attributes.BrowseName),
                    NewRead(parsed, Attributes.DisplayName),
                    NewRead(parsed, Attributes.Description),
                    NewRead(parsed, Attributes.WriteMask),
                    NewRead(parsed, Attributes.UserWriteMask),

                    // Variable-only attributes. Unsupported attributes come back
                    // as BadAttributeIdInvalid rather than failing the batch, so
                    // a single round-trip covers every node class.
                    NewRead(parsed, Attributes.Value),
                    NewRead(parsed, Attributes.DataType),
                    NewRead(parsed, Attributes.ValueRank),
                    NewRead(parsed, Attributes.AccessLevel),
                    NewRead(parsed, Attributes.UserAccessLevel),
                ];

                DataValueCollection values = await ReadAsync(server, reads, cancellationToken).ConfigureAwait(false);

                if (values is null || values.Count < reads.Count)
                {
                    return new NodeDetail { NodeId = nodeId, Error = "The node attributes could not be read." };
                }

                string displayName = FormatValue(values[2]);
                string nodeClass = FormatNodeClass(values[0]);

                List<NodeAttribute> attributes =
                [
                    new NodeAttribute("NodeId", parsed.ToString()),
                    new NodeAttribute("NodeClass", nodeClass),
                    new NodeAttribute("BrowseName", FormatValue(values[1])),
                    new NodeAttribute("DisplayName", displayName),
                    new NodeAttribute("Description", FormatValue(values[3])),
                    new NodeAttribute("WriteMask", FormatValue(values[4])),
                    new NodeAttribute("UserWriteMask", FormatValue(values[5])),
                ];

                List<NodeAttribute> valueAttributes = [];
                IReadOnlyList<StructureFieldValue> fields = [];

                // Only surface the value block when the node actually has one.
                if (StatusCode.IsGood(values[6].StatusCode) || StatusCode.IsGood(values[7].StatusCode))
                {
                    // A structure arrives as an ExtensionObject holding an opaque
                    // binary body. Decode it so the panel can show named fields
                    // instead of a byte count.
                    if (values[6].Value is ExtensionObject extension)
                    {
                        fields = DecodeStructure(server, extension, values[7].Value as NodeId);
                    }

                    valueAttributes.Add(new NodeAttribute("Value", fields.Count > 0 ? FormatStructureSummary(fields) : FormatValue(values[6])));
                    valueAttributes.Add(new NodeAttribute("Status", values[6].StatusCode.ToString()));
                    valueAttributes.Add(new NodeAttribute("SourceTimestamp", FormatTimestamp(values[6].SourceTimestamp)));
                    valueAttributes.Add(new NodeAttribute("ServerTimestamp", FormatTimestamp(values[6].ServerTimestamp)));
                    valueAttributes.Add(new NodeAttribute("DataType", FormatDataType(values[7], server)));
                    valueAttributes.Add(new NodeAttribute("ValueRank", FormatValueRank(values[8])));
                    valueAttributes.Add(new NodeAttribute("AccessLevel", FormatAccessLevel(values[9])));
                    valueAttributes.Add(new NodeAttribute("UserAccessLevel", FormatAccessLevel(values[10])));
                }

                return new NodeDetail
                {
                    NodeId = parsed.ToString(),
                    DisplayName = string.IsNullOrEmpty(displayName) ? parsed.ToString() : displayName,
                    NodeClass = nodeClass,
                    NamespaceUri = GetNamespaceUri(server, parsed.NamespaceIndex),
                    Attributes = attributes,
                    ValueAttributes = valueAttributes,
                    Fields = fields,
                    References = await GetReferencesAsync(server, parsed, cancellationToken).ConfigureAwait(false)
                };
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to read details of {NodeId} for the Explorer.", nodeId);

                return new NodeDetail { NodeId = nodeId, Error = "The node could not be read: " + ex.Message };
            }
        }

        /// <summary>
        /// Builds a single tree node for an arbitrary node id, used to seed the root.
        /// </summary>
        private async Task<AddressSpaceNode> GetNodeAsync(NodeId nodeId, CancellationToken cancellationToken)
        {
            try
            {
                if (!TryGetServer(out IServerInternal server))
                {
                    return null;
                }

                ReadValueIdCollection reads = [NewRead(nodeId, Attributes.DisplayName), NewRead(nodeId, Attributes.NodeClass)];
                DataValueCollection values = await ReadAsync(server, reads, cancellationToken).ConfigureAwait(false);

                string text = values is { Count: 2 } ? FormatValue(values[0]) : string.Empty;

                return new AddressSpaceNode
                {
                    Id = nodeId.ToString(),
                    Text = string.IsNullOrEmpty(text) ? nodeId.ToString() : text,
                    NodeClass = values is { Count: 2 } ? FormatNodeClass(values[1]) : NodeClass.Object.ToString(),
                    NamespaceIndex = nodeId.NamespaceIndex,
                    HasChildren = true
                };
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to resolve root node {NodeId} for the Explorer.", nodeId);

                return null;
            }
        }

        private async Task<IReadOnlyList<NodeReferenceInfo>> GetReferencesAsync(IServerInternal server, NodeId nodeId, CancellationToken cancellationToken)
        {
            List<NodeReferenceInfo> result = [];

            try
            {
                // Both directions, all reference types, so the panel shows the
                // node's full relationship set rather than just its children.
                ReferenceDescriptionCollection references = await BrowseAsync(
                    server,
                    nodeId,
                    cancellationToken,
                    ReferenceTypeIds.References,
                    BrowseDirection.Both).ConfigureAwait(false);

                foreach (ReferenceDescription reference in references)
                {
                    result.Add(new NodeReferenceInfo(
                        GetReferenceTypeName(server, reference.ReferenceTypeId),
                        reference.IsForward ? "Forward" : "Inverse",
                        GetNodeText(reference),
                        reference.NodeId?.ToString() ?? string.Empty));
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to read references of {NodeId} for the Explorer.", nodeId);
            }

            return result;
        }

        private static async Task<ReferenceDescriptionCollection> BrowseAsync(
            IServerInternal server,
            NodeId nodeId,
            CancellationToken cancellationToken,
            NodeId referenceTypeId = null,
            BrowseDirection direction = BrowseDirection.Forward)
        {
            BrowseDescriptionCollection nodesToBrowse =
            [
                new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = direction,
                    ReferenceTypeId = referenceTypeId ?? ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];

            (BrowseResultCollection results, _) = await server.NodeManager.BrowseAsync(
                CreateContext(),
                null,
                _maxReferencesPerNode,
                nodesToBrowse,
                cancellationToken).ConfigureAwait(false);

            if (results is null || results.Count == 0 || StatusCode.IsBad(results[0].StatusCode))
            {
                return [];
            }

            return results[0].References ?? [];
        }

        private static async Task<DataValueCollection> ReadAsync(IServerInternal server, ReadValueIdCollection reads, CancellationToken cancellationToken)
        {
            (DataValueCollection values, _) = await server.NodeManager.ReadAsync(
                CreateContext(),
                0,
                TimestampsToReturn.Both,
                reads,
                cancellationToken).ConfigureAwait(false);

            return values;
        }

        /// <summary>
        /// Builds a synthetic operation context. There is no client session
        /// behind the diagnostics UI, so we present the request as an internal
        /// system-user read. Access to the dashboard is already gated by HTTP
        /// Basic auth against the same credentials the OPC UA server uses
        /// (see <c>DiagnosticsWebHost</c>), and this service is read-only.
        /// </summary>
        private static OperationContext CreateContext()
        {
            RequestHeader header = new()
            {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = 10000,
                ReturnDiagnostics = 0
            };

            return new OperationContext(header, new SecureChannelContext(string.Empty, null, RequestEncoding.Binary), RequestType.Browse, new UserIdentity());
        }

        private static ReadValueId NewRead(NodeId nodeId, uint attributeId) => new() { NodeId = nodeId, AttributeId = attributeId };

        private static bool TryGetServer(out IServerInternal server)
        {
            server = null;

            try
            {
                server = UANodeManager.Instance?.Server;

                return server?.NodeManager is not null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryParseNodeId(string nodeId, out NodeId parsed)
        {
            parsed = null;

            try
            {
                parsed = NodeId.Parse(nodeId);

                return !NodeId.IsNull(parsed);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetNodeText(ReferenceDescription reference)
        {
            string text = reference.DisplayName?.Text;

            if (string.IsNullOrEmpty(text))
            {
                text = reference.BrowseName?.Name;
            }

            return string.IsNullOrEmpty(text) ? reference.NodeId?.ToString() ?? string.Empty : text;
        }

        private static string GetReferenceTypeName(IServerInternal server, NodeId referenceTypeId)
        {
            if (NodeId.IsNull(referenceTypeId))
            {
                return string.Empty;
            }

            try
            {
                // The browse name of the reference type is far more useful than
                // its numeric id ("Organizes" vs "i=35").
                object target = server.NodeManager.GetManagerHandle(referenceTypeId, out INodeManager nodeManager);

                if (target is not null && nodeManager is not null)
                {
                    NodeMetadata metadata = nodeManager.GetNodeMetadata(
                        CreateContext(),
                        target,
                        BrowseResultMask.BrowseName);

                    if (!string.IsNullOrEmpty(metadata?.BrowseName?.Name))
                    {
                        return metadata.BrowseName.Name;
                    }
                }
            }
            catch (Exception)
            {
                // fall through to the raw id
            }

            return referenceTypeId.ToString();
        }

        private static string GetNamespaceUri(IServerInternal server, ushort namespaceIndex)
        {
            try
            {
                return server.NamespaceUris?.GetString(namespaceIndex) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string FormatNodeClass(DataValue value)
        {
            if (value?.Value is int raw && Enum.IsDefined(typeof(NodeClass), raw))
            {
                return ((NodeClass)raw).ToString();
            }

            return value?.Value?.ToString() ?? string.Empty;
        }

        private static string FormatDataType(DataValue value, IServerInternal server)
        {
            if (value?.Value is NodeId dataTypeId)
            {
                // Built-in types resolve to a friendly name; anything else falls
                // back to the qualified id.
                string name = TypeInfo.GetBuiltInType(dataTypeId).ToString();

                return name == BuiltInType.Null.ToString()
                    ? GetNamespaceQualifiedId(server, dataTypeId)
                    : name;
            }

            return FormatValue(value);
        }

        private static string GetNamespaceQualifiedId(IServerInternal server, NodeId nodeId)
        {
            string uri = GetNamespaceUri(server, nodeId.NamespaceIndex);

            return string.IsNullOrEmpty(uri) ? nodeId.ToString() : string.Create(CultureInfo.InvariantCulture, $"{nodeId} ({uri})");
        }

        private static string FormatValueRank(DataValue value)
        {
            if (value?.Value is int rank)
            {
                return rank switch
                {
                    ValueRanks.Scalar => "Scalar",
                    ValueRanks.OneDimension => "OneDimension",
                    ValueRanks.OneOrMoreDimensions => "OneOrMoreDimensions",
                    ValueRanks.Any => "Any",
                    ValueRanks.ScalarOrOneDimension => "ScalarOrOneDimension",
                    _ => rank.ToString(CultureInfo.InvariantCulture)
                };
            }

            return FormatValue(value);
        }

        private static string FormatAccessLevel(DataValue value)
        {
            if (value?.Value is byte level)
            {
                List<string> parts = [];

                if ((level & AccessLevels.CurrentRead) != 0)
                {
                    parts.Add("Read");
                }

                if ((level & AccessLevels.CurrentWrite) != 0)
                {
                    parts.Add("Write");
                }

                if ((level & AccessLevels.HistoryRead) != 0)
                {
                    parts.Add("HistoryRead");
                }

                if ((level & AccessLevels.HistoryWrite) != 0)
                {
                    parts.Add("HistoryWrite");
                }

                return parts.Count == 0 ? "None" : string.Join(", ", parts);
            }

            return FormatValue(value);
        }

        private static string FormatTimestamp(DateTime timestamp)
        {
            return timestamp == DateTime.MinValue
                ? string.Empty
                : timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Decodes a structure (UDT) value into its named fields.
        /// <para>
        /// The translator stores structures as an <see cref="ExtensionObject"/>
        /// whose body is a positionally binary-encoded <c>byte[]</c> written by
        /// <c>UANodeManager.EncodeField</c>. There is no self-describing header,
        /// so the only way to read it back is to walk the
        /// <see cref="StructureDefinition"/> fields in declaration order using
        /// the same type switch the encoder used. This mirror must stay in
        /// lockstep with <c>EncodeField</c>.
        /// </para>
        /// </summary>
        private static IReadOnlyList<StructureFieldValue> DecodeStructure(IServerInternal server, ExtensionObject extension, NodeId dataTypeId)
        {
            List<StructureFieldValue> fields = [];

            if (extension?.Body is not byte[] body || body.Length == 0)
            {
                return fields;
            }

            StructureDefinition definition = GetStructureDefinition(dataTypeId);

            if (definition?.Fields is null || definition.Fields.Count == 0)
            {
                return fields;
            }

            try
            {
                using BinaryDecoder decoder = new(body, new ServiceMessageContext(Program.Telemetry)
                {
                    NamespaceUris = server.NamespaceUris,
                    Factory = server.Factory
                });

                foreach (StructureField field in definition.Fields)
                {
                    if (!TryDecodeField(decoder, field, out string value, out string typeName))
                    {
                        // An unsupported field type means every following field
                        // would be read at the wrong offset, so stop rather than
                        // present garbage.
                        fields.Add(new StructureFieldValue(field.Name, typeName, "(unsupported type)"));
                        break;
                    }

                    fields.Add(new StructureFieldValue(field.Name, typeName, value));
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to decode structure value for data type {DataType}.", dataTypeId);
            }

            return fields;
        }

        /// <summary>
        /// Reads one field. Mirrors the type switch in
        /// <c>UANodeManager.EncodeField</c>; returns false for anything that
        /// encoder cannot write, since the byte offsets would then be unknown.
        /// </summary>
        private static bool TryDecodeField(BinaryDecoder decoder, StructureField field, out string value, out string typeName)
        {
            value = string.Empty;
            typeName = string.Empty;

            if (field?.DataType is null || field.DataType.Identifier is not uint id)
            {
                return false;
            }

            switch (id)
            {
                case DataTypes.Float:
                    typeName = nameof(BuiltInType.Float);
                    value = decoder.ReadFloat(field.Name).ToString(CultureInfo.InvariantCulture);
                    return true;

                case DataTypes.Double:
                    typeName = nameof(BuiltInType.Double);
                    value = decoder.ReadDouble(field.Name).ToString(CultureInfo.InvariantCulture);
                    return true;

                case DataTypes.Boolean:
                    typeName = nameof(BuiltInType.Boolean);
                    value = decoder.ReadBoolean(field.Name).ToString();
                    return true;

                case DataTypes.SByte:
                    typeName = nameof(BuiltInType.SByte);
                    value = decoder.ReadSByte(field.Name).ToString(CultureInfo.InvariantCulture);
                    return true;

                case DataTypes.Byte:
                    typeName = nameof(BuiltInType.Byte);
                    value = decoder.ReadByte(field.Name).ToString(CultureInfo.InvariantCulture);
                    return true;

                case DataTypes.Int16:
                    typeName = nameof(BuiltInType.Int16);
                    value = decoder.ReadInt16(field.Name).ToString(CultureInfo.InvariantCulture);
                    return true;

                case DataTypes.UInt16:
                    typeName = nameof(BuiltInType.UInt16);
                    value = decoder.ReadUInt16(field.Name).ToString(CultureInfo.InvariantCulture);
                    return true;

                case DataTypes.Int32:
                    typeName = nameof(BuiltInType.Int32);
                    value = decoder.ReadInt32(field.Name).ToString(CultureInfo.InvariantCulture);
                    return true;

                case DataTypes.UInt32:
                    typeName = nameof(BuiltInType.UInt32);
                    value = decoder.ReadUInt32(field.Name).ToString(CultureInfo.InvariantCulture);
                    return true;

                case DataTypes.Int64:
                    typeName = nameof(BuiltInType.Int64);
                    value = decoder.ReadInt64(field.Name).ToString(CultureInfo.InvariantCulture);
                    return true;

                case DataTypes.UInt64:
                    typeName = nameof(BuiltInType.UInt64);
                    value = decoder.ReadUInt64(field.Name).ToString(CultureInfo.InvariantCulture);
                    return true;

                case DataTypes.String:
                    typeName = nameof(BuiltInType.String);
                    value = decoder.ReadString(field.Name) ?? string.Empty;
                    return true;

                default:
                    typeName = field.DataType.ToString();
                    return false;
            }
        }

        /// <summary>
        /// Resolves the <see cref="StructureDefinition"/> that describes a
        /// structure data type, using the same node lookup the node manager
        /// performs when it encodes the value.
        /// </summary>
        private static StructureDefinition GetStructureDefinition(NodeId dataTypeId)
        {
            if (NodeId.IsNull(dataTypeId))
            {
                return null;
            }

            try
            {
                return UANodeManager.Instance?.Find(dataTypeId) is DataTypeState dataType
                    ? dataType.DataTypeDefinition?.Body as StructureDefinition
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Renders decoded structure fields as a compact one-line summary for
        /// the tree, e.g. <c>{ Speed = 12.5, Running = True }</c>.
        /// </summary>
        private static string FormatStructureSummary(IReadOnlyList<StructureFieldValue> fields)
        {
            const int maxFields = 6;

            IEnumerable<string> parts = fields.Take(maxFields).Select(f => $"{f.Name} = {f.Value}");
            string joined = string.Join(", ", parts);

            return fields.Count > maxFields
                ? string.Create(CultureInfo.InvariantCulture, $"{{ {joined}, ... ({fields.Count} fields) }}")
                : string.Create(CultureInfo.InvariantCulture, $"{{ {joined} }}");
        }

        private static string FormatValue(DataValue value)
        {
            if (value is null)
            {
                return string.Empty;
            }

            // Description, WriteMask and UserWriteMask are optional in OPC UA.
            // A node that does not implement an attribute answers
            // BadAttributeIdInvalid; that means "not present", not "error", so
            // render it as an empty cell rather than leaking a status code.
            if (IsMissingValueAttribute(value.StatusCode))
            {
                return string.Empty;
            }

            // Any other bad status is genuine and worth surfacing: in
            // provisioning mode tag reads are deliberately rejected
            // (see UANodeManager.OnReadValue).
            if (StatusCode.IsBad(value.StatusCode))
            {
                return value.StatusCode.ToString();
            }

            return FormatVariant(value.Value);
        }

        private static string FormatVariant(object value)
        {
            switch (value)
            {
                case null:
                    return string.Empty;

                case LocalizedText text:
                    return text.Text ?? string.Empty;

                case QualifiedName name:
                    return name.Name ?? string.Empty;

                case byte[] bytes:
                    return string.Create(CultureInfo.InvariantCulture, $"byte[{bytes.Length}]");

                case Array array:
                    // Keep long arrays from flooding the panel.
                    IEnumerable<string> items = array.Cast<object>().Take(20).Select(FormatVariant);
                    string joined = string.Join(", ", items);

                    return array.Length > 20
                        ? string.Create(CultureInfo.InvariantCulture, $"[{joined}, ... ({array.Length} items)]")
                        : string.Create(CultureInfo.InvariantCulture, $"[{joined}]");

                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);

                default:
                    return value.ToString() ?? string.Empty;
            }
        }
    }
}
