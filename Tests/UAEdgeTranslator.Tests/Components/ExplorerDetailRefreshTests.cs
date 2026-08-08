namespace Opc.Ua.Edge.Translator.Tests.Components
{
    using Bunit;
    using Microsoft.Extensions.DependencyInjection;
    using Opc.Ua.Edge.Translator.Components.Pages;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.Tests.Integration;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Regression coverage for the Address Space Explorer's detail panel.
    /// <para>
    /// The tree refreshes its inline values every second, but the detail panel
    /// used to be populated only when a node was clicked. Sitting next to a
    /// visibly updating tree, that made the panel look live while actually
    /// showing whatever the value was at selection time.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class ExplorerDetailRefreshTests : BunitContext, IAsyncLifetime
    {
        private const BindingFlags _privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private OpcUaServerFixture _fixture;
        private NodeId _variableNodeId;

        public Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();

            Services.AddSingleton<DiagnosticsService>();
            Services.AddSingleton<AddressSpaceService>();

            SeedVariable(1);

            return Task.CompletedTask;
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            if (_fixture != null)
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
                _fixture = null;
            }
        }

        [Fact]
        public async Task Detail_panel_picks_up_a_changed_value_on_refresh()
        {
            IRenderedComponent<Explorer> page = Render<Explorer>();

            // Select the seeded variable, as clicking it in the tree would.
            await SelectAsync(page, _variableNodeId.ToString());

            NodeDetail first = GetDetail(page);

            Assert.NotNull(first);
            Assert.Equal("1", ValueOf(first));

            // Change the underlying value the way a southbound read would.
            SetVariableValue(42);

            // The periodic refresh must observe it.
            await RefreshDetailAsync(page);

            NodeDetail second = GetDetail(page);

            Assert.Equal("42", ValueOf(second));
        }

        [Fact]
        public async Task Detail_refresh_is_a_no_op_when_nothing_is_selected()
        {
            IRenderedComponent<Explorer> page = Render<Explorer>();

            // No selection: refreshing must not populate or throw.
            await RefreshDetailAsync(page);

            Assert.Null(GetDetail(page));
        }

        [Fact]
        public async Task Detail_refresh_keeps_the_panel_on_the_selected_node()
        {
            IRenderedComponent<Explorer> page = Render<Explorer>();

            await SelectAsync(page, _variableNodeId.ToString());
            await RefreshDetailAsync(page);

            NodeDetail detail = GetDetail(page);

            // A refresh must not silently swap the panel to a different node.
            Assert.Equal(_variableNodeId.ToString(), detail.NodeId);
        }

        private static string ValueOf(NodeDetail detail) =>
            detail.ValueAttributes.First(a => a.Name == "Value").Value;

        private static NodeDetail GetDetail(IRenderedComponent<Explorer> page)
        {
            FieldInfo field = typeof(Explorer).GetField("_detail", _privateInstance);

            Assert.NotNull(field);

            return (NodeDetail)field.GetValue(page.Instance);
        }

        private static async Task SelectAsync(IRenderedComponent<Explorer> page, string nodeId)
        {
            // Drive the same code path the tree's click handler uses.
            MethodInfo select = typeof(Explorer).GetMethod("SelectNodeAsync", _privateInstance);

            Assert.NotNull(select);

            AddressSpaceNode node = new() { Id = nodeId, NodeClass = "Variable", Text = "seeded" };

            await page.InvokeAsync(async () => await (Task)select.Invoke(page.Instance, [node]));
        }

        private static async Task RefreshDetailAsync(IRenderedComponent<Explorer> page)
        {
            MethodInfo refresh = typeof(Explorer).GetMethod("RefreshDetailAsync", _privateInstance);

            Assert.NotNull(refresh);

            await page.InvokeAsync(async () => await (Task)refresh.Invoke(page.Instance, []));
        }

        private void SeedVariable(int value)
        {
            UANodeManager manager = UANodeManager.Instance;
            ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/");

            _variableNodeId = new NodeId("ExplorerDetailRefreshValue", ns);

            manager.AddPredefinedNodePublic(new BaseDataVariableState(null)
            {
                NodeId = _variableNodeId,
                BrowseName = new QualifiedName("ExplorerDetailRefreshValue", ns),
                DisplayName = "ExplorerDetailRefreshValue",
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = value
            });
        }

        private void SetVariableValue(int value)
        {
            BaseDataVariableState variable = (BaseDataVariableState)UANodeManager.Instance.Find(_variableNodeId);

            Assert.NotNull(variable);

            variable.Value = value;
            variable.Timestamp = System.DateTime.UtcNow;
            variable.ClearChangeMasks(UANodeManager.Instance.SystemContext, false);
        }
    }
}
