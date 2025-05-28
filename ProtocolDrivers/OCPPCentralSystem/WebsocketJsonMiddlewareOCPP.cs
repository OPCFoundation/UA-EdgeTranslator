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
        private readonly RequestDelegate _next;

        private ConcurrentDictionary<string, ChargePointConnection> _connectedChargePoints = new();

        public static ConcurrentDictionary<string, string> Requests { get; set; } = new(); // used for Central system initiated commands

        public WebsocketJsonMiddlewareOCPP(RequestDelegate next)
        {
            _next = next;

            _ = Task.Run(ProcessCommandsAsync);
        }

        private async Task ProcessCommandsAsync()
        {
            while (true)
            {
                // process commands once a second
                await Task.Delay(1000).ConfigureAwait(false);

                // handle commands initiated by the central system
                while (Requests.Count > 0)
                {
                    try
                    {
                        if (!_connectedChargePoints.ContainsKey(Requests.FirstOrDefault().Key))
                        {
                            Log.Logger.Error($"Charge point {Requests.FirstOrDefault().Key} not found in the connected charge points list!");
                            Requests.TryRemove(Requests.FirstOrDefault().Key, out _);
                            continue;
                        }

                        string chargePointName = Requests.FirstOrDefault().Key;
                        if (!_connectedChargePoints[chargePointName].WaitingResponse && !_connectedChargePoints[chargePointName].WebsocketBusy)
                        {
                            if (_connectedChargePoints[chargePointName].WebSocket.State == WebSocketState.Open)
                            {
                                string requestPayload = Requests.FirstOrDefault().Value;

                                await SendDataToWebSocketAsync(chargePointName, requestPayload, _connectedChargePoints[chargePointName].WebSocket).ConfigureAwait(false);

                                _connectedChargePoints[chargePointName].WaitingResponse = true; // set the flag to true to indicate that a request is sent and waiting for response
                            }
                            else
                            {
                                Log.Logger.Error($"WebSocket for charge point {chargePointName} is not open. Cannot send request.");
                            }

                            Requests.TryRemove(chargePointName, out _);
                        }
                        else
                        {
                            Log.Logger.Warning($"Charge point {chargePointName} is busy or waiting for response. Skipping command processing.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error($"Exception while processing command for charge point {Requests.FirstOrDefault().Key}: {ex.Message}");
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

                                    await SendDataToWebSocketAsync(chargepointName, response, webSocket).ConfigureAwait(false);
                                }

                                if ((webSocket.SubProtocol == "ocpp2.0") || (webSocket.SubProtocol == "ocpp2.1"))
                                {
                                    string response = await OCPP21Processor.ProcessRequestPayloadAsync(chargepointName, (string)ocppMessage[1], (string)ocppMessage[2], JsonConvert.SerializeObject(ocppMessage[3])).ConfigureAwait(false);

                                    await SendDataToWebSocketAsync(chargepointName, response, webSocket).ConfigureAwait(false);
                                }

                                break;

                            case 3: // response

                                // remove the waiting response flag
                                if (_connectedChargePoints.TryGetValue(chargepointName, out ChargePointConnection charger))
                                {
                                    charger.WaitingResponse = false;
                                }

                                if (ocppMessage.Count < 3)
                                {
                                    Log.Logger.Error($"Invalid response payload received from charge points {chargepointName}. Payload: {payloadString}");
                                }

                                if (webSocket.SubProtocol == "ocpp1.6")
                                {
                                    await OCPP16Processor.ProcessResponsePayloadAsync(chargepointName, (string)ocppMessage[1], JsonConvert.SerializeObject(ocppMessage[3])).ConfigureAwait(false);
                                }

                                if ((webSocket.SubProtocol == "ocpp2.0") || (webSocket.SubProtocol == "ocpp2.1"))
                                {
                                    await OCPP21Processor.ProcessResponsePayloadAsync(chargepointName, (string)ocppMessage[1], JsonConvert.SerializeObject(ocppMessage[3])).ConfigureAwait(false);
                                }

                                break;

                            case 4: // error

                                // remove the waiting response flag
                                if (_connectedChargePoints.TryGetValue(chargepointName, out ChargePointConnection charger2))
                                {
                                    charger2.WaitingResponse = false;
                                }

                                if (webSocket.SubProtocol == "ocpp1.6")
                                {
                                    await OCPP16Processor.ProcessErrorPayloadAsync(chargepointName, payloadString).ConfigureAwait(false);
                                }

                                if ((webSocket.SubProtocol == "ocpp2.0") || (webSocket.SubProtocol == "ocpp2.1"))
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

        private async Task SendDataToWebSocketAsync(string chargepointName, string payload, WebSocket webSocket)
        {
            var charger = _connectedChargePoints[chargepointName];

            try
            {
                charger.WebsocketBusy = true;

                ArraySegment<byte> data = Encoding.UTF8.GetBytes(payload);

                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.SendAsync(data, WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
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
