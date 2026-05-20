namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.Collections.Generic;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Xunit;

    public class MockAssetTests
    {
        [Fact]
        public void Connect_rejects_blank_host()
        {
            MockAsset asset = new();
            Assert.Throws<ArgumentException>(() => asset.Connect("   ", 1));
            Assert.False(asset.IsConnected);
            Assert.Equal(0, asset.ConnectCount);
        }

        [Fact]
        public void Connect_without_port_emits_url_without_port_suffix()
        {
            MockAsset asset = new();
            asset.Connect("device", 0);

            Assert.True(asset.IsConnected);
            Assert.Equal("mock://device", asset.GetRemoteEndpoint());
            Assert.Equal(asset.GetRemoteEndpoint(), asset.LastBaseUrl);
        }

        [Fact]
        public void Repeated_connect_disconnect_increments_counters()
        {
            MockAsset asset = new();

            asset.Connect("device", 1502);
            asset.Disconnect();
            asset.Connect("device", 1502);
            asset.Disconnect();

            Assert.Equal(2, asset.ConnectCount);
            Assert.Equal(2, asset.DisconnectCount);
            Assert.False(asset.IsConnected);
        }

        [Fact]
        public void Read_returns_null_when_tag_was_never_seeded()
        {
            MockAsset asset = new();
            asset.Connect("h", 1);

            object value = asset.Read(new AssetTag { Name = "missing", UnitID = 1 });

            Assert.Null(value);
            Assert.Single(asset.Reads);
        }

        [Fact]
        public void Read_throws_on_null_tag()
        {
            MockAsset asset = new();
            asset.Connect("h", 1);

            Assert.Throws<ArgumentNullException>(() => asset.Read(null));
        }

        [Fact]
        public void Write_throws_on_null_tag()
        {
            MockAsset asset = new();
            asset.Connect("h", 1);

            Assert.Throws<ArgumentNullException>(() => asset.Write(null, 1));
        }

        [Fact]
        public void Seed_rejects_blank_name()
        {
            MockAsset asset = new();
            Assert.ThrowsAny<ArgumentException>(() => asset.Seed(string.Empty, 1));
            Assert.ThrowsAny<ArgumentException>(() => asset.Seed(null, 1));
        }

        [Fact]
        public void ExecuteAction_throws_on_null_method()
        {
            MockAsset asset = new();
            asset.Connect("h", 1);
            IList<object> outputs = new List<object>();

            Assert.Throws<ArgumentNullException>(() =>
                asset.ExecuteAction(null, new List<object>(), ref outputs));
        }

        [Fact]
        public void ExecuteAction_blocks_when_disconnected()
        {
            MockAsset asset = new();
            // never connected
            MethodState method = new(parent: null) { BrowseName = new QualifiedName("op") };
            IList<object> outputs = new List<object>();

            Assert.Throws<InvalidOperationException>(() =>
                asset.ExecuteAction(method, new List<object>(), ref outputs));
        }

        [Fact]
        public void ExecuteAction_handles_null_inputs_with_empty_list()
        {
            MockAsset asset = new();
            asset.Connect("h", 1);
            MethodState method = new(parent: null) { BrowseName = new QualifiedName("ping") };
            IList<object> outputs = null;

            string result = asset.ExecuteAction(method, null, ref outputs);

            Assert.Equal("mock:ping:ok", result);
            Assert.NotNull(outputs);
            Assert.Single(outputs);
            Assert.Single(asset.Actions);
        }

        [Fact]
        public void Concurrent_writes_record_every_value()
        {
            MockAsset asset = new();
            asset.Connect("h", 1);
            AssetTag tag = new() { Name = "x" };

            System.Threading.Tasks.Parallel.For(0, 100, i => asset.Write(tag, i));

            Assert.Equal(100, asset.Writes.Count);
        }
    }
}
