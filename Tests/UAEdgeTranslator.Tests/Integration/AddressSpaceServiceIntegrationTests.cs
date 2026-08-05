namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.Diagnostics;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Integration coverage for the Address Space Explorer's backing service.
    /// <para>
    /// These tests boot the production <see cref="UAServer"/> in-process and
    /// browse it through <see cref="AddressSpaceService"/>. That matters
    /// because the service deliberately bypasses the client stack and drives
    /// the <c>MasterNodeManager</c> directly using a synthetic, session-less
    /// <c>OperationContext</c>. Only a real server can prove the SDK accepts
    /// that context for Browse and Read.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class AddressSpaceServiceIntegrationTests : IAsyncLifetime
    {
        private OpcUaServerFixture _fixture;
        private AddressSpaceService _service;

        public Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();
            _service = new AddressSpaceService();

            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            if (_fixture != null)
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
                _fixture = null;
            }
        }

        [Fact]
        public void Address_space_is_available_once_the_server_is_running()
        {
            Assert.True(_service.IsAvailable);
        }

        [Fact]
        public void Namespaces_include_the_core_and_translator_namespaces()
        {
            IReadOnlyList<NamespaceInfo> namespaces = _service.GetNamespaces();

            Assert.NotEmpty(namespaces);

            // Index 0 is always the OPC UA core namespace.
            Assert.Equal(0, namespaces[0].Index);
            Assert.Equal(Namespaces.OpcUa, namespaces[0].Uri);

            // The translator registers its own namespace, which is what makes
            // the dropdown's bold highlighting useful.
            Assert.Contains(namespaces, n => n.Uri == "http://opcfoundation.org/UA/EdgeTranslator/");

            // Indexes must be contiguous so they can be used directly as the
            // NamespaceIndex the tree compares against.
            Assert.Equal(Enumerable.Range(0, namespaces.Count).ToArray(), namespaces.Select(n => n.Index).ToArray());
        }

        [Fact]
        public async Task Root_returns_the_server_root_folder()
        {
            IReadOnlyList<AddressSpaceNode> roots = await _service.GetRootNodesAsync().ConfigureAwait(false);

            AddressSpaceNode root = Assert.Single(roots);

            Assert.Equal(ObjectIds.RootFolder.ToString(), root.Id);
            Assert.Equal("Root", root.Text);
            Assert.True(root.HasChildren);
        }

        [Fact]
        public async Task Root_exposes_the_objects_types_and_views_folders()
        {
            // Rooting at Root (rather than Objects) is what makes the type
            // hierarchy browsable at all.
            IReadOnlyList<AddressSpaceNode> children =
                await _service.BrowseChildrenAsync(ObjectIds.RootFolder.ToString()).ConfigureAwait(false);

            Assert.Contains(children, c => c.Text == "Objects");
            Assert.Contains(children, c => c.Text == "Types");
            Assert.Contains(children, c => c.Text == "Views");
        }

        [Fact]
        public async Task Types_folder_exposes_the_four_type_hierarchies()
        {
            IReadOnlyList<AddressSpaceNode> types =
                await _service.BrowseChildrenAsync(ObjectIds.TypesFolder.ToString()).ConfigureAwait(false);

            Assert.Contains(types, c => c.Text == "ObjectTypes");
            Assert.Contains(types, c => c.Text == "VariableTypes");
            Assert.Contains(types, c => c.Text == "DataTypes");
            Assert.Contains(types, c => c.Text == "ReferenceTypes");
        }

        [Fact]
        public async Task Type_nodes_report_their_type_node_class()
        {
            // The tree badges nodes by NodeClass, so browsing into the type
            // hierarchy must yield ObjectType/ReferenceType rather than Object.
            IReadOnlyList<AddressSpaceNode> objectTypes =
                await _service.BrowseChildrenAsync(ObjectTypeIds.BaseObjectType.ToString()).ConfigureAwait(false);

            Assert.NotEmpty(objectTypes);
            Assert.All(objectTypes, t => Assert.Equal(nameof(NodeClass.ObjectType), t.NodeClass));

            IReadOnlyList<AddressSpaceNode> referenceTypes =
                await _service.BrowseChildrenAsync(ReferenceTypeIds.References.ToString()).ConfigureAwait(false);

            Assert.NotEmpty(referenceTypes);
            Assert.All(referenceTypes, t => Assert.Equal(nameof(NodeClass.ReferenceType), t.NodeClass));
        }

        [Fact]
        public async Task Default_expansion_opens_root_and_objects()
        {
            IReadOnlyList<string> defaults = _service.GetDefaultExpandedNodeIds();

            Assert.Contains(ObjectIds.RootFolder.ToString(), defaults);
            Assert.Contains(ObjectIds.ObjectsFolder.ToString(), defaults);

            // Every default must be a real, browsable node.
            foreach (string id in defaults)
            {
                Assert.NotEmpty(await _service.BrowseChildrenAsync(id).ConfigureAwait(false));
            }
        }

        [Fact]
        public async Task Browsing_the_objects_folder_returns_the_server_node()
        {
            // Proves the synthetic OperationContext is accepted by BrowseAsync.
            IReadOnlyList<AddressSpaceNode> children =
                await _service.BrowseChildrenAsync(ObjectIds.ObjectsFolder.ToString()).ConfigureAwait(false);

            Assert.NotEmpty(children);
            Assert.Contains(children, c => c.Text == "Server");

            // Children are sorted alphabetically for a stable tree.
            Assert.Equal(children.Select(c => c.Text).OrderBy(t => t, System.StringComparer.OrdinalIgnoreCase).ToArray(),
                         children.Select(c => c.Text).ToArray());
        }

        [Fact]
        public async Task Browsing_an_unknown_node_returns_empty_rather_than_throwing()
        {
            IReadOnlyList<AddressSpaceNode> children =
                await _service.BrowseChildrenAsync("ns=99;s=does-not-exist").ConfigureAwait(false);

            Assert.Empty(children);
        }

        [Fact]
        public async Task Browsing_a_malformed_node_id_returns_empty_rather_than_throwing()
        {
            IReadOnlyList<AddressSpaceNode> children =
                await _service.BrowseChildrenAsync("not-a-node-id").ConfigureAwait(false);

            Assert.Empty(children);
        }

        [Fact]
        public async Task Children_never_contain_duplicate_node_ids()
        {
            // A node reachable through more than one hierarchical reference
            // (e.g. both Organizes and HasComponent) used to be returned once
            // per reference. The tree keys siblings by node id and Blazor
            // throws InvalidOperationException on duplicate keys, so every
            // browse must yield a distinct set.
            Queue<string> pending = new();
            HashSet<string> visited = new(System.StringComparer.Ordinal);

            IReadOnlyList<AddressSpaceNode> roots = await _service.GetRootNodesAsync().ConfigureAwait(false);

            foreach (AddressSpaceNode root in roots)
            {
                pending.Enqueue(root.Id);
                visited.Add(root.Id);
            }

            // Bounded walk: deep enough to cover the server diagnostics and
            // translator sub-trees without traversing the whole core model.
            int budget = 400;

            while (pending.Count > 0 && budget-- > 0)
            {
                string nodeId = pending.Dequeue();

                IReadOnlyList<AddressSpaceNode> children =
                    await _service.BrowseChildrenAsync(nodeId).ConfigureAwait(false);

                string[] ids = children.Select(c => c.Id).ToArray();

                Assert.True(
                    ids.Length == ids.Distinct(System.StringComparer.Ordinal).Count(),
                    $"Duplicate child node ids under '{nodeId}': " +
                    string.Join(", ", ids.GroupBy(i => i, System.StringComparer.Ordinal)
                                         .Where(g => g.Count() > 1)
                                         .Select(g => g.Key)));

                foreach (AddressSpaceNode child in children)
                {
                    if (visited.Add(child.Id))
                    {
                        pending.Enqueue(child.Id);
                    }
                }
            }
        }

        [Fact]
        public async Task Children_are_ordered_deterministically()
        {
            // Duplicate display names must not make the sort order vary between
            // refreshes, otherwise the tree reshuffles under the user.
            IReadOnlyList<AddressSpaceNode> first =
                await _service.BrowseChildrenAsync(ObjectIds.Server.ToString()).ConfigureAwait(false);

            IReadOnlyList<AddressSpaceNode> second =
                await _service.BrowseChildrenAsync(ObjectIds.Server.ToString()).ConfigureAwait(false);

            Assert.Equal(first.Select(c => c.Id).ToArray(), second.Select(c => c.Id).ToArray());
        }

        [Fact]
        public async Task Batch_read_returns_values_for_variables()
        {
            IReadOnlyDictionary<string, string> values = await _service.ReadValuesAsync(
            [
                VariableIds.Server_NamespaceArray.ToString(),
                VariableIds.Server_ServerStatus_State.ToString()
            ]).ConfigureAwait(false);

            Assert.Equal(2, values.Count);
            Assert.Contains(Namespaces.OpcUa, values[VariableIds.Server_NamespaceArray.ToString()], System.StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(values[VariableIds.Server_ServerStatus_State.ToString()]));
        }

        [Fact]
        public async Task Batch_read_omits_non_variable_nodes()
        {
            // The page passes in whatever is on screen, so Objects and folders
            // are expected inputs. They must be silently skipped rather than
            // decorated with an error string in the tree.
            IReadOnlyDictionary<string, string> values = await _service.ReadValuesAsync(
            [
                ObjectIds.Server.ToString(),
                ObjectIds.ObjectsFolder.ToString()
            ]).ConfigureAwait(false);

            Assert.Empty(values);
        }

        [Fact]
        public async Task Batch_read_skips_malformed_ids_and_still_reads_the_rest()
        {
            IReadOnlyDictionary<string, string> values = await _service.ReadValuesAsync(
            [
                "not-a-node-id",
                VariableIds.Server_ServerStatus_State.ToString()
            ]).ConfigureAwait(false);

            // The malformed id must not shift the result alignment.
            string state = Assert.Single(values).Value;
            Assert.False(string.IsNullOrEmpty(state));
            Assert.True(values.ContainsKey(VariableIds.Server_ServerStatus_State.ToString()));
        }

        [Fact]
        public async Task Batch_read_handles_an_empty_and_null_input()
        {
            Assert.Empty(await _service.ReadValuesAsync([]).ConfigureAwait(false));
            Assert.Empty(await _service.ReadValuesAsync(null).ConfigureAwait(false));
        }

        [Fact]
        public async Task Browsing_reflects_nodes_added_after_the_first_browse()
        {
            // Assets are onboarded at any time, which mutates the address space.
            // The Explorer purges its cached children on reload, so the service
            // must report newly added nodes on a subsequent browse rather than
            // any stale snapshot.
            UANodeManager manager = UANodeManager.Instance;
            ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/");

            // Attach to the WoTAssetManagement folder: it is owned by this node
            // manager (ObjectsFolder belongs to the SDK's core manager, so
            // Find() returns null for it) and is where real assets get linked.
            ushort wotConNs = (ushort)manager.Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/WoT-Con/");
            NodeId parentId = new(31, wotConNs);
            NodeState parent = manager.Find(parentId);
            Assert.NotNull(parent);

            IReadOnlyList<AddressSpaceNode> before =
                await _service.BrowseChildrenAsync(parentId.ToString()).ConfigureAwait(false);

            BaseObjectState added = new(null)
            {
                NodeId = new NodeId("LateAddedExplorerNode", ns),
                BrowseName = new QualifiedName("LateAddedExplorerNode", ns),
                DisplayName = "LateAddedExplorerNode",
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };

            // Both directions, exactly as UANodeManager links a new asset: the
            // forward reference on the parent is what a forward Browse follows.
            parent.AddReference(ReferenceTypeIds.Organizes, false, added.NodeId);
            added.AddReference(ReferenceTypeIds.Organizes, true, parentId);

            manager.AddPredefinedNodePublic(added);

            IReadOnlyList<AddressSpaceNode> after =
                await _service.BrowseChildrenAsync(parentId.ToString()).ConfigureAwait(false);

            Assert.DoesNotContain(before, n => n.Text == "LateAddedExplorerNode");
            Assert.Contains(after, n => n.Text == "LateAddedExplorerNode");
        }

        [Fact]
        public async Task Has_children_is_resolved_accurately_for_leaves_and_branches()
        {
            // The tree only renders an expander when HasChildren is true, so a
            // wrong answer here either hides a real branch or offers an
            // expander that opens onto nothing.
            IReadOnlyList<AddressSpaceNode> children =
                await _service.BrowseChildrenAsync(ObjectIds.Server.ToString()).ConfigureAwait(false);

            // ServerStatus is a Variable that DOES have children (sub-variables
            // like State, BuildInfo), proving the flag is not merely
            // "NodeClass != Variable".
            AddressSpaceNode status = children.First(c => c.Text == "ServerStatus");
            Assert.True(status.HasChildren);

            // Cross-check every child against an actual browse.
            foreach (AddressSpaceNode child in children)
            {
                IReadOnlyList<AddressSpaceNode> grandChildren =
                    await _service.BrowseChildrenAsync(child.Id).ConfigureAwait(false);

                Assert.True(
                    child.HasChildren == grandChildren.Count > 0,
                    $"'{child.Text}' reported HasChildren={child.HasChildren} but browsing returned {grandChildren.Count} children.");
            }
        }

        [Fact]
        public async Task Leaf_variables_report_no_children()
        {
            // A scalar leaf must not offer an expander.
            IReadOnlyList<AddressSpaceNode> children =
                await _service.BrowseChildrenAsync(VariableIds.Server_ServerStatus_BuildInfo.ToString()).ConfigureAwait(false);

            AddressSpaceNode productName = children.First(c => c.Text == "ProductName");

            Assert.False(productName.HasChildren);
            Assert.Empty(await _service.BrowseChildrenAsync(productName.Id).ConfigureAwait(false));
        }

        [Fact]
        public async Task Optional_attributes_are_blank_rather_than_showing_a_status_code()
        {
            // Description / WriteMask / UserWriteMask are OPTIONAL in OPC UA.
            // A node that does not implement them answers BadAttributeIdInvalid,
            // which must render as an empty cell, not as a raw status code
            // leaking into the UI.
            string[] optional = ["Description", "WriteMask", "UserWriteMask"];

            // Walk a decent slice of the address space to find nodes that omit
            // the optional attributes.
            List<string> toInspect = [ObjectIds.Server.ToString(), ObjectIds.ObjectsFolder.ToString()];

            foreach (AddressSpaceNode child in await _service.BrowseChildrenAsync(ObjectIds.Server.ToString()).ConfigureAwait(false))
            {
                toInspect.Add(child.Id);
            }

            int checkedNodes = 0;

            foreach (string id in toInspect)
            {
                NodeDetail detail = await _service.GetNodeDetailAsync(id).ConfigureAwait(false);

                foreach (NodeAttribute attribute in detail.Attributes.Where(a => optional.Contains(a.Name)))
                {
                    Assert.False(
                        attribute.Value.Contains("Bad", System.StringComparison.Ordinal),
                        $"Node '{id}' attribute '{attribute.Name}' leaked a status code: '{attribute.Value}'.");

                    checkedNodes++;
                }
            }

            Assert.True(checkedNodes > 0, "No optional attributes were inspected.");
        }

        [Fact]
        public async Task Genuine_bad_statuses_are_still_surfaced()
        {
            // Blanking BadAttributeIdInvalid must not blanket-hide real errors.
            // A read that fails for an operational reason (e.g. provisioning
            // mode rejecting tag access) still has to reach the operator, so
            // the Status row keeps reporting the raw code.
            NodeDetail detail = await _service.GetNodeDetailAsync(VariableIds.Server_ServerStatus_State.ToString()).ConfigureAwait(false);

            Assert.Contains(detail.ValueAttributes, a => a.Name == "Status");

            // Sanity check the distinction is by status code, not by attribute
            // name: a good read reports Good rather than being blanked.
            string status = detail.ValueAttributes.First(a => a.Name == "Status").Value;
            Assert.Contains("Good", status, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Explorer_legend_covers_every_node_class()
        {
            // The Explorer renders a legend for the node class badges. If the
            // OPC UA NodeClass enum ever gains a member that the legend does
            // not list, the tree would show an unexplained "?" badge.
            string[] legend =
            [
                nameof(NodeClass.Object),
                nameof(NodeClass.Variable),
                nameof(NodeClass.Method),
                nameof(NodeClass.ObjectType),
                nameof(NodeClass.VariableType),
                nameof(NodeClass.ReferenceType),
                nameof(NodeClass.DataType),
                nameof(NodeClass.View)
            ];

            string[] actual = System.Enum.GetNames<NodeClass>()
                .Where(n => n != nameof(NodeClass.Unspecified))
                .ToArray();

            Assert.Equal(actual.OrderBy(n => n, System.StringComparer.Ordinal).ToArray(),
                         legend.OrderBy(n => n, System.StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public async Task Node_detail_reports_core_attributes_for_an_object()
        {
            // Proves the synthetic OperationContext is accepted by ReadAsync.
            NodeDetail detail = await _service.GetNodeDetailAsync(ObjectIds.Server.ToString()).ConfigureAwait(false);

            Assert.Null(detail.Error);
            Assert.Equal("Server", detail.DisplayName);
            Assert.Equal(nameof(NodeClass.Object), detail.NodeClass);
            Assert.Equal(Namespaces.OpcUa, detail.NamespaceUri);

            Assert.Contains(detail.Attributes, a => a.Name == "BrowseName" && a.Value == "Server");
            Assert.Contains(detail.Attributes, a => a.Name == "NodeClass" && a.Value == nameof(NodeClass.Object));

            // An Object has no Value attribute, so that block stays hidden.
            Assert.Empty(detail.ValueAttributes);

            // The Server object is richly referenced; the panel should show them.
            Assert.NotEmpty(detail.References);
        }

        [Fact]
        public async Task Node_detail_reports_the_value_block_for_a_variable()
        {
            NodeDetail detail = await _service.GetNodeDetailAsync(VariableIds.Server_NamespaceArray.ToString()).ConfigureAwait(false);

            Assert.Null(detail.Error);
            Assert.Equal(nameof(NodeClass.Variable), detail.NodeClass);

            // The value block is only populated for nodes that actually have one.
            Assert.NotEmpty(detail.ValueAttributes);
            Assert.Contains(detail.ValueAttributes, a => a.Name == "Value");
            Assert.Contains(detail.ValueAttributes, a => a.Name == "DataType");

            // NamespaceArray is a readable string array, so the formatted value
            // should contain the core namespace.
            string value = detail.ValueAttributes.First(a => a.Name == "Value").Value;
            Assert.Contains(Namespaces.OpcUa, value, System.StringComparison.Ordinal);

            Assert.Contains(detail.ValueAttributes, a => a.Name == "AccessLevel" && a.Value.Contains("Read", System.StringComparison.Ordinal));
        }

        [Fact]
        public async Task Node_detail_reports_an_error_for_a_malformed_node_id()
        {
            NodeDetail detail = await _service.GetNodeDetailAsync("not-a-node-id").ConfigureAwait(false);

            Assert.NotNull(detail.Error);
        }

        [Fact]
        public async Task Translator_namespace_nodes_are_tagged_with_their_namespace_index()
        {
            // The bold highlighting in the tree keys off NamespaceIndex, so it
            // must line up with the namespace table the dropdown is built from.
            IReadOnlyList<NamespaceInfo> namespaces = _service.GetNamespaces();
            NamespaceInfo core = namespaces.First(n => n.Uri == Namespaces.OpcUa);

            IReadOnlyList<AddressSpaceNode> children =
                await _service.BrowseChildrenAsync(ObjectIds.ObjectsFolder.ToString()).ConfigureAwait(false);

            AddressSpaceNode server = children.First(c => c.Text == "Server");

            Assert.Equal(core.Index, server.NamespaceIndex);
        }
    }
}
