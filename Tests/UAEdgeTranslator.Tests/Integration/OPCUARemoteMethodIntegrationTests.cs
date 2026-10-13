namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// The OPC UA driver is handed a MethodState from the translator's own address
    /// space; it must call the matching method on the remote server, not the local ids.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class OPCUARemoteMethodIntegrationTests : IAsyncLifetime
    {
        private OpcUaServerFixture _fixture;
        private OPCUAAsset _asset;
        private ISession _session;

        public async Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();
            _asset = new OPCUAAsset();

            // The driver selects a secured endpoint, while the fixture server only offers
            // SecurityPolicy#None, so hand the driver an unsecured session directly.
            _session = await OpenSessionAsync().ConfigureAwait(false);
            typeof(OPCUAAsset).GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(_asset, _session);
        }

        public async Task DisposeAsync()
        {
            if (_session != null)
            {
                try { await _session.CloseAsync().ConfigureAwait(false); } catch { }
                _session.Dispose();
                _session = null;
            }

            _asset = null;

            if (_fixture != null)
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
                _fixture = null;
            }
        }

        [Fact]
        public async Task An_action_is_called_on_the_remote_method_found_by_name()
        {
            AssetActionResult result = await _asset.ExecuteActionAsync(LocalMethod("DiscoverAssets"), new List<object>()).ConfigureAwait(false);

            Assert.True(result.IsSuccess, result.Status);
            Assert.NotNull(result.Outputs);
        }

        [Fact]
        public async Task An_action_is_called_on_the_remote_method_named_by_its_form()
        {
            string href = await RemoteMethodHrefAsync("ConnectionTest").ConfigureAwait(false);
            MethodState method = LocalMethod("SomeLocalName");
            method.Handle = new List<GenericForm> { new GenericForm { Href = href } };

            // An empty endpoint reaches the remote method body, which rejects it.
            AssetActionResult result = await _asset.ExecuteActionAsync(method, new List<object> { string.Empty }).ConfigureAwait(false);

            Assert.False(result.IsSuccess);
            Assert.Contains("BadInvalidArgument", result.Status, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_unknown_action_fails_without_calling_the_server()
        {
            AssetActionResult result = await _asset.ExecuteActionAsync(LocalMethod("NoSuchMethod"), new List<object>()).ConfigureAwait(false);

            Assert.False(result.IsSuccess);
            Assert.Contains("not found", result.Status, StringComparison.Ordinal);
        }

        // A method shaped like the v1.02 node manager's: local ids that the remote server does not know.
        private static MethodState LocalMethod(string name)
        {
            BaseObjectState parent = new(null) { NodeId = new NodeId("local-asset", 42), BrowseName = new QualifiedName("asset") };
            return new MethodState(parent) { NodeId = new NodeId("local-method", 42), BrowseName = new QualifiedName(name) };
        }

        private async Task<string> RemoteMethodHrefAsync(string name)
        {
            NodeId management = await ChildAsync(_session, ObjectIds.ObjectsFolder, "WoTAssetConnectionManagement").ConfigureAwait(false);
            NodeId method = await ChildAsync(_session, management, name).ConfigureAwait(false);
            return NodeId.ToExpandedNodeId(method, _session.NamespaceUris).ToString();
        }

        private async Task<ISession> OpenSessionAsync()
        {
            EndpointDescription endpoint = await CoreClientUtils.SelectEndpointAsync(
                _fixture.App.ApplicationConfiguration, _fixture.EndpointUrl, useSecurity: false, _fixture.Telemetry).ConfigureAwait(false);
            ConfiguredEndpoint configured = new(null, endpoint, EndpointConfiguration.Create(_fixture.App.ApplicationConfiguration));
            return await new DefaultSessionFactory(_fixture.Telemetry).CreateAsync(
                _fixture.App.ApplicationConfiguration, configured, true, false, "remote method tests", 30_000, new UserIdentity(), default).ConfigureAwait(false);
        }

        private static async Task<NodeId> ChildAsync(ISession session, NodeId parent, string name)
        {
            BrowseDescription browse = new()
            {
                NodeId = parent,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                ResultMask = (uint)BrowseResultMask.All
            };
            BrowseResponse response = await session.BrowseAsync(null, null, 0, new ArrayOf<BrowseDescription>(new[] { browse }), default).ConfigureAwait(false);
            ReferenceDescription child = response.Results[0].References.ToList().First(r => r.BrowseName.Name == name);
            return ExpandedNodeId.ToNodeId(child.NodeId, session.NamespaceUris);
        }
    }
}
