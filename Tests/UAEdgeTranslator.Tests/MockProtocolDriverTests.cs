namespace Opc.Ua.Edge.Translator.Tests
{
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System.Threading.Tasks;
    using Xunit;

    public class MockProtocolDriverTests
    {
        [Fact]
        public async Task Driver_advertises_scheme_and_binding_uri()
        {
            MockProtocolDriver driver = new();

            Assert.Equal("mock", driver.Scheme);
            Assert.False(string.IsNullOrWhiteSpace(driver.WoTBindingUri));
        }

        [Fact]
        public async Task Discover_returns_deterministic_endpoints()
        {
            MockProtocolDriver driver = new();
            List<string> first = new(driver.Discover());
            List<string> second = new(driver.Discover());

            Assert.NotEmpty(first);
            Assert.Equal(first, second);
            Assert.All(first, ep => Assert.StartsWith("mock://", ep));
        }

        [Fact]
        public async Task BrowseAndGenerateTD_returns_well_formed_td_with_properties_and_actions()
        {
            MockProtocolDriver driver = new();

            ThingDescription td = driver.BrowseAndGenerateTD("device1", "mock://device1:1502/1");

            Assert.NotNull(td);
            Assert.Equal("device1", td.Name);
            Assert.Equal("device1", td.Title);
            Assert.Equal("urn:device1", td.Id);
            Assert.Equal("mock://device1:1502/1", td.Base);
            Assert.NotNull(td.Properties);
            Assert.Contains("temperature", td.Properties.Keys);
            Assert.NotNull(td.Actions);
            Assert.Contains("reset", td.Actions.Keys);
        }

        [Theory]
        [InlineData(null, "mock://x:1/1")]
        [InlineData("", "mock://x:1/1")]
        [InlineData("device1", null)]
        [InlineData("device1", "")]
        public async Task BrowseAndGenerateTD_rejects_invalid_arguments(string assetName, string assetEndpoint)
        {
            MockProtocolDriver driver = new();

            Assert.ThrowsAny<System.ArgumentException>(() =>
                driver.BrowseAndGenerateTD(assetName, assetEndpoint));
        }

        [Fact]
        public async Task CreateAndConnectAsset_returns_connected_asset_with_parsed_unitId()
        {
            MockProtocolDriver driver = new();
            ThingDescription td = driver.BrowseAndGenerateTD("device1", "mock://device1:1502/7");

            AssetConnection connection = await driver.CreateAndConnectAssetAsync(td);
            IAsset asset = connection.Asset;

            Assert.NotNull(asset);
            Assert.True(asset.IsConnected);
            Assert.Equal(7, connection.UnitId);
            Assert.Equal("mock://device1:1502", asset.GetRemoteEndpoint());
        }

        [Fact]
        public async Task CreateAndConnectAsset_defaults_unitId_when_path_is_not_numeric()
        {
            MockProtocolDriver driver = new();
            ThingDescription td = driver.BrowseAndGenerateTD("device1", "mock://device1:1502/main");

            AssetConnection connection = await driver.CreateAndConnectAssetAsync(td);

            Assert.Equal(1, connection.UnitId);
        }

        [Fact]
        public async Task CreateAndConnectAsset_rejects_wrong_scheme()
        {
            MockProtocolDriver driver = new();
            ThingDescription td = driver.BrowseAndGenerateTD("device1", "modbus+tcp://1.2.3.4:502/1");

            await Assert.ThrowsAsync<System.ArgumentException>(() => driver.CreateAndConnectAssetAsync(td));
        }

        [Fact]
        public async Task CreateAndConnectAsset_rejects_null_td()
        {
            MockProtocolDriver driver = new();

            await Assert.ThrowsAsync<System.ArgumentNullException>(() => driver.CreateAndConnectAssetAsync(null));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task CreateAndConnectAsset_rejects_missing_base_uri(string baseUri)
        {
            MockProtocolDriver driver = new();
            ThingDescription td = new() { Base = baseUri, Name = "x", Title = "x", Id = "urn:x" };

            await Assert.ThrowsAsync<System.ArgumentException>(() => driver.CreateAndConnectAssetAsync(td));
        }

        [Fact]
        public async Task CreateAndConnectAsset_rejects_relative_base_uri()
        {
            MockProtocolDriver driver = new();
            ThingDescription td = new() { Base = "not-a-uri", Name = "x", Title = "x", Id = "urn:x" };

            await Assert.ThrowsAsync<System.ArgumentException>(() => driver.CreateAndConnectAssetAsync(td));
        }

        [Fact]
        public async Task CreateTag_throws_when_form_payload_cannot_be_deserialized_as_MockForm()
        {
            MockProtocolDriver driver = new();
            ThingDescription td = driver.BrowseAndGenerateTD("device1", "mock://device1:1502/1");

            // A JSON literal "null" deserializes to null MockForm and triggers the validation throw.
            Assert.Throws<System.ArgumentException>(() =>
                driver.CreateTag(td, "null", "device1", 1, "tag", "ns=2;s=t", string.Empty));
        }

        [Fact]
        public async Task CreateTag_round_trips_form_payload_into_AssetTag()
        {
            MockProtocolDriver driver = new();
            ThingDescription td = driver.BrowseAndGenerateTD("device1", "mock://device1:1502/1");

            // Use the same form shape the driver emits so the round-trip exercises
            // the production JSON contract.
            object form = td.Properties["temperature"].Forms[0];
            string formJson = JsonConvert.SerializeObject(form);

            AssetTag tag = driver.CreateTag(
                td,
                JsonConvert.DeserializeObject(formJson),
                assetId: "device1",
                unitId: 5,
                variableId: "temperature",
                mappedUAExpandedNodeId: "ns=2;s=temperature",
                mappedUAFieldPath: string.Empty);

            Assert.Equal("temperature", tag.Name);
            Assert.Equal("/temperature", tag.Address);
            Assert.Equal(5, tag.UnitID);
            Assert.Equal("xsd:float", tag.Type);
            Assert.Equal(1000, tag.PollingInterval);
            Assert.Equal("ns=2;s=temperature", tag.MappedUAExpandedNodeID);
        }

        [Fact]
        public async Task Asset_records_reads_writes_and_actions()
        {
            MockProtocolDriver driver = new();
            ThingDescription td = driver.BrowseAndGenerateTD("device1", "mock://device1:1502/1");
            MockAsset asset = (MockAsset)(await driver.CreateAndConnectAssetAsync(td)).Asset;

            AssetTag tag = new() { Name = "temperature", UnitID = 1, Type = "xsd:float" };

            // pre-seed and read
            asset.Seed("temperature", 21.5f);
            object read = await asset.ReadAsync(tag);
            Assert.Equal(21.5f, read);

            // write and re-read; the new value should win
            await asset.WriteAsync(tag, 99.9f);
            Assert.Equal(99.9f, await asset.ReadAsync(tag));

            // execute an action
            MethodState method = new(parent: null) { BrowseName = new QualifiedName("reset") };
            AssetActionResult result = await asset.ExecuteActionAsync(method, new List<object> { "arg" });

            Assert.Equal("mock:reset:ok", result.Status);
            Assert.Single(result.Outputs);
            Assert.Equal("mock:reset:ok", result.Outputs[0]);

            // recorded interactions
            Assert.Equal(2, asset.Reads.Count);
            Assert.Single(asset.Writes);
            Assert.Single(asset.Actions);
            Assert.Equal(1, asset.ConnectCount);
        }

        [Fact]
        public async Task Asset_disconnect_blocks_subsequent_io()
        {
            MockProtocolDriver driver = new();
            ThingDescription td = driver.BrowseAndGenerateTD("device1", "mock://device1:1502/1");
            MockAsset asset = (MockAsset)(await driver.CreateAndConnectAssetAsync(td)).Asset;

            await asset.DisconnectAsync();

            Assert.False(asset.IsConnected);
            Assert.Equal(1, asset.DisconnectCount);

            AssetTag tag = new() { Name = "temperature", UnitID = 1, Type = "xsd:float" };
            await Assert.ThrowsAsync<System.InvalidOperationException>(() => asset.ReadAsync(tag));
            await Assert.ThrowsAsync<System.InvalidOperationException>(() => asset.WriteAsync(tag, 1.0f));
        }

        [Fact]
        public async Task Driver_routes_through_ProtocolDriverRegistry()
        {
            // Contract test: a freshly registered mock driver must be reachable
            // via TryGetByUri using the base URI it advertises in its TDs.
            ProtocolDriverRegistry registry = new();
            MockProtocolDriver driver = new();
            registry.Register(driver);

            ThingDescription td = driver.BrowseAndGenerateTD("device1", "mock://device1:1502/1");

            Assert.True(registry.TryGetByUri(td.Base, out var resolved));
            Assert.Same(driver, resolved);
        }
    }
}
