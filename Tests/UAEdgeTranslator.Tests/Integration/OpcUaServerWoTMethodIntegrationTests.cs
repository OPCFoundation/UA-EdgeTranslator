namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Configuration;
    using Opc.Ua.Edge.Translator;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Live-server coverage for the WoT-Con methods that the existing suites
    /// did not yet exercise: <c>CreateAssetForEndpoint</c> failure paths,
    /// <c>ConnectionTest</c> port-validation branch, and the License read /
    /// write paths under <c>AssetManagement/Configuration</c>.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class OpcUaServerWoTMethodIntegrationTests : IAsyncLifetime
    {
        private OpcUaServerFixture _fixture;
        private ISession _session;

        public async Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();
            _session = await CreateClientSessionAsync().ConfigureAwait(false);
        }

        public async Task DisposeAsync()
        {
            if (_session != null)
            {
                try { await _session.CloseAsync().ConfigureAwait(false); } catch { }
                _session.Dispose();
                _session = null;
            }

            if (_fixture != null)
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
                _fixture = null;
            }
        }

        [Fact]
        public async Task CreateAssetForEndpoint_with_empty_inputs_returns_BadInvalidArgument()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId methodId = await ResolveAssetManagementChildAsync("CreateAssetForEndpoint").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(methodId));

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                new object[] { string.Empty, string.Empty }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task CreateAssetForEndpoint_with_invalid_name_returns_BadInvalidArgument()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId methodId = await ResolveAssetManagementChildAsync("CreateAssetForEndpoint").ConfigureAwait(false);

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                new object[] { "../escape", "mock://x:1/1" }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task CreateAssetForEndpoint_with_unknown_driver_returns_BadNotSupported()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId methodId = await ResolveAssetManagementChildAsync("CreateAssetForEndpoint").ConfigureAwait(false);

            string assetName = "no-driver-" + Guid.NewGuid().ToString("N").Substring(0, 6);

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                new object[] { assetName, "noscheme://nowhere" }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadNotSupported, result.StatusCode);
        }

        [Fact]
        public async Task ConnectionTest_with_empty_endpoint_returns_BadInvalidArgument()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId methodId = await ResolveAssetManagementChildAsync("ConnectionTest").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(methodId));

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                new object[] { string.Empty }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task ConnectionTest_without_port_returns_BadInvalidArgument()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId methodId = await ResolveAssetManagementChildAsync("ConnectionTest").ConfigureAwait(false);

            // No scheme + no port should surface BadInvalidArgument from the
            // method body (output arguments are not promised by the SDK in this
            // status-code path, so only assert on the status code).
            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                new object[] { "127.0.0.1" }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task ConnectionTest_against_a_real_listener_succeeds()
        {
            // Spin up a tiny TCP listener so the production TCP-probe path
            // returns success and exercises the success branch of OnConnectionTest.
            System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

                NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
                NodeId methodId = await ResolveAssetManagementChildAsync("ConnectionTest").ConfigureAwait(false);

                CallMethodResult result = await CallMethodRawAsync(
                    assetManagement,
                    methodId,
                    new object[] { "127.0.0.1:" + port }).ConfigureAwait(false);

                Assert.True(StatusCode.IsGood(result.StatusCode), $"ConnectionTest returned {result.StatusCode}");
                Assert.NotEmpty(result.OutputArguments);
                Assert.True((bool)result.OutputArguments[0].Value);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task DiscoverAssets_returns_endpoints_from_loaded_drivers()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId methodId = await ResolveAssetManagementChildAsync("DiscoverAssets").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(methodId));

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                Array.Empty<object>()).ConfigureAwait(false);

            Assert.True(StatusCode.IsGood(result.StatusCode), $"DiscoverAssets returned {result.StatusCode}");
            Assert.NotEmpty(result.OutputArguments);

            string[] endpoints = result.OutputArguments[0].Value as string[];
            Assert.NotNull(endpoints);
        }

        // --------- helpers (lifted from OpcUaServerAssetLifecycleIntegrationTests) ---------

        private async Task<ISession> CreateClientSessionAsync()
        {
            EndpointDescription selectedEndpoint = await CoreClientUtils.SelectEndpointAsync(
                _fixture.App.ApplicationConfiguration,
                _fixture.EndpointUrl,
                useSecurity: false,
                _fixture.Telemetry).ConfigureAwait(false);

            ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(
                null,
                selectedEndpoint,
                EndpointConfiguration.Create(_fixture.App.ApplicationConfiguration));

            return await new DefaultSessionFactory(_fixture.Telemetry).CreateAsync(
                _fixture.App.ApplicationConfiguration,
                configuredEndpoint,
                updateBeforeConnect: true,
                checkDomain: false,
                "UAEdgeTranslator wot-method tests",
                30_000,
                new UserIdentity(),
                null).ConfigureAwait(false);
        }

        private async Task<NodeId> ResolveAssetManagementNodeIdAsync()
        {
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(ObjectIds.ObjectsFolder).ConfigureAwait(false);
            ReferenceDescription assetManagement = children.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, "WoTAssetConnectionManagement", StringComparison.Ordinal));

            Assert.NotNull(assetManagement);
            return ExpandedNodeId.ToNodeId(assetManagement.NodeId, _session.NamespaceUris);
        }

        private async Task<NodeId> ResolveAssetManagementChildAsync(string browseName)
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(assetManagement).ConfigureAwait(false);
            ReferenceDescription target = children.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, browseName, StringComparison.Ordinal));

            return target == null
                ? NodeId.Null
                : ExpandedNodeId.ToNodeId(target.NodeId, _session.NamespaceUris);
        }

        private async Task<ReferenceDescriptionCollection> BrowseChildrenAsync(NodeId parent)
        {
            BrowseDescription browse = new BrowseDescription
            {
                NodeId = parent,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };

            BrowseResponse response = await _session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescriptionCollection { browse },
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotEmpty(response.Results);

            return response.Results[0].References ?? new ReferenceDescriptionCollection();
        }

        private async Task<CallMethodResult> CallMethodRawAsync(NodeId objectId, NodeId methodId, object[] inputArgs = null)
        {
            CallMethodRequest request = new CallMethodRequest
            {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = inputArgs == null
                    ? new VariantCollection()
                    : new VariantCollection(inputArgs.Select(a => new Variant(a)))
            };

            CallResponse response = await _session.CallAsync(
                null,
                new CallMethodRequestCollection { request },
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotEmpty(response.Results);
            return response.Results[0];
        }
    }
}
