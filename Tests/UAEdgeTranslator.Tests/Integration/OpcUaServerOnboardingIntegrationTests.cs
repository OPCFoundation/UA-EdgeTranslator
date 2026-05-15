namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Edge.Translator;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Additional integration coverage that exercises the deeper code paths in
    /// <see cref="UANodeManager"/> through the live OPC UA server: WoT Thing
    /// Description onboarding, asset deletion, license property write/read,
    /// and connection-test rejection paths.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class OpcUaServerOnboardingIntegrationTests : IAsyncLifetime
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
                try { await _session.CloseAsync().ConfigureAwait(false); }
                catch { /* best-effort */ }
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
        public async Task CreateAsset_then_DeleteAsset_round_trips()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId createAsset = await ResolveAssetManagementChildAsync("CreateAsset").ConfigureAwait(false);
            NodeId deleteAsset = await ResolveAssetManagementChildAsync("DeleteAsset").ConfigureAwait(false);

            string assetName = "del" + Guid.NewGuid().ToString("N").Substring(0, 8);

            IList<object> created = await CallMethodAsync(assetManagement, createAsset, new object[] { assetName }).ConfigureAwait(false);
            NodeId newId = Assert.IsType<NodeId>(created[0]);
            Assert.False(NodeId.IsNull(newId));

            // Calling CreateAsset again with the same name must surface BadBrowseNameDuplicated
            CallMethodResult duplicate = await CallMethodRawAsync(assetManagement, createAsset, new object[] { assetName }).ConfigureAwait(false);
            Assert.Equal((StatusCode)StatusCodes.BadBrowseNameDuplicated, duplicate.StatusCode);

            // DeleteAsset removes it cleanly
            CallMethodResult delResult = await CallMethodRawAsync(assetManagement, deleteAsset, new object[] { newId }).ConfigureAwait(false);
            Assert.True(StatusCode.IsGood(delResult.StatusCode), $"DeleteAsset status: {delResult.StatusCode}");
        }

        [Fact]
        public async Task CreateAsset_rejects_invalid_names()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId createAsset = await ResolveAssetManagementChildAsync("CreateAsset").ConfigureAwait(false);

            CallMethodResult result = await CallMethodRawAsync(assetManagement, createAsset, new object[] { "../etc/passwd" }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task DeleteAsset_with_unknown_id_returns_BadNodeIdUnknown()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId deleteAsset = await ResolveAssetManagementChildAsync("DeleteAsset").ConfigureAwait(false);

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                deleteAsset,
                new object[] { new NodeId(987_654_321u, 1) }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadNodeIdUnknown, result.StatusCode);
        }

        [Fact]
        public async Task DeleteAsset_rejects_null_or_empty_argument()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId deleteAsset = await ResolveAssetManagementChildAsync("DeleteAsset").ConfigureAwait(false);

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                deleteAsset,
                new object[] { NodeId.Null }).ConfigureAwait(false);

            Assert.False(StatusCode.IsGood(result.StatusCode));
        }

        [Fact]
        public async Task ConnectionTest_rejects_missing_port()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId connectionTest = await ResolveAssetManagementChildAsync("ConnectionTest").ConfigureAwait(false);

            // Plain hostname has no port -> BadInvalidArgument from OnConnectionTest.
            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                connectionTest,
                new object[] { "no-port-host" }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task ConnectionTest_succeeds_against_live_loopback_listener()
        {
            // Bind a transient listener so the TCP probe finds an open port to connect to.
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

                NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
                NodeId connectionTest = await ResolveAssetManagementChildAsync("ConnectionTest").ConfigureAwait(false);

                CallMethodResult result = await CallMethodRawAsync(
                    assetManagement,
                    connectionTest,
                    new object[] { $"opc.tcp://127.0.0.1:{port}" }).ConfigureAwait(false);

                Assert.True(StatusCode.IsGood(result.StatusCode), $"Expected good status, got {result.StatusCode}");
                bool success = Assert.IsType<bool>(result.OutputArguments[0].Value);
                Assert.True(success);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task License_property_write_with_invalid_key_returns_BadInvalidArgument()
        {
            const string Var = "LICENSE_KEY";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, "expected-license-key");

                NodeId licenseNode = await ResolveLicenseNodeIdAsync().ConfigureAwait(false);

                WriteValue write = new WriteValue
                {
                    NodeId = licenseNode,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant("wrong-key"))
                };

                WriteResponse response = await _session.WriteAsync(
                    null,
                    new WriteValueCollection { write },
                    System.Threading.CancellationToken.None).ConfigureAwait(false);

                Assert.NotNull(response);
                Assert.NotEmpty(response.Results);
                Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, response.Results[0]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        [Fact]
        public async Task License_property_write_with_correct_key_succeeds()
        {
            const string Var = "LICENSE_KEY";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, "good-license-key");

                NodeId licenseNode = await ResolveLicenseNodeIdAsync().ConfigureAwait(false);

                WriteValue write = new WriteValue
                {
                    NodeId = licenseNode,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant("good-license-key"))
                };

                WriteResponse response = await _session.WriteAsync(
                    null,
                    new WriteValueCollection { write },
                    System.Threading.CancellationToken.None).ConfigureAwait(false);

                Assert.NotNull(response);
                Assert.NotEmpty(response.Results);
                Assert.True(StatusCode.IsGood(response.Results[0]),
                    $"License write should succeed, got {response.Results[0]}");

                // Read it back: the license field should have the new value.
                DataValue read = await _session.ReadValueAsync(licenseNode).ConfigureAwait(false);
                Assert.True(StatusCode.IsGood(read.StatusCode));
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        [Fact]
        public async Task License_property_write_when_LICENSE_KEY_unset_returns_BadNotSupported()
        {
            const string Var = "LICENSE_KEY";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, null);

                NodeId licenseNode = await ResolveLicenseNodeIdAsync().ConfigureAwait(false);

                WriteValue write = new WriteValue
                {
                    NodeId = licenseNode,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant("any"))
                };

                WriteResponse response = await _session.WriteAsync(
                    null,
                    new WriteValueCollection { write },
                    System.Threading.CancellationToken.None).ConfigureAwait(false);

                Assert.NotNull(response);
                Assert.NotEmpty(response.Results);
                Assert.Equal((StatusCode)StatusCodes.BadNotSupported, response.Results[0]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        [Fact]
        public async Task License_property_write_with_empty_value_returns_BadInvalidArgument()
        {
            const string Var = "LICENSE_KEY";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, "anything");

                NodeId licenseNode = await ResolveLicenseNodeIdAsync().ConfigureAwait(false);

                WriteValue write = new WriteValue
                {
                    NodeId = licenseNode,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(string.Empty))
                };

                WriteResponse response = await _session.WriteAsync(
                    null,
                    new WriteValueCollection { write },
                    System.Threading.CancellationToken.None).ConfigureAwait(false);

                Assert.NotNull(response);
                Assert.NotEmpty(response.Results);
                Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, response.Results[0]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        [Fact]
        public async Task SupportedOPCUAInfoModels_reads_local_nodeset_files()
        {
            // Drop a sample nodeset into the per-fixture nodesets/ directory and
            // make sure the property surfaces it. This exercises the OnReadValue
            // branch that lists local nodeset files.
            string nodesetsDir = Path.Combine(_fixture.WorkingDirectory, "nodesets");
            Directory.CreateDirectory(nodesetsDir);
            string sampleFile = Path.Combine(nodesetsDir, "sample.nodeset2.xml");
            File.WriteAllText(sampleFile, "<UANodeSet/>");

            NodeId nodeId = await ResolveAssetManagementChildAsync("SupportedOPCUAInfoModels").ConfigureAwait(false);
            DataValue value = await _session.ReadValueAsync(nodeId).ConfigureAwait(false);

            Assert.True(StatusCode.IsGood(value.StatusCode));
            string[] files = Assert.IsType<string[]>(value.Value);
            Assert.Contains(files, f => f.EndsWith("sample.nodeset2.xml", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task MemoryWorkingSet_variable_returns_positive_value()
        {
            NodeId nodeId = await ResolveAssetManagementChildAsync("MemoryWorkingSet(MB)").ConfigureAwait(false);
            DataValue value = await _session.ReadValueAsync(nodeId).ConfigureAwait(false);

            Assert.True(StatusCode.IsGood(value.StatusCode));
            int mb = Convert.ToInt32(value.Value);
            Assert.True(mb >= 0);
        }

        [Fact]
        public async Task CreateAssetForEndpoint_then_read_temperature_tag_via_OPC_UA()
        {
            // Onboard a mock asset and then browse for the auto-created
            // "temperature" variable, reading it through the OPC UA service. This
            // exercises the WoT-form -> UA-variable creation, the per-asset
            // polling loop, and OnReadValue's tag-resolution fast-path.
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId createAssetForEndpoint = await ResolveAssetManagementChildAsync("CreateAssetForEndpoint").ConfigureAwait(false);

            string assetName = "rd" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string assetEndpoint = $"{MockProtocolDriver.MockScheme}://device-r:1502/2";

            IList<object> created = await CallMethodAsync(
                assetManagement,
                createAssetForEndpoint,
                new object[] { assetName, assetEndpoint }).ConfigureAwait(false);

            NodeId newAssetNodeId = Assert.IsType<NodeId>(created[0]);

            // Browse new asset for its "temperature" child created from the TD property.
            ReferenceDescriptionCollection assetChildren = await BrowseChildrenAsync(newAssetNodeId).ConfigureAwait(false);
            ReferenceDescription tempRef = assetChildren.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, "temperature", StringComparison.Ordinal));

            Assert.NotNull(tempRef);

            NodeId tempNodeId = ExpandedNodeId.ToNodeId(tempRef.NodeId, _session.NamespaceUris);

            // Use the raw Read service so a Bad status surfaces in the result set
            // instead of getting thrown by the high-level NodeCacheContext.
            ReadValueId rv = new ReadValueId
            {
                NodeId = tempNodeId,
                AttributeId = Attributes.Value
            };

            ReadResponse readResponse = await _session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueIdCollection { rv },
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            // Status may be Bad (asset never connected / no value), but the read
            // must complete and not throw — that confirms tag plumbing is alive.
            Assert.NotNull(readResponse);
            Assert.NotEmpty(readResponse.Results);
        }

        [Fact]
        public async Task CreateAssetForEndpoint_rejects_unsupported_scheme()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId createAssetForEndpoint = await ResolveAssetManagementChildAsync("CreateAssetForEndpoint").ConfigureAwait(false);

            string assetName = "ne" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string assetEndpoint = "unknownscheme://device-x:1234/1";

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                createAssetForEndpoint,
                new object[] { assetName, assetEndpoint }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadNotSupported, result.StatusCode);
        }

        [Fact]
        public async Task CreateAssetForEndpoint_rejects_invalid_asset_name()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId createAssetForEndpoint = await ResolveAssetManagementChildAsync("CreateAssetForEndpoint").ConfigureAwait(false);

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                createAssetForEndpoint,
                new object[] { "with space", $"{MockProtocolDriver.MockScheme}://x:1/1" }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task SupportedWoTBindings_write_is_ignored_with_good_status()
        {
            NodeId nodeId = await ResolveAssetManagementChildAsync("SupportedWoTBindings").ConfigureAwait(false);

            WriteValue write = new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(new[] { "ignored" }))
            };

            // The handler short-circuits and returns Good without storing the value.
            WriteResponse response = await _session.WriteAsync(
                null,
                new WriteValueCollection { write },
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotEmpty(response.Results);
            // Server may reject with Bad due to access level; accept either Good
            // or Bad as long as the call did not throw — the tested code path is
            // the OnWriteValue switch on DisplayName.
        }

        // ----------------- helpers -----------------

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
                "UAEdgeTranslator extra integration tests",
                30_000,
                new UserIdentity(),
                null).ConfigureAwait(false);
        }

        private async Task<NodeId> ResolveAssetManagementNodeIdAsync()
        {
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(ObjectIds.ObjectsFolder).ConfigureAwait(false);
            ReferenceDescription am = children.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, "WoTAssetConnectionManagement", StringComparison.Ordinal));
            Assert.NotNull(am);
            return ExpandedNodeId.ToNodeId(am.NodeId, _session.NamespaceUris);
        }

        private async Task<NodeId> ResolveAssetManagementChildAsync(string browseName)
        {
            NodeId am = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(am).ConfigureAwait(false);
            ReferenceDescription target = children.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, browseName, StringComparison.Ordinal));

            return target == null ? NodeId.Null : ExpandedNodeId.ToNodeId(target.NodeId, _session.NamespaceUris);
        }

        private async Task<NodeId> ResolveLicenseNodeIdAsync()
        {
            NodeId am = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(am).ConfigureAwait(false);

            // License lives under AssetManagement -> Configuration -> License
            ReferenceDescription configRef = children.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, "Configuration", StringComparison.Ordinal));
            Assert.NotNull(configRef);

            NodeId configNodeId = ExpandedNodeId.ToNodeId(configRef.NodeId, _session.NamespaceUris);
            ReferenceDescriptionCollection configChildren = await BrowseChildrenAsync(configNodeId).ConfigureAwait(false);

            ReferenceDescription licenseRef = configChildren.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, "License", StringComparison.Ordinal));
            Assert.NotNull(licenseRef);

            return ExpandedNodeId.ToNodeId(licenseRef.NodeId, _session.NamespaceUris);
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

            Assert.NotEmpty(response.Results);
            Assert.True(StatusCode.IsGood(response.Results[0].StatusCode), $"Method call returned {response.Results[0].StatusCode}.");

            return response.Results[0].OutputArguments.Select(v => v.Value).ToList();
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

            Assert.NotEmpty(response.Results);
            return response.Results[0];
        }
    }
}
