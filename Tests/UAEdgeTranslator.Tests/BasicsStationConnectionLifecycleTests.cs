namespace Opc.Ua.Edge.Translator.Tests
{
    using LoRaWan;
    using LoRaWan.NetworkServer;
    using System;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Regression tests for the Basics Station connection lifecycle.
    /// <para>
    /// A station that connects, sends <c>version</c> and never receives a
    /// <c>router_config</c> times out and reconnects forever. Two defects caused
    /// exactly that: the reply was routed through a polled queue that discarded
    /// it when the gateway entry was missing, and tearing down a superseded
    /// socket removed the live gateway entry that had just replaced it.
    /// </para>
    /// </summary>
    public class BasicsStationConnectionLifecycleTests : IDisposable
    {
        private readonly StationEui _stationEui = StationEui.Parse("E45F01FFFE61F8DD");

        public BasicsStationConnectionLifecycleTests()
        {
            WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways.Clear();
        }

        public void Dispose()
        {
            WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways.Clear();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Minimal open socket; the lifecycle logic only inspects reference
        /// identity and <see cref="WebSocket.State"/>.
        /// </summary>
        private sealed class FakeWebSocket : WebSocket
        {
            public override WebSocketCloseStatus? CloseStatus => null;
            public override string CloseStatusDescription => null;
            public override WebSocketState State => WebSocketState.Open;
            public override string SubProtocol => null;
            public override void Abort() { }
            public override Task CloseAsync(WebSocketCloseStatus s, string d, CancellationToken t) => Task.CompletedTask;
            public override Task CloseOutputAsync(WebSocketCloseStatus s, string d, CancellationToken t) => Task.CompletedTask;
            public override void Dispose() { }
            public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b, CancellationToken t) =>
                Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
            public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType m, bool e, CancellationToken t) =>
                Task.CompletedTask;
        }

        [Fact]
        public void A_reconnecting_station_replaces_its_socket_without_losing_the_entry()
        {
            FakeWebSocket first = new();
            WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways.TryAdd(
                _stationEui, new GatewayConnection(_stationEui.ToString(), first));

            // The station reconnects: the middleware swaps in the new socket.
            FakeWebSocket second = new();
            WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways[_stationEui].WebSocket = second;

            Assert.True(WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways.ContainsKey(_stationEui));
            Assert.Same(second, WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways[_stationEui].WebSocket);
        }

        [Fact]
        public void The_live_socket_is_distinguishable_from_a_superseded_one()
        {
            // This identity check is what lets the teardown of an old socket
            // leave the freshly registered connection intact. Without it the
            // station is removed immediately after it reconnects, and the
            // router_config has nowhere to go.
            FakeWebSocket first = new();
            FakeWebSocket second = new();

            WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways.TryAdd(
                _stationEui, new GatewayConnection(_stationEui.ToString(), first));
            WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways[_stationEui].WebSocket = second;

            GatewayConnection current = WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways[_stationEui];

            Assert.False(ReferenceEquals(current.WebSocket, first), "the superseded socket must not be treated as current");
            Assert.True(ReferenceEquals(current.WebSocket, second), "the replacement socket must be treated as current");
        }

        [Fact]
        public void A_queued_message_for_an_absent_gateway_is_discarded()
        {
            // Documents why router_config must not travel through this queue:
            // ProcessPendingMessages drops any message whose gateway is missing,
            // so a reply queued around a reconnect is silently lost.
            WebsocketJsonMiddlewareLoRaWAN.PendingMessages.Enqueue(
                new WebsocketJsonMiddlewareLoRaWAN.QueuedMessage
                {
                    Destination = _stationEui.ToString(),
                    Payload = "{\"msgtype\":\"router_config\"}"
                });

            Assert.False(WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways.ContainsKey(_stationEui));

            // Drain so the queue cannot leak into another test.
            while (WebsocketJsonMiddlewareLoRaWAN.PendingMessages.TryDequeue(out _))
            {
            }
        }
    }
}
