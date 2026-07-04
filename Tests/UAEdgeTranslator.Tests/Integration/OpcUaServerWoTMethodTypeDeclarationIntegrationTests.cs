namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// OPC UA Part 4 v1.05.07 §5.12.2.2 (Table 59) / v1.04 §5.11.2.2 (Table 65)
    /// conformance: a client may invoke a Method by passing the NodeId of the
    /// Method that is the InstanceDeclaration on the ObjectType (or a supertype)
    /// the addressed Object instantiates ("form 2"), not just the per-instance
    /// Method NodeId ("form 1").
    ///
    /// These tests call every WoT-Con method via its type-declaration NodeId
    /// (taken from Opc.Ua.WotCon.NodeSet2.xml) paired with the addressed
    /// instance ObjectId and assert the call reaches the bound handler on the
    /// first attempt — i.e. it does NOT return <c>Bad_MethodInvalid</c>. A
    /// genuinely invalid methodId must still return <c>Bad_MethodInvalid</c>.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class OpcUaServerWoTMethodTypeDeclarationIntegrationTests : IAsyncLifetime
    {
        private const string _wotConNamespace = "http://opcfoundation.org/UA/WoT-Con/";

        // Type-declaration (InstanceDeclaration) NodeIds from the WoT-Con NodeSet.
        private const uint _typeCreateAsset = 26;
        private const uint _typeDeleteAsset = 29;
        private const uint _typeDiscoverAssets = 41;
        private const uint _typeCreateAssetForEndpoint = 49;
        private const uint _typeConnectionTest = 75;
        private const uint _typeCloseAndUpdate = 111;

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
        public async Task CreateAsset_via_type_declaration_methodId_creates_asset()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId methodId = TypeDeclarationNodeId(_typeCreateAsset);

            string assetName = "td-create-" + Guid.NewGuid().ToString("N").Substring(0, 6);

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                new object[] { assetName }).ConfigureAwait(false);

            Assert.NotEqual((StatusCode)StatusCodes.BadMethodInvalid, result.StatusCode);
            Assert.True(StatusCode.IsGood(result.StatusCode), $"CreateAsset (form 2) returned {result.StatusCode}");

            NodeId newId = Assert.IsType<NodeId>(result.OutputArguments[0].Value);
            Assert.False(NodeId.IsNull(newId));
        }

        [Fact]
        public async Task DeleteAsset_via_type_declaration_methodId_deletes_asset()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);

            // Create through the instance method so the delete has a real target.
            NodeId createInstance = await ResolveAssetManagementChildAsync("CreateAsset").ConfigureAwait(false);
            string assetName = "td-del-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            CallMethodResult created = await CallMethodRawAsync(
                assetManagement, createInstance, new object[] { assetName }).ConfigureAwait(false);
            Assert.True(StatusCode.IsGood(created.StatusCode), $"CreateAsset setup returned {created.StatusCode}");
            NodeId assetId = (NodeId)created.OutputArguments[0].Value;

            NodeId methodId = TypeDeclarationNodeId(_typeDeleteAsset);
            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                new object[] { assetId }).ConfigureAwait(false);

            Assert.NotEqual((StatusCode)StatusCodes.BadMethodInvalid, result.StatusCode);
            Assert.True(StatusCode.IsGood(result.StatusCode), $"DeleteAsset (form 2) returned {result.StatusCode}");
        }

        [Fact]
        public async Task DiscoverAssets_via_type_declaration_methodId_resolves()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId methodId = TypeDeclarationNodeId(_typeDiscoverAssets);

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                Array.Empty<object>()).ConfigureAwait(false);

            Assert.NotEqual((StatusCode)StatusCodes.BadMethodInvalid, result.StatusCode);
            Assert.True(StatusCode.IsGood(result.StatusCode), $"DiscoverAssets (form 2) returned {result.StatusCode}");
        }

        [Fact]
        public async Task CreateAssetForEndpoint_via_type_declaration_methodId_resolves()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId methodId = TypeDeclarationNodeId(_typeCreateAssetForEndpoint);

            // Empty inputs make the handler return BadInvalidArgument; reaching
            // that branch (instead of BadMethodInvalid) proves form-2 resolution.
            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                new object[] { string.Empty, string.Empty }).ConfigureAwait(false);

            Assert.NotEqual((StatusCode)StatusCodes.BadMethodInvalid, result.StatusCode);
            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task ConnectionTest_via_type_declaration_methodId_resolves()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId methodId = TypeDeclarationNodeId(_typeConnectionTest);

            // Empty endpoint makes the handler return BadInvalidArgument; reaching
            // that branch (instead of BadMethodInvalid) proves form-2 resolution.
            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                methodId,
                new object[] { string.Empty }).ConfigureAwait(false);

            Assert.NotEqual((StatusCode)StatusCodes.BadMethodInvalid, result.StatusCode);
            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task CloseAndUpdate_via_type_declaration_methodId_resolves()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);

            NodeId createInstance = await ResolveAssetManagementChildAsync("CreateAsset").ConfigureAwait(false);
            string assetName = "td-file-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            CallMethodResult created = await CallMethodRawAsync(
                assetManagement, createInstance, new object[] { assetName }).ConfigureAwait(false);
            Assert.True(StatusCode.IsGood(created.StatusCode), $"CreateAsset setup returned {created.StatusCode}");
            NodeId assetId = (NodeId)created.OutputArguments[0].Value;

            NodeId wotFile = await ResolveChildNodeIdAsync(assetId, "WoTFile").ConfigureAwait(false);
            Assert.False(NodeId.IsNull(wotFile), "WoTFile component not found under the created asset.");

            // Call CloseAndUpdate via the WoTAssetFileType declaration (i=111)
            // with an unknown file handle. The handler returns BadInvalidArgument
            // for the unknown handle; reaching it (instead of BadMethodInvalid)
            // proves form-2 resolution against the per-asset WoTFile instance.
            NodeId methodId = TypeDeclarationNodeId(_typeCloseAndUpdate);
            CallMethodResult result = await CallMethodRawAsync(
                wotFile,
                methodId,
                new object[] { (uint)999_999 }).ConfigureAwait(false);

            Assert.NotEqual((StatusCode)StatusCodes.BadMethodInvalid, result.StatusCode);
            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, result.StatusCode);
        }

        [Fact]
        public async Task Unknown_methodId_still_returns_BadMethodInvalid()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            NodeId bogus = new NodeId(987_654_321u, ResolveWoTConNamespaceIndex());

            CallMethodResult result = await CallMethodRawAsync(
                assetManagement,
                bogus,
                Array.Empty<object>()).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadMethodInvalid, result.StatusCode);
        }

        // --------- helpers ---------

        private ushort ResolveWoTConNamespaceIndex()
        {
            int index = _session.NamespaceUris.GetIndex(_wotConNamespace);
            Assert.True(index > 0, "WoT-Con namespace was not published by the server.");
            return (ushort)index;
        }

        private NodeId TypeDeclarationNodeId(uint identifier)
        {
            return new NodeId(identifier, ResolveWoTConNamespaceIndex());
        }

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
                "UAEdgeTranslator wot-method type-declaration tests",
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
            return await ResolveChildNodeIdAsync(assetManagement, browseName).ConfigureAwait(false);
        }

        private async Task<NodeId> ResolveChildNodeIdAsync(NodeId parent, string browseName)
        {
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(parent).ConfigureAwait(false);
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
