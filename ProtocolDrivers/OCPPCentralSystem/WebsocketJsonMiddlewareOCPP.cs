/*
Copyright 2020 Cognizant
Copyright 2021 Microsoft Corporation
*/

namespace OCPPCentralSystem
{
    using Microsoft.AspNetCore.Http;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using OCPPCentralSystem.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Serilog;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class WebsocketJsonMiddlewareOCPP
    {
        public class QueuedMessage
        {
            public string Destination { get; set; }

            public string Payload { get; set; }
        }

        public static ConcurrentQueue<QueuedMessage> PendingMessages { get; } = new();

        private readonly RequestDelegate _next;

        private static ConcurrentDictionary<string, ChargePointConnection> _connectedChargePoints = new();

        public WebsocketJsonMiddlewareOCPP(RequestDelegate next)
        {
            _next = next;

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
                    string chargePointName = message.Destination;

                    try
                    {
                        if (!_connectedChargePoints.ContainsKey(chargePointName))
                        {
                            Log.Logger.Error($"Charge point {chargePointName} not found in the connected charge points list!");
                            PendingMessages.TryDequeue(out _);
                            continue;
                        }

                        if (_connectedChargePoints[chargePointName].WebSocket.State == WebSocketState.Open)
                        {
                            try
                            {
                                await _connectedChargePoints[chargePointName].WebSocket.SendAsync(
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
                            Log.Logger.Error($"WebSocket for charge point {chargePointName} is not open. Cannot send request.");
                        }

                        PendingMessages.TryDequeue(out _);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error($"Exception while processing command for charge point {chargePointName}: {ex.Message}");
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

        public static string ExecuteCommand(string chargePointName, string command, string[] inputArgs, string[] outputArgs)
        {
            if (!_connectedChargePoints.ContainsKey(chargePointName))
            {
                Log.Logger.Error($"Charge point {chargePointName} not found in the connected charge points list!");
                return null;
            }

            if (_connectedChargePoints[chargePointName].WebSocket.State != WebSocketState.Open)
            {
                Log.Logger.Error($"WebSocket for charge point {chargePointName} is not open. Cannot send request.");
                return null;
            }

            string subProtocol = _connectedChargePoints[chargePointName].WebSocket.SubProtocol;

            if (subProtocol == "ocpp1.6")
            {
                OCPP16Processor.SendCentralStationCommand(chargePointName, command, inputArgs);
            }

            if ((subProtocol == "ocpp2.0") || (subProtocol == "ocpp2.0.1") || (subProtocol == "ocpp2.1"))
            {
                OCPP21Processor.SendCentralStationCommand(chargePointName, command, inputArgs);
            }

            return string.Empty;
        }

        private async Task AddWebSocketAsync(HttpContext httpContext)
        {
            try
            {
                string chargepointName = httpContext.Request.Path.Value.TrimEnd('/').Split('/').LastOrDefault();

                IList<string> chargerProtocols = httpContext.WebSockets.WebSocketRequestedProtocols;
                if (chargerProtocols.Count == 0)
                {
                    Log.Logger.Error($"Invalid protocol list received!");
                    return;
                }

                WebSocket socket = null;
                foreach (string protocol in chargerProtocols)
                {
                    if ((protocol == "ocpp1.6") || (protocol == "ocpp2.0") || (protocol == "ocpp2.0.1") || (protocol == "ocpp2.1"))
                    {
                        socket = await httpContext.WebSockets.AcceptWebSocketAsync(protocol).ConfigureAwait(false);
                        break;
                    }
                }

                if (socket == null || socket.State != WebSocketState.Open)
                {
                    Log.Logger.Error("Accepting websocket failed!");
                    await _next(httpContext).ConfigureAwait(false);
                    return;
                }

                if (!_connectedChargePoints.ContainsKey(chargepointName))
                {
                    _connectedChargePoints.TryAdd(chargepointName, new ChargePointConnection(chargepointName, socket));
                }
                else
                {
                    try
                    {
                        var oldSocket = _connectedChargePoints[chargepointName].WebSocket;
                        _connectedChargePoints[chargepointName].WebSocket = socket;

                        if (oldSocket != null)
                        {
                            Log.Logger.Information($"New websocket request received for {chargepointName}");
                            if (oldSocket != socket && oldSocket.State != WebSocketState.Closed)
                            {
                                Log.Logger.Information($"Closing old websocket for {chargepointName}");

                                await oldSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client sent new websocket request", CancellationToken.None).ConfigureAwait(false);
                            }
                        }

                        Log.Logger.Information($"Websocket replaced successfully for {chargepointName}");
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error("Exception: " + ex.Message);
                    }
                }

                if (socket.State == WebSocketState.Open)
                {
                    await SendAndReceiveAsync(chargepointName, socket).ConfigureAwait(false);
                }
                else
                {
                    await RemoveWebSocketAsync(chargepointName, socket).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
            }
        }

        private async Task RemoveWebSocketAsync(string chargepointName, WebSocket webSocket)
        {
            try
            {
                if (_connectedChargePoints.TryRemove(chargepointName, out ChargePointConnection charger))
                {
                    Log.Logger.Information($"Removed charge points {chargepointName}");
                }
                else
                {
                    Log.Logger.Error($"Cannot remove charge points {chargepointName}");
                }

                // also remove from the OPC UA mapping
                if (OCPPCentralSystem.ChargePoints.ContainsKey(chargepointName))
                {
                    OCPPCentralSystem.ChargePoints.TryRemove(chargepointName, out ChargePoint cp);
                }

                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client requested closed", CancellationToken.None).ConfigureAwait(false);
                Log.Logger.Information($"Closed websocket for charge point {chargepointName}. Remaining active charge points : {_connectedChargePoints.Count}");
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
            }
        }

        private async Task SendAndReceiveAsync(string chargepointName, WebSocket webSocket)
        {
            while (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    string payloadString = await ReceiveDataFromWebSocketAsync(webSocket, chargepointName).ConfigureAwait(false);
                    if (payloadString != null)
                    {
                        // switching based on messageTypeId
                        JArray ocppMessage = JArray.Parse(payloadString);
                        switch ((int)ocppMessage[0])
                        {
                            case 2: // request

                                if (ocppMessage.Count < 4)
                                {
                                    Log.Logger.Error($"Invalid request payload received from charge point {chargepointName}. Payload: {payloadString}");
                                    continue;
                                }

                                if (webSocket.SubProtocol == "ocpp1.6")
                                {
                                    string response = await OCPP16Processor.ProcessRequestPayloadAsync(chargepointName, (string)ocppMessage[1], (string)ocppMessage[2], JsonConvert.SerializeObject(ocppMessage[3])).ConfigureAwait(false);
                                    PendingMessages.Enqueue(new QueuedMessage { Destination = chargepointName, Payload = response });
                                }

                                if ((webSocket.SubProtocol == "ocpp2.0") || (webSocket.SubProtocol == "ocpp2.0.1") || (webSocket.SubProtocol == "ocpp2.1"))
                                {
                                    string response = await OCPP21Processor.ProcessRequestPayloadAsync(chargepointName, (string)ocppMessage[1], (string)ocppMessage[2], JsonConvert.SerializeObject(ocppMessage[3])).ConfigureAwait(false);
                                    PendingMessages.Enqueue(new QueuedMessage { Destination = chargepointName, Payload = response });
                                }

                                break;

                            case 3: // response

                                if (ocppMessage.Count < 3)
                                {
                                    Log.Logger.Error($"Invalid response payload received from charge points {chargepointName}. Payload: {payloadString}");
                                }

                                if (webSocket.SubProtocol == "ocpp1.6")
                                {
                                    await OCPP16Processor.ProcessResponsePayloadAsync(chargepointName, (string)ocppMessage[1], JsonConvert.SerializeObject(ocppMessage[3])).ConfigureAwait(false);
                                }

                                if ((webSocket.SubProtocol == "ocpp2.0") || (webSocket.SubProtocol == "ocpp2.0.1") || (webSocket.SubProtocol == "ocpp2.1"))
                                {
                                    await OCPP21Processor.ProcessResponsePayloadAsync(chargepointName, (string)ocppMessage[1], JsonConvert.SerializeObject(ocppMessage[3])).ConfigureAwait(false);
                                }

                                break;

                            case 4: // error

                                if (webSocket.SubProtocol == "ocpp1.6")
                                {
                                    await OCPP16Processor.ProcessErrorPayloadAsync(chargepointName, payloadString).ConfigureAwait(false);
                                }

                                if ((webSocket.SubProtocol == "ocpp2.0") || (webSocket.SubProtocol == "ocpp2.0.1") || (webSocket.SubProtocol == "ocpp2.1"))
                                {
                                    await OCPP21Processor.ProcessErrorPayloadAsync(chargepointName, payloadString).ConfigureAwait(false);
                                }

                                break;

                            default:

                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("Exception: " + ex.Message);
                }
            }
        }

        private async Task<string> ReceiveDataFromWebSocketAsync(WebSocket webSocket, string chargepointName)
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
                        if (webSocket != _connectedChargePoints[chargepointName].WebSocket)
                        {
                            if (webSocket.State != WebSocketState.CloseReceived)
                            {
                                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "New websocket request received for this charge point", CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            await RemoveWebSocketAsync(chargepointName, webSocket).ConfigureAwait(false);
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
                if (webSocket != _connectedChargePoints[chargepointName].WebSocket)
                {
                    Log.Logger.Error($"WebsocketException occured in the old socket while receiving payload from charge point {chargepointName}. Error : {websocex.Message}");
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
