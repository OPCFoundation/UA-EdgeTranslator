namespace Opc.Ua.Edge.Translator.WotCon
{
    using global::Opc.Ua.WotCon.Bindings;
    using global::Opc.Ua.WotCon.Server.Materialization;
    using Opc.Ua.Server;
    using Opc.Ua.Server.Fluent;
    using System;
    using System.Collections.Immutable;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Rewrites WoT Connectivity 1.02 ObjectType target mappings in the binding
    /// plans before the projection runtime wires them.
    /// </summary>
    /// <remarks>
    /// In 1.02, <c>uav:mapToType</c> naming an ObjectType plus <c>uav:mapByFieldPath</c>
    /// means "instantiate the type and map onto its child Variable". The registry reads
    /// every field path as a structure field and would wire the target as a structure,
    /// which fails for an Object. The mapping is rewritten into a whole-value mapping
    /// onto the child, which <see cref="CreatingTargetVariableResolver"/> creates.
    /// </remarks>
    internal sealed class ObjectTypeMappingRuntimeFactory : IWotProjectionBindingRuntimeFactory
    {
        private readonly IWotProjectionBindingRuntimeFactory _inner;

        private readonly CreatingTargetVariableResolver _resolver;

        private readonly ThingDescriptionCache _thingDescriptions;

        /// <param name="thingDescriptions">
        /// Told which registry resource each wired form belongs to, so the executor
        /// can give drivers that resource's Thing Description.
        /// </param>
        public ObjectTypeMappingRuntimeFactory(IWotProjectionBindingRuntimeFactory inner, CreatingTargetVariableResolver resolver, ThingDescriptionCache thingDescriptions = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _thingDescriptions = thingDescriptions;
        }

        public ValueTask<IAsyncDisposable> CreateAsync(INodeManagerBuilder builder, ArrayOf<WotBindingPlan> bindingPlans, CancellationToken cancellationToken = default)
        {
            MarkRegistryThings(builder);

            WotBindingPlan[] rewritten = new WotBindingPlan[bindingPlans.Count];
            for (int i = 0; i < bindingPlans.Count; i++)
            {
                PlaceMappedTargets(builder, bindingPlans[i]);
                rewritten[i] = Rewrite(bindingPlans[i]);
                RecordResource(rewritten[i]);
            }

            return _inner.CreateAsync(builder, new ArrayOf<WotBindingPlan>(rewritten), cancellationToken);
        }

        /// <summary>
        /// A property with a <c>uav:mapToNodeId</c> is served by its mapping target, so the
        /// target is created below the Thing that owns the property (not in the Objects
        /// folder), and the property's own projected Variable - which would never receive a
        /// value - is removed, leaving one node per mapped value under the asset.
        /// </summary>
        private void PlaceMappedTargets(INodeManagerBuilder builder, WotBindingPlan plan)
        {
            if (plan == null || plan.IsDeclarationContext || plan.CompiledForms.IsDefaultOrEmpty
                || builder.NodeManager is not AsyncCustomNodeManager manager)
            {
                return;
            }

            // The registry projects each property as a Variable named after it, below the
            // Thing's Object. One projection can hold several Things with properties of the
            // same name, so only this plan's Thing is indexed: the projection encodes the
            // Thing id in the Object's NodeId. Without a known Thing nothing is touched.
            string thingObjectPrefix = ThingObjectPrefix(plan.ResourceXid);
            if (thingObjectPrefix == null)
            {
                return;
            }

            var projectedProperties = new System.Collections.Generic.Dictionary<string, BaseVariableState>(StringComparer.Ordinal);
            foreach (NodeState node in PredefinedNodes(manager))
            {
                if (node is BaseVariableState variable && variable.Parent is BaseObjectState owner
                    && owner.NodeId.TryGetValue(out string ownerId)
                    && ownerId.StartsWith(thingObjectPrefix, StringComparison.Ordinal)
                    && !variable.BrowseName.IsNull)
                {
                    projectedProperties.TryAdd(variable.BrowseName.Name, variable);
                }
            }

            var removed = new System.Collections.Generic.HashSet<NodeId>();
            foreach (WotCompiledForm form in plan.CompiledForms)
            {
                WotTargetMappingDescriptor mapping = form?.TargetMapping;
                if (mapping == null || mapping.IsEmpty || string.IsNullOrWhiteSpace(mapping.TargetNodeId)
                    || form.AffordanceKind != WotAffordanceKind.Property
                    || !projectedProperties.TryGetValue(form.AffordanceName, out BaseVariableState projected))
                {
                    continue;
                }

                // A property mapped onto itself (the default) keeps its projected Variable.
                if (projected.NodeId == ToNodeId(builder, mapping.TargetNodeId))
                {
                    continue;
                }

                NodeId owner = projected.Parent?.NodeId ?? NodeId.Null;
                if (owner.IsNull)
                {
                    continue;
                }

                _resolver.Parents[mapping.TargetNodeId] = owner;
                // An ObjectType mapping is rewritten to a child target; record its parent too.
                string rewrittenTarget = _resolver.RewriteObjectTypeMapping(mapping).TargetNodeId;
                if (!string.Equals(rewrittenTarget, mapping.TargetNodeId, StringComparison.Ordinal))
                {
                    _resolver.Parents[rewrittenTarget] = owner;
                }

                if (removed.Add(projected.NodeId))
                {
                    NodeId projectedId = projected.NodeId;
                    AsyncBridge.RunSync(() => manager.DeleteNodeAsync(manager.SystemContext, projectedId).AsTask());
                }
            }
        }

        /// <summary>
        /// Returns the start of the string identifier the registry gives the Object it
        /// projects for the Thing <paramref name="resourceXid"/> describes:
        /// <c>/nsu=&lt;id&gt;;</c>, with the Thing id URI-escaped. The rest is derived from the
        /// title, so only the Thing id is matched. Returns <c>null</c> when the Thing is unknown.
        /// </summary>
        private string ThingObjectPrefix(string resourceXid)
        {
            Models.ThingDescription td = _thingDescriptions?.GetOrCreate(resourceXid, string.Empty);
            if (string.IsNullOrEmpty(td?.Id))
            {
                return null;
            }

            return "/nsu=" + Uri.EscapeDataString(td.Id) + ";";
        }

        /// <summary>
        /// Suffix that tells the Thing projected from the registry apart from the asset of
        /// the same name that the WoT asset connection management node manager creates.
        /// </summary>
        internal const string RegistryThingSuffix = " [xRegistry]";

        /// <summary>
        /// Marks every Thing projected into this node manager, mapped or not: the projection
        /// encodes the Thing id in the Object's NodeId as <c>/nsu=&lt;id&gt;;...</c>.
        /// </summary>
        private static void MarkRegistryThings(INodeManagerBuilder builder)
        {
            if (builder.NodeManager is not AsyncCustomNodeManager manager)
            {
                return;
            }

            foreach (NodeState node in PredefinedNodes(manager))
            {
                if (node is BaseObjectState thing && node.NodeId.TryGetValue(out string id)
                    && id.StartsWith("/nsu=", StringComparison.Ordinal))
                {
                    MarkAsRegistryThing(thing);
                }
            }
        }

        private static void MarkAsRegistryThing(BaseObjectState thing)
        {
            string text = thing.DisplayName.Text ?? thing.BrowseName.Name;
            if (string.IsNullOrEmpty(text) || text.EndsWith(RegistryThingSuffix, StringComparison.Ordinal))
            {
                return;
            }

            thing.DisplayName = new LocalizedText(thing.DisplayName.Locale, text + RegistryThingSuffix);
        }

        private static System.Collections.Generic.IEnumerable<NodeState> PredefinedNodes(AsyncCustomNodeManager manager)
        {
            if (typeof(AsyncCustomNodeManager).GetProperty("PredefinedNodes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(manager)
                is not System.Collections.IEnumerable entries)
            {
                yield break;
            }

            foreach (object entry in entries)
            {
                NodeState node = entry as NodeState ?? entry.GetType().GetProperty("Value")?.GetValue(entry) as NodeState;
                if (node != null)
                {
                    yield return node;
                }
            }
        }

        private static NodeId ToNodeId(INodeManagerBuilder builder, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return NodeId.Null;
            }

            try
            {
                return ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(text), builder.Context.NamespaceUris);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return NodeId.Null;
            }
        }

        /// <summary>
        /// Records the plan's registry resource against each of its final compiled forms;
        /// the runtime opens channels with exactly these instances.
        /// </summary>
        private void RecordResource(WotBindingPlan plan)
        {
            if (_thingDescriptions == null || plan == null || plan.CompiledForms.IsDefaultOrEmpty)
            {
                return;
            }

            foreach (WotCompiledForm form in plan.CompiledForms)
            {
                _thingDescriptions.SetResource(form, plan.ResourceXid);
            }
        }

        private WotBindingPlan Rewrite(WotBindingPlan plan)
        {
            if (plan == null || plan.CompiledForms.IsDefaultOrEmpty)
            {
                return plan;
            }

            bool changed = false;
            ImmutableArray<WotCompiledForm>.Builder forms = ImmutableArray.CreateBuilder<WotCompiledForm>(plan.CompiledForms.Length);
            foreach (WotCompiledForm form in plan.CompiledForms)
            {
                WotTargetMappingDescriptor mapping = _resolver.RewriteObjectTypeMapping(form.TargetMapping);
                if (!ReferenceEquals(mapping, form.TargetMapping))
                {
                    forms.Add(form.WithTargetMapping(mapping));
                    changed = true;
                }
                else
                {
                    forms.Add(form);
                }
            }

            if (!changed)
            {
                return plan;
            }

            return new WotBindingPlan(plan.ResourceXid, plan.Capabilities, forms.MoveToImmutable(), plan.UnsupportedForms, plan.Diagnostics)
                .WithProjectedAffordances(plan.ProjectedAffordances)
                .WithDeclarationContext(plan.IsDeclarationContext);
        }
    }
}
