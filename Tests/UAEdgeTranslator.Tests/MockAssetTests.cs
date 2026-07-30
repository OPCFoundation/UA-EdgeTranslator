namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.Threading.Tasks;
    using System.Linq;
    using System.Collections.Generic;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Xunit;

    public class MockAssetTests
    {
        [Fact]
        public async Task Connect_rejects_blank_host()
        {
            MockAsset asset = new();
            await Assert.ThrowsAsync<ArgumentException>(() => asset.ConnectAsync("   ", 1));
            Assert.False(asset.IsConnected);
            Assert.Equal(0, asset.ConnectCount);
        }

        [Fact]
        public async Task Connect_without_port_emits_url_without_port_suffix()
        {
            MockAsset asset = new();
            await asset.ConnectAsync("device", 0);

            Assert.True(asset.IsConnected);
            Assert.Equal("mock://device", asset.GetRemoteEndpoint());
            Assert.Equal(asset.GetRemoteEndpoint(), asset.LastBaseUrl);
        }

        [Fact]
        public async Task Repeated_connect_disconnect_increments_counters()
        {
            MockAsset asset = new();

            await asset.ConnectAsync("device", 1502);
            await asset.DisconnectAsync();
            await asset.ConnectAsync("device", 1502);
            await asset.DisconnectAsync();

            Assert.Equal(2, asset.ConnectCount);
            Assert.Equal(2, asset.DisconnectCount);
            Assert.False(asset.IsConnected);
        }

        [Fact]
        public async Task Read_returns_null_when_tag_was_never_seeded()
        {
            MockAsset asset = new();
            await asset.ConnectAsync("h", 1);

            object value = await asset.ReadAsync(new AssetTag { Name = "missing", UnitID = 1 });

            Assert.Null(value);
            Assert.Single(asset.Reads);
        }

        [Fact]
        public async Task Read_throws_on_null_tag()
        {
            MockAsset asset = new();
            await asset.ConnectAsync("h", 1);

            await Assert.ThrowsAsync<ArgumentNullException>(() => asset.ReadAsync(null));
        }

        [Fact]
        public async Task Write_throws_on_null_tag()
        {
            MockAsset asset = new();
            await asset.ConnectAsync("h", 1);

            await Assert.ThrowsAsync<ArgumentNullException>(() => asset.WriteAsync(null, 1));
        }

        [Fact]
        public async Task Seed_rejects_blank_name()
        {
            MockAsset asset = new();
            Assert.ThrowsAny<ArgumentException>(() => asset.Seed(string.Empty, 1));
            Assert.ThrowsAny<ArgumentException>(() => asset.Seed(null, 1));
        }

        [Fact]
        public async Task ExecuteAction_throws_on_null_method()
        {
            MockAsset asset = new();
            await asset.ConnectAsync("h", 1);

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                asset.ExecuteActionAsync(null, new List<object>()));
        }

        [Fact]
        public async Task ExecuteAction_blocks_when_disconnected()
        {
            MockAsset asset = new();
            // never connected
            MethodState method = new(parent: null) { BrowseName = new QualifiedName("op") };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                asset.ExecuteActionAsync(method, new List<object>()));
        }

        [Fact]
        public async Task ExecuteAction_handles_null_inputs_with_empty_list()
        {
            MockAsset asset = new();
            await asset.ConnectAsync("h", 1);
            MethodState method = new(parent: null) { BrowseName = new QualifiedName("ping") };

            AssetActionResult result = await asset.ExecuteActionAsync(method, null);

            Assert.Equal("mock:ping:ok", result.Status);
            Assert.NotNull(result.Outputs);
            Assert.Single(result.Outputs);
            Assert.Single(asset.Actions);
        }

        [Fact]
        public async Task Concurrent_writes_record_every_value()
        {
            MockAsset asset = new();
            await asset.ConnectAsync("h", 1);
            AssetTag tag = new() { Name = "x" };

            await Task.WhenAll(Enumerable.Range(0, 100).Select(i => asset.WriteAsync(tag, i)));

            Assert.Equal(100, asset.Writes.Count);
        }
    }
}
