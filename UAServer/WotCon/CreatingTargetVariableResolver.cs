namespace Opc.Ua.Edge.Translator.WotCon
{
    using global::Opc.Ua.WotCon.Bindings;
    using global::Opc.Ua.WotCon.Server.Materialization;
    using Opc.Ua.Server.Fluent;
    using Serilog;
    using System;

    /// <summary>
    /// Target-variable resolver that honours an authored <c>uav:mapToNodeId</c>
    /// whose Variable the Thing Description itself does not declare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The package's <see cref="WotTargetVariableResolver"/> only looks a target up:
    /// <c>uav:mapToNodeId</c> must name a Variable that already exists. Thing
    /// Descriptions written for the WoT Connectivity 1.02 asset management use the
    /// term the other way round - "create this Variable, of <c>uav:mapToType</c>,
    /// and feed <c>uav:mapByFieldPath</c> into it" - so several properties (for
    /// example the three phases of a PNEM <c>VoltageL-N</c>) share one structured
    /// Variable that nothing else creates.
    /// </para>
    /// <para>
    /// This resolver first defers to the package's resolver, so a document that
    /// already declares its targets resolves exactly as before. When the named
    /// Variable does not exist, it creates it with that NodeId, below the Objects
    /// folder of the Thing's address space, typed by <c>uav:mapToType</c> when one
    /// is authored. Later properties mapping to the same NodeId find the created
    /// Variable and share it, so structured fields are combined into one value.
    /// </para>
    /// </remarks>
    public sealed class CreatingTargetVariableResolver : IWotTargetVariableResolver
    {
        private readonly IWotTargetVariableResolver _inner;

        private readonly WotMappingTypeModel _types;

        /// <summary>
        /// The node each authored target is created under, keyed by the target as it
        /// appears in the binding plan: the projected Thing whose property maps onto it.
        /// Targets without an entry are created in the Objects folder.
        /// </summary>
        internal System.Collections.Concurrent.ConcurrentDictionary<string, NodeId> Parents { get; } = new(StringComparer.Ordinal);

        private NodeId ParentOf(WotTargetMappingDescriptor mapping) =>
            mapping.TargetNodeId != null && Parents.TryGetValue(mapping.TargetNodeId, out NodeId parent) && !parent.IsNull
                ? parent
                : ObjectIds.ObjectsFolder;

        public CreatingTargetVariableResolver(IWotTargetVariableResolver inner = null)
            : this(null, inner)
        {
        }

        internal CreatingTargetVariableResolver(WotMappingTypeModel types, IWotTargetVariableResolver inner = null)
        {
            _types = types;
            _inner = inner ?? new WotTargetVariableResolver();
        }

        /// <summary>
        /// Rewrites a 1.02 ObjectType mapping (<c>uav:mapToType</c> naming an ObjectType
        /// plus a <c>uav:mapByFieldPath</c> naming one of its child Variables) into a
        /// whole-value mapping onto that child. The registry treats any field path as a
        /// structure field and would otherwise try to encode the ObjectType as a
        /// structure. The rewritten mapping carries the child's NodeId with the
        /// ObjectType kept behind <see cref="ObjectTypeMarker"/> so <see cref="Resolve"/>
        /// can still create the instance.
        /// </summary>
        internal WotTargetMappingDescriptor RewriteObjectTypeMapping(WotTargetMappingDescriptor mapping)
        {
            if (mapping == null || _types == null
                || string.IsNullOrWhiteSpace(mapping.TargetNodeId)
                || string.IsNullOrWhiteSpace(mapping.TargetTypeNodeId)
                || string.IsNullOrWhiteSpace(mapping.FieldPath))
            {
                return mapping;
            }

            NodeId typeId;
            try
            {
                typeId = ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(mapping.TargetTypeNodeId), _types.NamespaceUris);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                return mapping;
            }

            if (typeId.IsNull || _types.ReadNodeClass(typeId) != NodeClass.ObjectType)
            {
                return mapping;
            }

            // The child is addressed through the field path; no DataType is asserted,
            // as the child's DataType comes from the ObjectType's declaration.
            return new WotTargetMappingDescriptor(
                mapping.TargetNodeId + ObjectTypeMarker + mapping.TargetTypeNodeId + ObjectTypeMarker + mapping.FieldPath,
                null,
                null);
        }

        /// <summary>Separates the parts of a rewritten ObjectType mapping target.</summary>
        internal const string ObjectTypeMarker = "\u001F";

        public BaseVariableState Resolve(INodeManagerBuilder builder, WotTargetMappingDescriptor mapping)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(mapping);

            if (mapping.TargetNodeId != null && mapping.TargetNodeId.Contains(ObjectTypeMarker, StringComparison.Ordinal))
            {
                string[] parts = mapping.TargetNodeId.Split(ObjectTypeMarker);
                WotTargetMappingDescriptor original = new(parts[0], parts[1], parts[2]);
                return ResolveObjectTypeChild(
                    builder,
                    ParentOf(mapping),
                    original,
                    Parse(builder, parts[0], "uav:mapToNodeId"),
                    Parse(builder, parts[1], "uav:mapToType"));
            }

            // Only an explicit target can be created; a type-only mapping selects an
            // existing Variable by DataType and is left to the package's rules.
            if (string.IsNullOrWhiteSpace(mapping.TargetNodeId))
            {
                return _inner.Resolve(builder, mapping);
            }

            NodeId targetId = Parse(builder, mapping.TargetNodeId, "uav:mapToNodeId");
            NodeId typeId = string.IsNullOrWhiteSpace(mapping.TargetTypeNodeId)
                ? NodeId.Null
                : Parse(builder, mapping.TargetTypeNodeId, "uav:mapToType");

            // 1.02: a 'uav:mapToType' naming an ObjectType means "instantiate the type
            // and map the property onto the child named by 'uav:mapByFieldPath'".
            if (!typeId.IsNull && _types?.ReadNodeClass(typeId) == NodeClass.ObjectType)
            {
                return ResolveObjectTypeChild(builder, ParentOf(mapping), mapping, targetId, typeId);
            }

            if (builder.TryGetNode(targetId, out NodeState existing))
            {
                return existing as BaseVariableState
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadNodeClassInvalid,
                        "'uav:mapToNodeId' '{0}' names a {1}, not a Variable.",
                        mapping.TargetNodeId,
                        existing.NodeClass);
            }

            NodeId dataType = typeId.IsNull ? DataTypeIds.BaseDataType : typeId;

            // Field-by-field mapping needs an encodeable type for the structure. Companion
            // models have none generated, so register a dynamic one from the DataType.
            if (!string.IsNullOrEmpty(mapping.FieldPath) && _types != null && !_types.EnsureStructureRegistered(dataType))
            {
                Log.Logger.Warning(
                    "DataType {DataType} of mapping target {NodeId} is not a structure the server describes; its fields cannot be mapped. Is the companion NodeSet loaded?",
                    dataType,
                    mapping.TargetNodeId);
            }

            BaseDataVariableState variable = NewVariable(targetId, new QualifiedName(BrowseNameOf(targetId), targetId.NamespaceIndex), dataType);
            builder.Add(variable, ParentOf(mapping));
            Log.Logger.Information(
                "Created WoT mapping target {NodeId} (DataType {DataType}) for an authored 'uav:mapToNodeId'.",
                mapping.TargetNodeId,
                dataType);

            return variable;
        }

        /// <summary>
        /// Creates (once) an instance of <paramref name="objectTypeId"/> with the
        /// authored NodeId below <paramref name="parentId"/>, and returns its child Variable
        /// matching <c>uav:mapByFieldPath</c> - creating that child when needed - as
        /// the 1.02 node manager did.
        /// </summary>
        private BaseVariableState ResolveObjectTypeChild(INodeManagerBuilder builder, NodeId parentId, WotTargetMappingDescriptor mapping, NodeId objectId, NodeId objectTypeId)
        {
            if (!_types.TryFindVariableDeclaration(objectTypeId, mapping.FieldPath, out QualifiedName childName, out NodeId childDataType, out bool isProperty))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "ObjectType '{0}' declares no Variable '{1}' for 'uav:mapByFieldPath'.",
                    mapping.TargetTypeNodeId,
                    mapping.FieldPath);
            }

            BaseObjectState instance;
            if (builder.TryGetNode(objectId, out NodeState existing))
            {
                instance = existing as BaseObjectState
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadNodeClassInvalid,
                        "'uav:mapToNodeId' '{0}' names a {1}, not an Object.",
                        mapping.TargetNodeId,
                        existing.NodeClass);
            }
            else
            {
                string name = BrowseNameOf(objectId);
                instance = new BaseObjectState(null)
                {
                    NodeId = objectId,
                    BrowseName = new QualifiedName(name, objectId.NamespaceIndex),
                    DisplayName = new LocalizedText(name),
                    SymbolicName = name,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    TypeDefinitionId = objectTypeId
                };

                builder.Add(instance, parentId);
                Log.Logger.Information(
                    "Created WoT mapping target object {NodeId} (ObjectType {ObjectType}) for an authored 'uav:mapToNodeId'.",
                    mapping.TargetNodeId,
                    objectTypeId);
            }

            // The child's NodeId is qualified by the parent, as the 1.02 node manager did.
            NodeId childId = new($"{BrowseNameOf(objectId)}.{childName.Name}", objectId.NamespaceIndex);
            if (builder.TryGetNode(childId, out NodeState existingChild) && existingChild is BaseVariableState found)
            {
                return found;
            }

            BaseVariableState child = isProperty
                ? new PropertyState(instance)
                : new BaseDataVariableState(instance);
            child.NodeId = childId;
            child.BrowseName = childName;
            child.DisplayName = new LocalizedText(childName.Name);
            child.SymbolicName = childName.Name;
            child.ReferenceTypeId = isProperty ? ReferenceTypeIds.HasProperty : ReferenceTypeIds.HasComponent;
            child.TypeDefinitionId = isProperty ? VariableTypeIds.PropertyType : VariableTypeIds.BaseDataVariableType;
            child.DataType = childDataType;
            child.ValueRank = ValueRanks.Scalar;
            child.AccessLevel = AccessLevels.CurrentReadOrWrite;
            child.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            child.StatusCode = StatusCodes.BadWaitingForInitialData;
            child.Timestamp = DateTime.UtcNow;

            builder.Add(child, objectId);
            return child;
        }

        private static BaseDataVariableState NewVariable(NodeId nodeId, QualifiedName browseName, NodeId dataType)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = browseName,
                DisplayName = new LocalizedText(browseName.Name),
                SymbolicName = browseName.Name,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                StatusCode = StatusCodes.BadWaitingForInitialData,
                Timestamp = DateTime.UtcNow
            };
        }

        private static NodeId Parse(INodeManagerBuilder builder, string text, string term)
        {
            try
            {
                return ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(text, builder.Context.NamespaceUris), builder.Context.NamespaceUris);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "'{0}' value '{1}' is not a valid portable NodeId: {2}",
                    term,
                    text,
                    ex.Message);
            }
        }

        private static string BrowseNameOf(NodeId nodeId)
        {
            string identifier = nodeId.IdType == IdType.String && nodeId.TryGetValue(out string text)
                ? text
                : null;

            return string.IsNullOrWhiteSpace(identifier) ? nodeId.ToString() : identifier;
        }
    }
}
