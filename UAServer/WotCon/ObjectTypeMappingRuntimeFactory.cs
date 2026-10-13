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

        public ObjectTypeMappingRuntimeFactory(IWotProjectionBindingRuntimeFactory inner, CreatingTargetVariableResolver resolver)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public ValueTask<IAsyncDisposable> CreateAsync(INodeManagerBuilder builder, ArrayOf<WotBindingPlan> bindingPlans, CancellationToken cancellationToken = default)
        {
            WotBindingPlan[] rewritten = new WotBindingPlan[bindingPlans.Count];
            for (int i = 0; i < bindingPlans.Count; i++)
            {
                PlaceMappedTargets(builder, bindingPlans[i]);
                rewritten[i] = Rewrite(bindingPlans[i]);
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
            // Thing's Object. Index those Variables by property name.
            var projectedProperties = new System.Collections.Generic.Dictionary<string, BaseVariableState>(StringComparer.Ordinal);
            foreach (NodeState node in PredefinedNodes(manager))
            {
                if (node is BaseVariableState variable && variable.Parent is BaseObjectState && !variable.BrowseName.IsNull)
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
