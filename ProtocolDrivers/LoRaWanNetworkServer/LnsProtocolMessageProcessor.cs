// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace LoRaWan.NetworkServer.BasicsStation.Processors
{
    using LoRaTools.NetworkServerDiscovery;
    using LoRaWANContainer.LoRaWan.NetworkServer;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using static LoRaWANContainer.LoRaWan.NetworkServer.Models.LnsData;

    internal class LnsProtocolMessageProcessor(BasicsStationConfigurationService basicsStationConfigurationService,
                                       WebSocketWriterRegistry<StationEui, string> socketWriterRegistry,
                                       DownstreamMessageSender downstreamMessageSender,
                                       MessageDispatcher messageDispatcher,
                                       ILoggerFactory loggerFactory,
                                       ILogger<LnsProtocolMessageProcessor> logger)
    {
        private static readonly Action<ILogger, string, string, Exception> LogReceivedMessage =
            LoggerMessage.Define<string, string>(LogLevel.Information, default, "Received '{Type}' message: '{Json}'.");


        public static readonly DateTime GpsEpoch = new DateTime(1980, 1, 6, 0, 0, 0, DateTimeKind.Utc);

        public Task HandleDiscoveryAsync(HttpContext httpContext, CancellationToken cancellationToken) =>
            ExecuteWithExceptionHandlingAsync(async () =>
            {
                var uriBuilder = new UriBuilder
                {
                    Scheme = httpContext.Request.IsHttps ? "wss" : "ws",
                    Host = httpContext.Request.Host.Host
                };

                if (httpContext.Request.Host.Port is { } somePort)
                    uriBuilder.Port = somePort;

                var discoveryService = new DiscoveryService(new LocalLnsDiscovery(uriBuilder.Uri), loggerFactory.CreateLogger<DiscoveryService>());
                await discoveryService.HandleDiscoveryRequestAsync(httpContext, cancellationToken).ConfigureAwait(false);
                return 0;
            });

        public Task HandleDataAsync(HttpContext httpContext, CancellationToken cancellationToken) =>
            ExecuteWithExceptionHandlingAsync(async () =>
            {
                var webSocketConnection = new WebSocketConnection(httpContext, logger);
                return await webSocketConnection.HandleAsync((httpContext, socket, ct) => InternalHandleDataAsync(httpContext.GetRouteData().Values, socket, ct), cancellationToken).ConfigureAwait(false);
            });

        internal async Task InternalHandleDataAsync(RouteValueDictionary routeValues, WebSocket socket, CancellationToken cancellationToken)
        {
            var stationEui = routeValues.TryGetValue(BasicsStationNetworkServer.RouterIdPathParameterName, out var sEui) ?
                StationEui.Parse(sEui.ToString())
                : throw new InvalidOperationException($"{BasicsStationNetworkServer.RouterIdPathParameterName} was not present on path.");

            using var scope = logger.BeginEuiScope(stationEui);
            var channel = new WebSocketTextChannel(socket, sendTimeout: TimeSpan.FromSeconds(3));
            var handle = socketWriterRegistry.Register(stationEui, channel);

            try
            {
                using var cancellationTokenSource = new CancellationTokenSource();
                using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);
                var task = channel.ProcessSendQueueAsync(linkedCancellationTokenSource.Token);

                await using var message = socket.ReadTextMessages(cancellationToken);
                while (await message.MoveNextAsync().ConfigureAwait(false))
                {
                    await HandleDataMessageAsync(stationEui, handle, message.Current, cancellationToken).ConfigureAwait(false);
                }

                cancellationTokenSource.Cancel(); // cancel send queue processing, then...

                try
                {
                    await task.ConfigureAwait(false); // ...wait for its task to complete (practically instantaneous)
                }
                catch (OperationCanceledException)
                {
                    // ignore because it is expected
                }
            }
            finally
            {
                _ = socketWriterRegistry.Deregister(stationEui);
            }
        }

        private async Task HandleDataMessageAsync(StationEui stationEui,
                                                  IWebSocketWriterHandle<string> socket,
                                                  string json,
                                                  CancellationToken cancellationToken)
        {
            var basic = JsonConvert.DeserializeObject<BasicMessage>(json);
            switch (basic.MessageType)
            {
                case LnsMessageType.Version:
                    var versionMessage = JsonConvert.DeserializeObject<VersionMessage>(json);
                    logger.LogInformation("Received 'version' message for station '{StationVersion}' with package '{StationPackage}'.", versionMessage.MessageType, versionMessage.Package);

                    var routerConfigResponse = await basicsStationConfigurationService.GetRouterConfigMessageAsync(stationEui, cancellationToken).ConfigureAwait(false);
                    await socket.SendAsync(routerConfigResponse, cancellationToken).ConfigureAwait(false);

                    break;

                case LnsMessageType.JoinRequest:
                    LogReceivedMessage(logger, "jreq", json, null);
                    try
                    {
                        JoinRequestMessage jreq = JsonConvert.DeserializeObject<JoinRequestMessage>(json);
                        RadioMetadata radioMetadata = new()
                        {
                            Frequency = new Hertz(jreq.Frequency),
                            DataRate = (DataRateIndex)jreq.DR,
                            UpInfo = jreq.UpInfo
                        };

                        var routerRegion = await basicsStationConfigurationService.GetRegionAsync(stationEui, cancellationToken).ConfigureAwait(false);

                        var loraRequest = new LoRaRequest(radioMetadata, downstreamMessageSender, DateTime.UtcNow);
                        loraRequest.SetPayload(new LoRaPayloadJoinRequest(JoinEui.Parse(jreq.JoinEui),
                                                                          DevEui.Parse(jreq.DevEui),
                                                                          new DevNonce(jreq.DevNonce),
                                                                          new MessageIntegrityCode(jreq.Mic)));
                        loraRequest.SetRegion(routerRegion);
                        loraRequest.SetStationEui(stationEui);
                        messageDispatcher.DispatchRequest(loraRequest);

                    }
                    catch (JsonException)
                    {
                        logger.LogInformation("Received unexpected 'jreq' message: {Json}.", json);
                    }

                    break;

                case LnsMessageType.UplinkDataFrame:
                    LogReceivedMessage(logger, "updf", json, null);
                    try
                    {
                        var updf = JsonConvert.DeserializeObject<UpstreamDataMessage>(json);

                        using var scope = logger.BeginDeviceAddressScope(DevAddr.Parse(updf.DevAddr.ToString()));

                        var routerRegion = await basicsStationConfigurationService.GetRegionAsync(stationEui, cancellationToken).ConfigureAwait(false);

                        var loraRequest = new LoRaRequest(updf.RadioMetadata, downstreamMessageSender, DateTime.UtcNow);
                        loraRequest.SetPayload(new LoRaPayloadData(new DevAddr(updf.DevAddr),
                                                                   new MacHeader((byte)updf.MacHeader),
                                                                   (FrameControlFlags)updf.FrameControlFlags,
                                                                   (ushort)updf.Counter,
                                                                   updf.Options,
                                                                   updf.Payload,
                                                                   (FramePort)updf.Port,
                                                                   new MessageIntegrityCode(updf.Mic),
                                                                   logger));
                        loraRequest.SetRegion(routerRegion);
                        loraRequest.SetStationEui(stationEui);
                        messageDispatcher.DispatchRequest(loraRequest);
                    }
                    catch (JsonException)
                    {
                        logger.LogError("Received unexpected 'updf' message: {Json}.", json);
                    }

                    break;

                case LnsMessageType.TransmitConfirmation:
                    LogReceivedMessage(logger, "dntxed", json, null);

                    break;

                case var messageType and (LnsMessageType.DownlinkMessage or LnsMessageType.RouterConfig):
                    throw new NotSupportedException($"'{messageType}' is not a valid message type for this endpoint and is only valid for 'downstream' messages.");

                case LnsMessageType.TimeSync:
                    var timeSyncData = JsonConvert.DeserializeObject<TimeSyncMessage>(json);
                    LogReceivedMessage(logger, "TimeSync", json, null);
                    timeSyncData.GpsTime = (ulong)DateTime.UtcNow.Subtract(GpsEpoch).TotalMilliseconds * 1000; // to microseconds
                    await socket.SendAsync(JsonConvert.SerializeObject(timeSyncData), cancellationToken).ConfigureAwait(false);

                    break;

                case var messageType and (LnsMessageType.ProprietaryDataFrame
                                          or LnsMessageType.MulticastSchedule
                                          or LnsMessageType.RunCommand
                                          or LnsMessageType.RemoteShell):
                    logger.LogWarning("'{MessageType}' ({MessageTypeBasicStationString}) is not handled in current LoRaWan Network Server implementation.", messageType, messageType.ToBasicStationString());

                    break;

                default:
                    throw new SwitchExpressionException();
            }
        }

        private async Task<T> ExecuteWithExceptionHandlingAsync<T>(Func<Task<T>> action)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An exception occurred while processing requests: {Exception}.", ex);
                throw;
            }
        }

        internal async Task CloseSocketAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            if (socket.State is WebSocketState.Open)
            {
                logger.LogDebug("Closing websocket.");
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, nameof(WebSocketCloseStatus.NormalClosure), cancellationToken).ConfigureAwait(false);
                logger.LogDebug("WebSocket connection closed");
            }
        }
    }
}
