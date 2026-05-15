namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Edge.Translator;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// End-to-end integration tests that boot the production
    /// <see cref="UAServer"/> in-process via <see cref="OpcUaServerFixture"/>
    /// and drive it through a real OPC UA client session. The Cloud Library is
    /// "mocked" by leaving <c>UACLURL</c> unset (see fixture comments) so the
    /// production code path that talks to the cloud short-circuits to its
    /// local-only nodeset lookup.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class OpcUaServerIntegrationTests : IAsyncLifetime
    {
        private const string _wotConNamespace = "http://opcfoundation.org/UA/WoT-Con/";

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
                try
                {
                    await _session.CloseAsync().ConfigureAwait(false);
                }
                catch
                {
                    // best-effort
                }

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
        public async Task Server_advertises_WoTCon_namespace_to_clients()
        {
            // Ask the live server for its NamespaceArray via the standard
            // ServerObject node and make sure WoT-Con is published, which is
            // the contract every WoT/Edge Translator client relies on.
            DataValue value = await _session.ReadValueAsync(VariableIds.Server_NamespaceArray).ConfigureAwait(false);

            Assert.NotNull(value);
            Assert.True(StatusCode.IsGood(value.StatusCode), $"Read returned {value.StatusCode}.");

            string[] namespaces = Assert.IsType<string[]>(value.Value);
            Assert.Contains(_wotConNamespace, namespaces);
        }

        [Fact]
        public async Task SupportedWoTBindings_property_lists_registered_drivers()
        {
            // The fixture registers MockProtocolDriver, so the property exposed
            // by AssetManagement/SupportedWoTBindings must include its
            // WoTBindingUri without any reconfiguration on the server side.
            NodeId nodeId = await ResolveAssetManagementChildAsync("SupportedWoTBindings").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(nodeId), "SupportedWoTBindings node was not found in AssetManagement.");

            DataValue value = await _session.ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.True(StatusCode.IsGood(value.StatusCode), $"Read returned {value.StatusCode}.");

            string[] bindings = Assert.IsType<string[]>(value.Value);
            Assert.Contains(MockProtocolDriver.MockBindingUri, bindings);
        }

        [Fact]
        public async Task DiscoverAssets_returns_endpoints_advertised_by_registered_drivers()
        {
            // Walk to AssetManagement and call DiscoverAssets via the standard
            // OPC UA Method service — this exercises the full UANodeManager
            // method-binding pipeline (predefined nodes, output args, server
            // dispatch) instead of poking the C# method directly.
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId discoverAssets = await ResolveAssetManagementChildAsync("DiscoverAssets").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(discoverAssets), "DiscoverAssets method was not found.");

            IList<object> result = await CallMethodAsync(assetManagement, discoverAssets).ConfigureAwait(false);

            Assert.NotEmpty(result);
            string[] endpoints = Assert.IsType<string[]>(result[0]);
            Assert.Contains(endpoints, e => e.StartsWith(MockProtocolDriver.MockScheme + "://", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task CreateAssetForEndpoint_via_OPC_UA_method_call_onboards_a_new_asset()
        {
            // Drive the full onboarding flow over OPC UA: client invokes the
            // CreateAssetForEndpoint method, the server selects MockProtocolDriver
            // by URI scheme, generates a Thing Description, writes the .jsonld
            // file under settings/, and adds the new asset's variables to the
            // address space. Then we re-browse to make sure the new node is visible.
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId createAssetForEndpoint = await ResolveAssetManagementChildAsync("CreateAssetForEndpoint").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(createAssetForEndpoint), "CreateAssetForEndpoint method was not found.");

            string assetName = "integrationasset" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string assetEndpoint = $"{MockProtocolDriver.MockScheme}://device-x:1502/1";

            IList<object> result = await CallMethodAsync(
                assetManagement,
                createAssetForEndpoint,
                new object[] { assetName, assetEndpoint }).ConfigureAwait(false);

            Assert.NotEmpty(result);
            NodeId newAssetNodeId = Assert.IsType<NodeId>(result[0]);
            Assert.False(NodeId.IsNull(newAssetNodeId));

            // Re-browse AssetManagement to make sure the new asset shows up.
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(assetManagement).ConfigureAwait(false);
            Assert.Contains(children, r => string.Equals(r.DisplayName.Text, assetName, StringComparison.Ordinal));

            // The TD JSON-LD file must have landed under the per-fixture settings/ directory.
            string expectedTdFile = System.IO.Path.Combine(
                _fixture.WorkingDirectory,
                "settings",
                assetName + ".jsonld");
            Assert.True(System.IO.File.Exists(expectedTdFile), $"Expected TD file at {expectedTdFile}");
        }

        [Fact]
        public async Task ConnectionTest_returns_failure_for_unreachable_endpoint()
        {
            // ConnectionTest opens a TCP probe against the supplied endpoint.
            // Use a port we know is closed (port 1) on the loopback interface
            // and assert the method reports the failure cleanly through the
            // OPC UA Method service instead of throwing.
            //
            // The production handler returns StatusCodes.BadNotFound on a
            // failed probe; the OPC UA SDK then returns the bad status in
            // CallMethodResult.StatusCode and OUTs are not marshalled. The
            // contract we care about for the integration test is simply
            // "the method bound, executed, and reported failure deterministically"
            // — i.e. no exception escapes and the status code reflects the
            // probe outcome.
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId connectionTest = await ResolveAssetManagementChildAsync("ConnectionTest").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(connectionTest), "ConnectionTest method was not found.");

            string endpoint = "opc.tcp://127.0.0.1:1";
            CallMethodResult callResult = await CallMethodRawAsync(
                assetManagement,
                connectionTest,
                new object[] { endpoint }).ConfigureAwait(false);

            Assert.False(
                StatusCode.IsGood(callResult.StatusCode),
                $"ConnectionTest to a closed port should fail; got status {callResult.StatusCode}.");
            Assert.Equal((StatusCode)StatusCodes.BadNotFound, callResult.StatusCode);
        }

        [Fact]
        public async Task CloudLibrary_is_not_contacted_when_UACLURL_is_unset()
        {
            // The fixture deliberately leaves UACLURL unset. UANodeManager's
            // built-in WoT-Con nodeset must therefore have been added from the
            // local Opc.Ua.WotCon.NodeSet2.xml file copied next to the fixture's
            // working directory, not by reaching out to the cloud library.
            //
            // Asserting that the WoT-Con namespace is in the server's
            // NamespaceArray is sufficient evidence here because, with
            // UACLURL=null, no other code path could have added it.
            DataValue value = await _session.ReadValueAsync(VariableIds.Server_NamespaceArray).ConfigureAwait(false);
            string[] namespaces = Assert.IsType<string[]>(value.Value);
            Assert.Contains(_wotConNamespace, namespaces);

            Assert.Null(Environment.GetEnvironmentVariable("UACLURL"));
        }

        // ----------------- helpers -----------------

        private async Task<ISession> CreateClientSessionAsync()
        {
            // Use the SDK's standard endpoint discovery + session factory to
            // mirror the real client experience. SecurityNone keeps the test
            // friction low (no PKI dance) and matches the fixture's server
            // configuration.
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
                "UAEdgeTranslator integration tests",
                30_000,
                new UserIdentity(),
                null).ConfigureAwait(false);
        }

        private async Task<NodeId> ResolveAssetManagementNodeIdAsync()
        {
            // The WoT-Con object that hosts CreateAssetForEndpoint /
            // DiscoverAssets / ConnectionTest is named
            // "WoTAssetConnectionManagement" in the WoT-Con nodeset
            // (see Opc.Ua.WotCon.NodeSet2.xml NodeId="ns=1;i=31").
            // Browse the Objects folder once and find it by browse name so
            // we don't have to know its numeric NodeId in advance.
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
            Assert.True(StatusCode.IsGood(response.Results[0].StatusCode));

            return response.Results[0].References ?? new ReferenceDescriptionCollection();
        }

        private async Task<IList<object>> CallMethodAsync(NodeId objectId, NodeId methodId, object[] inputArgs = null)
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
            Assert.True(
                StatusCode.IsGood(response.Results[0].StatusCode),
                $"Method call returned {response.Results[0].StatusCode}.");

            return response.Results[0].OutputArguments
                .Select(v => v.Value)
                .ToList();
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
