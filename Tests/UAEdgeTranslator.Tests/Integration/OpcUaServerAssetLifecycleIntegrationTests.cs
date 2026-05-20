namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Configuration;
    using Opc.Ua.Edge.Translator;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// End-to-end OPC UA coverage for the asset lifecycle methods:
    /// <c>CreateAsset</c>, <c>DeleteAsset</c>, plus their failure paths
    /// (invalid name, duplicate name, unknown id). These exercise the full
    /// <see cref="UANodeManager"/> bookkeeping branches around the
    /// <c>_assets</c>, <c>_tags</c>, <c>_uaVariables</c> and <c>_tagIndex</c>
    /// dictionaries, which the existing integration suite did not touch.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class OpcUaServerAssetLifecycleIntegrationTests : IAsyncLifetime
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
        public async Task CreateAsset_with_invalid_name_returns_BadInvalidArgument()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId createAsset = await ResolveAssetManagementChildAsync("CreateAsset").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(createAsset));

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                createAsset,
                new object[] { "../escape" }).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task CreateAsset_then_DeleteAsset_round_trips_a_brand_new_asset()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId createAsset = await ResolveAssetManagementChildAsync("CreateAsset").ConfigureAwait(false);
            NodeId deleteAsset = await ResolveAssetManagementChildAsync("DeleteAsset").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(createAsset));
            Assert.False(NodeId.IsNull(deleteAsset));

            string assetName = "lifecycle" + Guid.NewGuid().ToString("N").Substring(0, 6);

            // Create
            IList<object> created = await CallMethodAsync(assetManagement, createAsset, new object[] { assetName })
                .ConfigureAwait(false);
            Assert.NotEmpty(created);
            NodeId newAssetId = Assert.IsType<NodeId>(created[0]);
            Assert.False(NodeId.IsNull(newAssetId));

            // Asset must show up in the AssetManagement object's children
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(assetManagement).ConfigureAwait(false);
            Assert.Contains(children, r => string.Equals(r.DisplayName.Text, assetName, StringComparison.Ordinal));

            // Creating again with the same name must hit the duplicate branch
            CallMethodResult duplicate = await CallMethodRawAsync(
                assetManagement,
                createAsset,
                new object[] { assetName }).ConfigureAwait(false);
            Assert.Equal((StatusCode)StatusCodes.BadBrowseNameDuplicated, duplicate.StatusCode);

            // Delete with empty input must fail invalid-argument
            CallMethodResult empty = await CallMethodRawAsync(
                assetManagement,
                deleteAsset,
                new object[] { string.Empty }).ConfigureAwait(false);
            Assert.False(StatusCode.IsGood(empty.StatusCode));

            // Delete with unknown id must fail BadNodeIdUnknown
            CallMethodResult unknown = await CallMethodRawAsync(
                assetManagement,
                deleteAsset,
                new object[] { new NodeId("does-not-exist", 99) }).ConfigureAwait(false);
            Assert.Equal((StatusCode)StatusCodes.BadNodeIdUnknown, unknown.StatusCode);

            // Real delete must succeed
            CallMethodResult deleted = await CallMethodRawAsync(
                assetManagement,
                deleteAsset,
                new object[] { newAssetId }).ConfigureAwait(false);
            Assert.True(StatusCode.IsGood(deleted.StatusCode), $"DeleteAsset returned {deleted.StatusCode}");
        }

        [Fact]
        public async Task License_property_is_present_under_AssetManagement_Configuration()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);

            ReferenceDescriptionCollection topChildren = await BrowseChildrenAsync(assetManagement).ConfigureAwait(false);
            ReferenceDescription configRef = topChildren.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, "Configuration", StringComparison.Ordinal));
            Assert.NotNull(configRef);

            NodeId configurationId = ExpandedNodeId.ToNodeId(configRef.NodeId, _session.NamespaceUris);
            ReferenceDescriptionCollection configChildren = await BrowseChildrenAsync(configurationId).ConfigureAwait(false);

            Assert.Contains(configChildren, r => string.Equals(r.BrowseName.Name, "License", StringComparison.Ordinal));
        }

        [Fact]
        public async Task MemoryWorkingSet_variable_returns_a_positive_value()
        {
            NodeId memNodeId = await ResolveAssetManagementChildAsync("MemoryWorkingSet(MB)").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(memNodeId));

            DataValue value = await _session.ReadValueAsync(memNodeId).ConfigureAwait(false);
            Assert.True(StatusCode.IsGood(value.StatusCode), $"Read returned {value.StatusCode}.");

            long bytes = Convert.ToInt64(value.Value);
            Assert.True(bytes > 0, "Memory working set must be positive.");
        }

        // ----------------- helpers (lifted from OpcUaServerIntegrationTests) -----------------

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
                "UAEdgeTranslator asset-lifecycle tests",
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
