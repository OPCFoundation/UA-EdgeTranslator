// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using LoRaWan.NetworkServer.BasicsStation;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.AspNetCore.Http;
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models;
    using Serilog;
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Linq;
    using System.Net.NetworkInformation;
    using System.Net.WebSockets;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using static LoRaWANContainer.LoRaWan.NetworkServer.Models.LnsData;

    public class WebsocketJsonMiddlewareLoRaWAN
    {
        public class QueuedMessage
        {
            public string Destination { get; set; }

            public string Payload { get; set; }
        }

        public static ConcurrentQueue<QueuedMessage> PendingMessages { get; } = new();

        public static ConcurrentDictionary<string, GatewayConnection> ConnectedGateways { get; } = new();

        private readonly RequestDelegate _next;
        private readonly BasicsStationConfigurationService _basicsStationConfigurationService;
        private readonly DownlinkMessageSender _downstreamMessageSender;
        private readonly MessageDispatcher _messageDispatcher;

        public WebsocketJsonMiddlewareLoRaWAN(
            RequestDelegate next,
            BasicsStationConfigurationService basicsStationConfigurationService,
            DownlinkMessageSender downstreamMessageSender,
            MessageDispatcher messageDispatcher)
        {
            _next = next;
            _basicsStationConfigurationService = basicsStationConfigurationService;
            _downstreamMessageSender = downstreamMessageSender;
            _messageDispatcher = messageDispatcher;

            _ = Task.Run(ProcessPendingMessages);
        }

        private async Task ProcessPendingMessages()
        {
            while (true)
            {
                // process commands every 10ms
                await Task.Delay(10).ConfigureAwait(false);

                // handle commands initiated by the central system
                while (PendingMessages.Count > 0)
                {
                    QueuedMessage message = PendingMessages.First();
                    string gatewayName = message.Destination;

                    try
                    {
                        if (!ConnectedGateways.ContainsKey(gatewayName))
                        {
                            Log.Logger.Error($"Gateway {gatewayName} not found in the connected gateways list!");
                            PendingMessages.TryDequeue(out _);
                            continue;
                        }

                        if (ConnectedGateways[gatewayName].WebSocket.State == WebSocketState.Open)
                        {
                            try
                            {
                                await ConnectedGateways[gatewayName].WebSocket.SendAsync(
                                    Encoding.UTF8.GetBytes(message.Payload),
                                    WebSocketMessageType.Text,
                                    true,
                                    CancellationToken.None).ConfigureAwait(false);

                            }
                            catch (Exception ex)
                            {
                                Log.Logger.Error("Exception: " + ex.Message);
                            }
                        }
                        else
                        {
                            Log.Logger.Error($"WebSocket for gateway {gatewayName} is not open. Cannot send request.");
                        }

                        PendingMessages.TryDequeue(out _);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error($"Exception while processing command for gateway {gatewayName}: {ex.Message}");
                    }
                }
            }
        }

        public async Task Invoke(HttpContext httpContext)
        {
            try
            {
                if (httpContext.WebSockets.IsWebSocketRequest)
                {
                    await AddWebSocketAsync(httpContext).ConfigureAwait(false);
                    return;
                }

                // passed on to next middleware
                await _next(httpContext).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);

                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

                await httpContext.Response.WriteAsync(ex.Message).ConfigureAwait(false);
            }
        }

        public static string ExecuteCommand(string gatewayName, string command, string[] inputArgs, string[] outputArgs)
        {
            if (!ConnectedGateways.ContainsKey(gatewayName))
            {
                Log.Logger.Error($"Gateway {gatewayName} not found in the connected gateway list!");
                return null;
            }

            if (ConnectedGateways[gatewayName].WebSocket.State != WebSocketState.Open)
            {
                Log.Logger.Error($"WebSocket for gateway {gatewayName} is not open. Cannot send request.");
                return null;
            }

            string subProtocol = ConnectedGateways[gatewayName].WebSocket.SubProtocol;

            //OCPP16Processor.SendCentralStationCommand(gatewayName, command, inputArgs);

            return string.Empty;
        }

        private async Task AddWebSocketAsync(HttpContext httpContext)
        {
            try
            {
                string gatewayName = httpContext.Request.Path.Value.TrimEnd('/').Split('/').LastOrDefault();

                WebSocket socket = await httpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                if (socket == null || socket.State != WebSocketState.Open)
                {
                    Log.Logger.Error("Accepting websocket failed!");
                    await _next(httpContext).ConfigureAwait(false);
                    return;
                }

                if (gatewayName != "router-info")
                {
                    // store the websocket connection in the dictionary
                    if (!ConnectedGateways.ContainsKey(gatewayName))
                    {
                        ConnectedGateways.TryAdd(gatewayName, new GatewayConnection(gatewayName, socket));
                    }
                    else
                    {
                        try
                        {
                            var oldSocket = ConnectedGateways[gatewayName].WebSocket;
                            ConnectedGateways[gatewayName].WebSocket = socket;

                            if (oldSocket != null)
                            {
                                Log.Logger.Information($"New websocket request received for {gatewayName}");
                                if (oldSocket != socket && oldSocket.State != WebSocketState.Closed)
                                {
                                    Log.Logger.Information($"Closing old websocket for {gatewayName}");

                                    await oldSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client sent new websocket request", CancellationToken.None).ConfigureAwait(false);
                                }
                            }

                            Log.Logger.Information($"Websocket replaced successfully for {gatewayName}");
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Error("Exception: " + ex.Message);
                        }
                    }
                }

                if (socket.State == WebSocketState.Open)
                {
                    await SendAndReceiveAsync(gatewayName, socket, httpContext).ConfigureAwait(false);
                }
                else
                {
                    await RemoveWebSocketAsync(gatewayName, socket).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
            }
        }

        private async Task RemoveWebSocketAsync(string gatewayName, WebSocket webSocket)
        {
            try
            {
                if (gatewayName != "router-info")
                {
                    if (ConnectedGateways.TryRemove(gatewayName, out GatewayConnection gateway))
                    {
                        Log.Logger.Information($"Removed gateway {gatewayName}");
                    }
                    else
                    {
                        Log.Logger.Error($"Cannot remove gateway {gatewayName}");
                    }
                }

                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client requested closed", CancellationToken.None).ConfigureAwait(false);
                Log.Logger.Information($"Closed websocket for gateway {gatewayName}. Remaining active gateways : {ConnectedGateways.Count}");
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
            }
        }

        private async Task SendAndReceiveAsync(string gatewayName, WebSocket webSocket, HttpContext context)
        {
            while (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    CancellationToken cancellationToken = CancellationToken.None;

                    string payloadString = await ReceiveDataFromWebSocketAsync(webSocket, gatewayName).ConfigureAwait(false);
                    if (payloadString != null)
                    {
                        if (gatewayName == "router-info")
                        {
                            // handle discovery request
                            DiscoveryMessage discoMessage = JsonConvert.DeserializeObject<DiscoveryMessage>(payloadString);
                            StationEui stationEui = StationEui.Parse(discoMessage.Router);

                            Log.Logger.Information("Received discovery request from: {StationEui}", stationEui);

                            // store the websocket connection in the dictionary
                            if (!ConnectedGateways.ContainsKey(stationEui.ToString()))
                            {
                                ConnectedGateways.TryAdd(stationEui.ToString(), new GatewayConnection(stationEui.ToString(), webSocket));
                            }
                            else
                            {
                                try
                                {
                                    var oldSocket = ConnectedGateways[stationEui.ToString()].WebSocket;
                                    ConnectedGateways[stationEui.ToString()].WebSocket = webSocket;

                                    if (oldSocket != null)
                                    {
                                        Log.Logger.Information($"New websocket request received for {stationEui}");
                                        if (oldSocket != webSocket && oldSocket.State != WebSocketState.Closed)
                                        {
                                            Log.Logger.Information($"Closing old websocket for {stationEui}");

                                            await oldSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client sent new websocket request", CancellationToken.None).ConfigureAwait(false);
                                        }
                                    }

                                    Log.Logger.Information($"Websocket replaced successfully for {stationEui}");
                                }
                                catch (Exception ex)
                                {
                                    Log.Logger.Error("Exception: " + ex.Message);
                                }
                            }

                            // send response
                            try
                            {
                                UriBuilder uriBuilder = new()
                                {
                                    Scheme = context.Request.IsHttps ? "wss" : "ws",
                                    Host = context.Request.Host.Host
                                };

                                if (context.Request.Host.Port is { } somePort)
                                {
                                    uriBuilder.Port = somePort;
                                }

                                Uri dataEnpoint = new(new Uri(uriBuilder.Uri.AbsoluteUri), $"router-data/{stationEui}");

                                var networkInterface = NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(ni => ni
                                    .GetIPProperties()
                                    .UnicastAddresses
                                    .Any(info => info.Address.Equals(context.Connection.LocalIpAddress)));

                                string response = JsonConvert.SerializeObject(new DiscoveryResponseMessage()
                                {
                                    Router = Id6.Format(stationEui.AsUInt64, Id6.FormatOptions.Lowercase),
                                    Muxs = Id6.Format(networkInterface is { } someNetworkInterface ? someNetworkInterface.GetPhysicalAddress().Convert48To64() : 0, Id6.FormatOptions.FixedWidth),
                                    Uri = dataEnpoint.ToString()
                                });

                                PendingMessages.Enqueue(new QueuedMessage { Destination = stationEui.ToString(), Payload = response });
                            }
                            catch (Exception ex)
                            {
                                Log.Logger.Error($"Exception while processing discovery request: {ex.Message}");

                                string response = JsonConvert.SerializeObject(new DiscoveryErrorMessage()
                                {
                                    Router = Id6.Format(stationEui.AsUInt64, Id6.FormatOptions.Lowercase),
                                    Error = ex.Message
                                });

                                PendingMessages.Enqueue(new QueuedMessage { Destination = stationEui.ToString(), Payload = response });
                                throw;
                            }
                        }
                        else
                        {
                            // handle data request
                            var basic = JsonConvert.DeserializeObject<BasicMessage>(payloadString);
                            switch (basic.MessageType)
                            {
                                case LnsMessageType.Version:
                                    VersionMessage versionMessage = JsonConvert.DeserializeObject<VersionMessage>(payloadString);
                                    Log.Logger.Information("Received 'version' message for station '{StationVersion}' with package '{StationPackage}'.", versionMessage.MessageType, versionMessage.Package);

                                    string routerConfigResponse = await _basicsStationConfigurationService.GetRouterConfigMessageAsync(StationEui.Parse(gatewayName), cancellationToken).ConfigureAwait(false);
                                    PendingMessages.Enqueue(new QueuedMessage { Destination = gatewayName, Payload = routerConfigResponse });
                                    break;

                                case LnsMessageType.JoinRequest:
                                    Log.Logger.Information($"Received jreq message: '{payloadString}'.", payloadString);
                                    try
                                    {
                                        JoinRequestMessage jreq = JsonConvert.DeserializeObject<JoinRequestMessage>(payloadString);
                                        RadioMetadata radioMetadata = new()
                                        {
                                            Frequency = new Hertz(jreq.Frequency),
                                            DataRate = (DataRateIndex)jreq.DR,
                                            UpInfo = jreq.UpInfo
                                        };

                                        var routerRegion = await _basicsStationConfigurationService.GetRegionAsync(StationEui.Parse(gatewayName), cancellationToken).ConfigureAwait(false);

                                        var loraRequest = new LoRaRequest(radioMetadata, _downstreamMessageSender, DateTime.UtcNow);
                                        loraRequest.SetPayload(new LoRaPayloadJoinRequest(JoinEui.Parse(jreq.JoinEui),
                                                                                          DevEui.Parse(jreq.DevEui),
                                                                                          new DevNonce(jreq.DevNonce),
                                                                                          new MessageIntegrityCode(jreq.Mic)));
                                        loraRequest.SetRegion(routerRegion);
                                        loraRequest.SetStationEui(StationEui.Parse(gatewayName));
                                        _messageDispatcher.DispatchRequest(loraRequest);
                                    }
                                    catch (JsonException)
                                    {
                                        Log.Logger.Information("Received unexpected 'jreq' message: {Json}.", payloadString);
                                    }

                                    break;

                                case LnsMessageType.UplinkDataFrame:
                                    Log.Logger.Information($"Received updf message: '{payloadString}'.", payloadString);
                                    try
                                    {
                                        var updf = JsonConvert.DeserializeObject<UpstreamDataMessage>(payloadString);
                                        RadioMetadata radioMetadata = new()
                                        {
                                            Frequency = new Hertz(updf.Frequency),
                                            DataRate = (DataRateIndex)updf.DR,
                                            UpInfo = updf.UpInfo
                                        };

                                        var routerRegion = await _basicsStationConfigurationService.GetRegionAsync(StationEui.Parse(gatewayName), cancellationToken).ConfigureAwait(false);

                                        var loraRequest = new LoRaRequest(radioMetadata, _downstreamMessageSender, DateTime.UtcNow);
                                        loraRequest.SetPayload(new LoRaPayloadData(new DevAddr(updf.DevAddr),
                                                                                   new MacHeader((byte)updf.MacHeader),
                                                                                   (FrameControlFlags)updf.FrameControlFlags,
                                                                                   (ushort)updf.Counter,
                                                                                   updf.Options,
                                                                                   updf.Payload,
                                                                                   (FramePort)updf.Port,
                                                                                   new MessageIntegrityCode(updf.Mic)));
                                        loraRequest.SetRegion(routerRegion);
                                        loraRequest.SetStationEui(StationEui.Parse(gatewayName));
                                        _messageDispatcher.DispatchRequest(loraRequest);
                                    }
                                    catch (JsonException)
                                    {
                                        Log.Logger.Error($"Received unexpected updf message: {payloadString}.", payloadString);
                                    }

                                    break;

                                case LnsMessageType.TransmitConfirmation:
                                    Log.Logger.Information($"Received dntxed message: '{payloadString}'.", payloadString);

                                    break;

                                case var messageType and (LnsMessageType.DownlinkMessage or LnsMessageType.RouterConfig):
                                    throw new NotSupportedException($"'{messageType}' is not a valid message type for this endpoint and is only valid for 'downstream' messages.");

                                case LnsMessageType.TimeSync:
                                    var timeSyncData = JsonConvert.DeserializeObject<TimeSyncMessage>(payloadString);
                                    Log.Logger.Information($"Received TimeSync message: '{payloadString}'.", payloadString);
                                    timeSyncData.GpsTime = (ulong)DateTime.UtcNow.Subtract(new DateTime(1980, 1, 6, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1000; // to microseconds
                                    PendingMessages.Enqueue(new QueuedMessage { Destination = gatewayName, Payload = JsonConvert.SerializeObject(timeSyncData) });

                                    break;

                                case var messageType and (LnsMessageType.ProprietaryDataFrame
                                                          or LnsMessageType.MulticastSchedule
                                                          or LnsMessageType.RunCommand
                                                          or LnsMessageType.RemoteShell):
                                    Log.Logger.Warning("'{MessageType}' ({MessageTypeBasicStationString}) is not handled in current LoRaWan Network Server implementation.", messageType, messageType.ToBasicStationString());

                                    break;

                                default:
                                    throw new SwitchExpressionException();
                            }
                        }

                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("Exception: " + ex.Message);
                }
            }
        }

        private async Task<string> ReceiveDataFromWebSocketAsync(WebSocket webSocket, string gatewayName)
        {
            try
            {
                ArraySegment<byte> data = new ArraySegment<byte>(new byte[1024]);
                WebSocketReceiveResult result;
                string payloadString = string.Empty;

                do
                {
                    result = await webSocket.ReceiveAsync(data, CancellationToken.None).ConfigureAwait(false);

                    // client sent close frame
                    if (result.CloseStatus.HasValue)
                    {
                        if ((gatewayName != "router-info") && (webSocket != ConnectedGateways[gatewayName].WebSocket))
                        {
                            if (webSocket.State != WebSocketState.CloseReceived)
                            {
                                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "New websocket request received for this gateway", CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            await RemoveWebSocketAsync(gatewayName, webSocket).ConfigureAwait(false);
                        }

                        return null;
                    }

                    // append received data
                    payloadString += Encoding.UTF8.GetString(data.Array, 0, result.Count);

                } while (!result.EndOfMessage);

                return payloadString;
            }
            catch (WebSocketException websocex)
            {
                if (webSocket != ConnectedGateways[gatewayName].WebSocket)
                {
                    Log.Logger.Error($"WebsocketException occured in the old socket while receiving payload from gateway {gatewayName}. Error : {websocex.Message}");
                }
                else
                {
                    Log.Logger.Error("Exception: " + websocex.Message);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
            }

            return null;
        }
    }
}
