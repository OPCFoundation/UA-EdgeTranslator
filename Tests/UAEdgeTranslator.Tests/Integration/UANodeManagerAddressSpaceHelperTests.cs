namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.Diagnostics;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Covers <see cref="UANodeManager"/> helpers that need a live address
    /// space, which the pure reflection-based unit tests cannot provide.
    /// <para>
    /// These sit behind the asset onboarding and diagnostics paths, so they are
    /// reached through the real server rather than a stubbed node manager.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class UANodeManagerAddressSpaceHelperTests : IAsyncLifetime
    {
        private const BindingFlags _privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private OpcUaServerFixture _fixture;

        public Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();

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
        public void FindAssetNodeIdByName_returns_null_for_unknown_and_blank_names()
        {
            Assert.Null(InvokeFindAssetNodeIdByName("no-such-asset"));
            Assert.Null(InvokeFindAssetNodeIdByName(string.Empty));
            Assert.Null(InvokeFindAssetNodeIdByName(null));
        }

        [Fact]
        public void GetDefaultBinaryEncodingId_returns_null_when_the_type_has_no_encoding()
        {
            // A structure DataType with no HasEncoding reference must yield null
            // rather than throwing — UANodeManager uses the result to decide
            // whether it can emit an ExtensionObject at all.
            UANodeManager manager = UANodeManager.Instance;
            ushort ns = TranslatorNamespaceIndex(manager);

            NodeId dataTypeId = new("NoEncodingType", ns);

            manager.AddPredefinedNodePublic(new DataTypeState
            {
                NodeId = dataTypeId,
                BrowseName = new QualifiedName("NoEncodingType", ns),
                DisplayName = "NoEncodingType",
                SuperTypeId = DataTypeIds.Structure
            });

            DataTypeState dataType = (DataTypeState)manager.Find(dataTypeId);

            Assert.Null(InvokeGetDefaultBinaryEncodingId(dataType));
        }

        [Fact]
        public void GetDefaultBinaryEncodingId_finds_the_default_binary_encoding_node()
        {
            UANodeManager manager = UANodeManager.Instance;
            ushort ns = TranslatorNamespaceIndex(manager);

            NodeId dataTypeId = new("EncodedType", ns);
            NodeId encodingId = new("EncodedType_DefaultBinary", ns);

            DataTypeState dataType = new()
            {
                NodeId = dataTypeId,
                BrowseName = new QualifiedName("EncodedType", ns),
                DisplayName = "EncodedType",
                SuperTypeId = DataTypeIds.Structure
            };

            BaseObjectState encoding = new(null)
            {
                NodeId = encodingId,

                // The lookup matches on this exact browse name.
                BrowseName = new QualifiedName("Default Binary", ns),
                DisplayName = "Default Binary",
                TypeDefinitionId = ObjectTypeIds.DataTypeEncodingType
            };

            dataType.AddReference(ReferenceTypeIds.HasEncoding, false, encodingId);
            encoding.AddReference(ReferenceTypeIds.HasEncoding, true, dataTypeId);

            manager.AddPredefinedNodePublic(dataType);
            manager.AddPredefinedNodePublic(encoding);

            NodeId resolved = InvokeGetDefaultBinaryEncodingId((DataTypeState)manager.Find(dataTypeId));

            Assert.Equal(encodingId, resolved);
        }

        [Fact]
        public void GetConnectedAssets_reports_the_live_asset_set()
        {
            // Backs the Devices page and the Overview counters.
            IEnumerable<ConnectedAssetInfo> assets = UANodeManager.Instance.GetConnectedAssets();

            Assert.NotNull(assets);

            foreach (ConnectedAssetInfo asset in assets)
            {
                Assert.False(string.IsNullOrEmpty(asset.Name));
                Assert.True(asset.TagCount >= 0);
            }
        }

        [Fact]
        public void GetConnectedAssets_is_consistent_with_the_diagnostics_view()
        {
            // DiagnosticsService projects this same set onto the dashboard, so
            // the two must not disagree.
            DiagnosticsService diagnostics = new();

            List<ConnectedAssetInfo> assets = UANodeManager.Instance.GetConnectedAssets().ToList();
            IReadOnlyList<DeviceStatus> devices = diagnostics.GetConnectedDevices();

            Assert.Equal(assets.Count, devices.Count);

            foreach (ConnectedAssetInfo asset in assets)
            {
                DeviceStatus device = Assert.Single(devices.Where(d => d.Name == asset.Name));

                Assert.Equal(asset.IsConnected, device.IsConnected);
                Assert.Equal(asset.TagCount, device.TagCount);
                Assert.Equal(asset.Endpoint, device.Endpoint);
            }
        }

        private static ushort TranslatorNamespaceIndex(UANodeManager manager) =>
            (ushort)manager.Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/");

        private static NodeId InvokeFindAssetNodeIdByName(string assetName)
        {
            MethodInfo method = typeof(UANodeManager).GetMethod("FindAssetNodeIdByName", _privateInstance);

            Assert.NotNull(method);

            return (NodeId)method.Invoke(UANodeManager.Instance, [assetName]);
        }

        private static NodeId InvokeGetDefaultBinaryEncodingId(DataTypeState dataType)
        {
            MethodInfo method = typeof(UANodeManager).GetMethod("GetDefaultBinaryEncodingId", _privateInstance);

            Assert.NotNull(method);

            return (NodeId)method.Invoke(UANodeManager.Instance, [dataType]);
        }
    }
}
