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
    using Serilog;
    using System;
    using System.Collections;
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
        private readonly RequestDelegate _next;
        private ConcurrentDictionary<string, ChargePointConnection> connectedChargePoints = new ConcurrentDictionary<string, ChargePointConnection>();

        public WebsocketJsonMiddlewareOCPP(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            try
            {
                if (httpContext.WebSockets.IsWebSocketRequest)
                {
                    await HandleWebsockets(httpContext);
                    return;
                }

                // passed on to next middleware
                await _next(httpContext);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);

                httpContext.Response.StatusCode=StatusCodes.Status500InternalServerError;
                await httpContext.Response.WriteAsync("Something went wrong!!. Please check with the Central system admin");
            }
        }

        private async Task HandleWebsockets(HttpContext httpContext)
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
                    if (protocol == "ocpp1.6")
                    {
                        socket = await httpContext.WebSockets.AcceptWebSocketAsync(protocol).ConfigureAwait(false);
                        break;
                    }

                    if ((protocol == "ocpp2.0") || (protocol == "ocpp2.1"))
                    {
                        socket = await httpContext.WebSockets.AcceptWebSocketAsync(protocol).ConfigureAwait(false);
                        break;
                    }
                }

                if (socket == null || socket.State != WebSocketState.Open)
                {
                    Log.Logger.Error("Accepting websocket failed!");
                    await _next(httpContext);
                    return;
                }

                if (!connectedChargePoints.ContainsKey(chargepointName))
                {
                    connectedChargePoints.TryAdd(chargepointName, new ChargePointConnection(chargepointName, socket));
                    Log.Logger.Information($"No. of active chargers : {connectedChargePoints.Count}");
                }
                else
                {
                    try
                    {
                        var oldSocket = connectedChargePoints[chargepointName].WebSocket;
                        connectedChargePoints[chargepointName].WebSocket = socket;

                        if (oldSocket != null)
                        {
                            Log.Logger.Information($"New websocket request received for {chargepointName}");
                            if (oldSocket != socket && oldSocket.State != WebSocketState.Closed)
                            {
                                Log.Logger.Information($"Closing old websocket for {chargepointName}");

                                await oldSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client sent new websocket request", CancellationToken.None);
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
                    await HandleActiveConnection(socket, chargepointName);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
            }
        }

        private async Task HandleActiveConnection(WebSocket webSocket, string chargepointName)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await HandlePayloadsAsync(chargepointName, webSocket);
                }

                if (webSocket.State != WebSocketState.Open && connectedChargePoints.ContainsKey(chargepointName) && connectedChargePoints[chargepointName].WebSocket == webSocket)
                {
                    await RemoveConnectionsAsync(chargepointName, webSocket);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
            }
        }

        private async Task HandlePayloadsAsync(string chargepointName, WebSocket webSocket)
        {
            while (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    string payloadString = await ReceiveDataFromChargerAsync(webSocket, chargepointName).ConfigureAwait(false);
                    if (payloadString != null)
                    {
                        // switching based on messageTypeId
                        JArray ocppMessage = JArray.Parse(payloadString);
                        switch ((int)ocppMessage[0])
                        {
                            case 2:
                                if (ocppMessage.Count < 4)
                                {
                                    Log.Logger.Error($"Invalid request payload received from charger {chargepointName}. Payload: {payloadString}");
                                    continue;
                                }

                                if (webSocket.SubProtocol == "ocpp1.6")
                                {

                                    string response = await OCPP16Processor.ProcessRequestPayloadAsync((string)ocppMessage[1], (string)ocppMessage[2], JsonConvert.SerializeObject(ocppMessage[3])).ConfigureAwait(false);

                                    await SendPayloadToChargerAsync(chargepointName, response, webSocket);
                                }

                                if (webSocket.SubProtocol == "ocpp2.1")
                                {
                                    string response = await OCPP21Processor.ProcessRequestPayloadAsync((string)ocppMessage[1], (string)ocppMessage[2], JsonConvert.SerializeObject(ocppMessage[3])).ConfigureAwait(false);
                                    await SendPayloadToChargerAsync(chargepointName, response, webSocket);
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

        private async Task RemoveConnectionsAsync(string chargepointName, WebSocket webSocket)
        {
            try
            {
                if (connectedChargePoints.TryRemove(chargepointName, out ChargePointConnection charger))
                {
                    Log.Logger.Information($"Removed charger {chargepointName}");
                }
                else
                {
                    Log.Logger.Error($"Cannot remove charger {chargepointName}");
                }

                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client requested closed", CancellationToken.None);
                Log.Logger.Information($"Closed websocket for charger {chargepointName}. Remaining active chargers : {connectedChargePoints.Count}");

            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
            }
        }

        private async Task<string> ReceiveDataFromChargerAsync(WebSocket webSocket, string chargepointName)
        {
            try
            {
                ArraySegment<byte> data = new ArraySegment<byte>(new byte[1024]);
                WebSocketReceiveResult result;
                string payloadString = string.Empty;

                do
                {
                    result = await webSocket.ReceiveAsync(data, CancellationToken.None);

                    // charger sent close frame
                    if (result.CloseStatus.HasValue)
                    {
                        if (webSocket != connectedChargePoints[chargepointName].WebSocket)
                        {
                            if (webSocket.State != WebSocketState.CloseReceived)
                            {
                                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "New websocket request received for this charger", CancellationToken.None);
                            }
                        }
                        else
                        {
                            await RemoveConnectionsAsync(chargepointName, webSocket);
                        }

                        return null;
                    }

                    // appending received data
                    payloadString += Encoding.UTF8.GetString(data.Array, 0, result.Count);

                } while (!result.EndOfMessage);

                return payloadString;
            }
            catch (WebSocketException websocex)
            {
                if (webSocket != connectedChargePoints[chargepointName].WebSocket)
                {
                    Log.Logger.Error($"WebsocketException occured in the old socket while receiving payload from charger {chargepointName}. Error : {websocex.Message}");
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

        private async Task SendPayloadToChargerAsync(string chargepointName, string payload, WebSocket webSocket)
        {
            var charger = connectedChargePoints[chargepointName];

            try
            {
                charger.WebsocketBusy = true;

                ArraySegment<byte> data = Encoding.UTF8.GetBytes(payload);

                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.SendAsync(data, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
            }

            charger.WebsocketBusy = false;
        }
    }
}
