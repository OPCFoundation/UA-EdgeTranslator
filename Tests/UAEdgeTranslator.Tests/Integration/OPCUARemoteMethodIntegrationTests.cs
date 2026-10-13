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
        public async Task An_action_is_called_on_the_remote_method_and_object_named_by_its_form()
        {
            _asset.ThingDescription = await RemoteTdAsync("DiscoverAssets").ConfigureAwait(false);

            AssetActionResult result = await _asset.ExecuteActionAsync(LocalMethod("DiscoverAssets"), new List<object>()).ConfigureAwait(false);

            Assert.True(result.IsSuccess, result.Status);
            Assert.NotNull(result.Outputs);
        }

        [Fact]
        public async Task A_bad_status_from_the_remote_method_fails_the_action()
        {
            _asset.ThingDescription = await RemoteTdAsync("ConnectionTest").ConfigureAwait(false);

            // An empty endpoint reaches the remote method body, which rejects it.
            AssetActionResult result = await _asset.ExecuteActionAsync(LocalMethod("ConnectionTest"), new List<object> { string.Empty }).ConfigureAwait(false);

            Assert.False(result.IsSuccess);
            Assert.Contains("BadInvalidArgument", result.Status, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_action_without_its_object_node_id_fails_without_calling_the_server()
        {
            _asset.ThingDescription = await RemoteTdAsync("DiscoverAssets", includeObjectId: false).ConfigureAwait(false);

            AssetActionResult result = await _asset.ExecuteActionAsync(LocalMethod("DiscoverAssets"), new List<object>()).ConfigureAwait(false);

            Assert.False(result.IsSuccess);
            Assert.Contains("uav:objectId", result.Status, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_action_missing_from_the_td_fails_without_calling_the_server()
        {
            _asset.ThingDescription = await RemoteTdAsync("ConnectionTest").ConfigureAwait(false);

            AssetActionResult result = await _asset.ExecuteActionAsync(LocalMethod("DiscoverAssets"), new List<object>()).ConfigureAwait(false);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task A_browsed_method_becomes_an_action_the_v102_node_manager_can_call()
        {
            BrowsedNode browsed = _asset.BrowseObjectsFolder().Single(n => n.IsMethod && n.BrowsePath == "WoTAssetConnectionManagement.DiscoverAssets");
            Assert.NotNull(browsed.ObjectExpandedNodeId);

            // The action as generated into the TD, read back from TD JSON as when the asset
            // is onboarded from its file.
            UAForm generated = new() { Href = browsed.ExpandedNodeId, ObjectId = browsed.ObjectExpandedNodeId, Op = [Op.Invokeaction] };
            ThingDescription td = new() { Actions = new Dictionary<string, TDAction> { [browsed.BrowsePath] = new TDAction { Forms = [generated] } } };
            _asset.ThingDescription = Newtonsoft.Json.JsonConvert.DeserializeObject<ThingDescription>(Newtonsoft.Json.JsonConvert.SerializeObject(td));

            AssetActionResult result = await _asset.ExecuteActionAsync(LocalMethod(browsed.BrowsePath), new List<object>()).ConfigureAwait(false);

            Assert.True(result.IsSuccess, result.Status);
        }

        // A method shaped like the v1.02 node manager's: local ids that the remote server does not know.
        private static MethodState LocalMethod(string name)
        {
            BaseObjectState parent = new(null) { NodeId = new NodeId("local-asset", 42), BrowseName = new QualifiedName("asset") };
            return new MethodState(parent) { NodeId = new NodeId("local-method", 42), BrowseName = new QualifiedName(name) };
        }

        // A TD with one action naming a method of WoTAssetConnectionManagement, read from JSON.
        private async Task<ThingDescription> RemoteTdAsync(string name, bool includeObjectId = true)
        {
            NodeId management = await ChildAsync(_session, ObjectIds.ObjectsFolder, "WoTAssetConnectionManagement").ConfigureAwait(false);
            NodeId method = await ChildAsync(_session, management, name).ConfigureAwait(false);
            Newtonsoft.Json.Linq.JObject form = new() { ["href"] = NodeId.ToExpandedNodeId(method, _session.NamespaceUris).ToString() };
            if (includeObjectId)
            {
                form["uav:objectId"] = NodeId.ToExpandedNodeId(management, _session.NamespaceUris).ToString();
            }

            Newtonsoft.Json.Linq.JObject actions = new() { [name] = new Newtonsoft.Json.Linq.JObject { ["forms"] = new Newtonsoft.Json.Linq.JArray(form) } };
            return Newtonsoft.Json.JsonConvert.DeserializeObject<ThingDescription>(new Newtonsoft.Json.Linq.JObject { ["actions"] = actions }.ToString());
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
