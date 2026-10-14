namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.WotCon;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// The WoT-Con reconnect handler: backoff, single-flight reconnects and endpoint parsing,
    /// mirroring the v1.02 node manager's per-asset reconnect.
    /// </summary>
    public sealed class WotAssetReconnectorTests
    {
        private sealed class FlakyAsset : IAsset
        {
            public bool IsConnected { get; set; }

            public bool ConnectSucceeds { get; set; }

            public int Connects;

            public string LastHost;

            public int LastPort = -1;

            public string GetRemoteEndpoint() => "eip://10.0.0.5:44818/1,0/";

            public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Connects);
                LastHost = ipAddress;
                LastPort = port;
                IsConnected = ConnectSucceeds;
                return Task.CompletedTask;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken = default)
            {
                IsConnected = false;
                return Task.CompletedTask;
            }

            public Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default) => Task.FromResult<object>(null);

            public Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<AssetActionResult> ExecuteActionAsync(Opc.Ua.MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public ValueTask DisposeAsync() => default;
        }

        [Fact]
        public async Task A_successful_reconnect_clears_the_backoff()
        {
            var asset = new FlakyAsset { ConnectSucceeds = true };

            await WotAssetReconnector.TryReconnectAsync(asset, "plc");

            Assert.True(asset.IsConnected);
            Assert.Equal("10.0.0.5", asset.LastHost);
            Assert.Equal(44818, asset.LastPort);
            Assert.True(WotAssetReconnector.IsAttemptDue(asset));
        }

        [Fact]
        public async Task A_failed_reconnect_backs_off_before_the_next_attempt()
        {
            var asset = new FlakyAsset { ConnectSucceeds = false };

            await WotAssetReconnector.TryReconnectAsync(asset, "plc");
            Assert.False(WotAssetReconnector.IsAttemptDue(asset));

            // Within the backoff window no further attempt is made.
            await WotAssetReconnector.TryReconnectAsync(asset, "plc");
            Assert.Equal(1, asset.Connects);

            WotAssetReconnector.Forget(asset);
            Assert.True(WotAssetReconnector.IsAttemptDue(asset));
        }

        [Theory]
        [InlineData("eip://10.0.0.5/1,0/", "10.0.0.5", 0)]
        [InlineData("opc.tcp://server:4840", "server", 4840)]
        [InlineData("192.168.1.2:502", "192.168.1.2", 502)]
        [InlineData("plc", "plc", 0)]
        public void Endpoints_yield_host_and_port(string endpoint, string host, int port)
        {
            Assert.True(WotAssetReconnector.TryParseEndpoint(endpoint, out string parsedHost, out int parsedPort));
            Assert.Equal(host, parsedHost);
            Assert.Equal(port, parsedPort);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("bad://")]
        public void Unusable_endpoints_are_rejected(string endpoint)
        {
            Assert.False(WotAssetReconnector.TryParseEndpoint(endpoint, out _, out _));
        }
    }
}
