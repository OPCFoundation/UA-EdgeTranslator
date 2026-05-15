namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Configuration;
    using Opc.Ua.Edge.Translator;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Live coverage of the License write path inside
    /// <c>UANodeManager.OnWriteValue</c>: empty value, missing
    /// <c>LICENSE_KEY</c> environment variable, mismatched key, and the
    /// successful end-to-end write that flips the cached property's value.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class OpcUaServerLicenseWriteIntegrationTests : IAsyncLifetime
    {
        private const string _envLicenseKey = "LICENSE_KEY";

        private OpcUaServerFixture _fixture;
        private ISession _session;
        private string _previousLicenseKey;

        public async Task InitializeAsync()
        {
            _previousLicenseKey = Environment.GetEnvironmentVariable(_envLicenseKey);
            _fixture = new OpcUaServerFixture();
            _session = await CreateClientSessionAsync().ConfigureAwait(false);
        }

        public async Task DisposeAsync()
        {
            Environment.SetEnvironmentVariable(_envLicenseKey, _previousLicenseKey);

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
        public async Task License_write_with_empty_value_returns_BadInvalidArgument()
        {
            NodeId licenseId = await ResolveLicenseNodeIdAsync().ConfigureAwait(false);

            StatusCode status = await WriteLicenseAsync(licenseId, string.Empty).ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, status);
        }

        [Fact]
        public async Task License_write_without_LICENSE_KEY_env_returns_BadNotSupported()
        {
            Environment.SetEnvironmentVariable(_envLicenseKey, null);

            NodeId licenseId = await ResolveLicenseNodeIdAsync().ConfigureAwait(false);

            StatusCode status = await WriteLicenseAsync(licenseId, "any-value").ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadNotSupported, status);
        }

        [Fact]
        public async Task License_write_with_wrong_key_returns_BadInvalidArgument()
        {
            Environment.SetEnvironmentVariable(_envLicenseKey, "the-correct-key");

            NodeId licenseId = await ResolveLicenseNodeIdAsync().ConfigureAwait(false);

            StatusCode status = await WriteLicenseAsync(licenseId, "wrong-key").ConfigureAwait(false);

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, status);
        }

        [Fact]
        public async Task License_write_with_correct_key_succeeds_and_value_round_trips()
        {
            const string licenseKey = "the-correct-key-12345";
            Environment.SetEnvironmentVariable(_envLicenseKey, licenseKey);

            NodeId licenseId = await ResolveLicenseNodeIdAsync().ConfigureAwait(false);

            StatusCode status = await WriteLicenseAsync(licenseId, licenseKey).ConfigureAwait(false);
            Assert.True(StatusCode.IsGood(status), $"License write returned {status}");

            DataValue read = await _session.ReadValueAsync(licenseId).ConfigureAwait(false);
            Assert.True(StatusCode.IsGood(read.StatusCode));
            Assert.Equal(licenseKey, read.Value?.ToString());
        }

        // ----------------- helpers -----------------

        private async Task<NodeId> ResolveLicenseNodeIdAsync()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);

            ReferenceDescriptionCollection topChildren = await BrowseChildrenAsync(assetManagement).ConfigureAwait(false);
            ReferenceDescription configRef = topChildren.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, "Configuration", StringComparison.Ordinal));
            Assert.NotNull(configRef);

            NodeId configurationId = ExpandedNodeId.ToNodeId(configRef.NodeId, _session.NamespaceUris);
            ReferenceDescriptionCollection configChildren = await BrowseChildrenAsync(configurationId).ConfigureAwait(false);

            ReferenceDescription licenseRef = configChildren.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, "License", StringComparison.Ordinal));
            Assert.NotNull(licenseRef);

            return ExpandedNodeId.ToNodeId(licenseRef.NodeId, _session.NamespaceUris);
        }

        private async Task<StatusCode> WriteLicenseAsync(NodeId licenseId, string value)
        {
            WriteValueCollection writes = new()
            {
                new WriteValue
                {
                    NodeId = licenseId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue { Value = value }
                }
            };

            WriteResponse response = await _session.WriteAsync(
                null,
                writes,
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.NotNull(response);
            Assert.NotEmpty(response.Results);
            return response.Results[0];
        }

        private async Task<NodeId> ResolveAssetManagementNodeIdAsync()
        {
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(ObjectIds.ObjectsFolder).ConfigureAwait(false);
            ReferenceDescription assetManagement = children.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, "WoTAssetConnectionManagement", StringComparison.Ordinal));

            Assert.NotNull(assetManagement);
            return ExpandedNodeId.ToNodeId(assetManagement.NodeId, _session.NamespaceUris);
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
                "UAEdgeTranslator license-write tests",
                30_000,
                new UserIdentity(),
                null).ConfigureAwait(false);
        }
    }
}
