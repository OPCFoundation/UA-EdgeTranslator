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

        private static ConcurrentDictionary<string, ChargePointConnection> connectedChargePoints = new ConcurrentDictionary<string, ChargePointConnection>();
        private int _transactionNumber = 0;

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

                var chargerProtocols = httpContext.WebSockets.WebSocketRequestedProtocols;
                if ((chargerProtocols.Count == 0) || !chargerProtocols.Contains(StringConstants.RequiredProtocol))
                {
                    Log.Logger.Error($"Chargepoint does not support OCPP 1.6 protocol!");
                    return;
                }

                var socket = await httpContext.WebSockets.AcceptWebSocketAsync(StringConstants.RequiredProtocol);
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

                                await oldSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, StringConstants.ClientInitiatedNewWebsocketMessage, CancellationToken.None);
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

                                string response = await ProcessRequestPayloadAsync((string)ocppMessage[1], (string)ocppMessage[2], JsonConvert.SerializeObject(ocppMessage[3])).ConfigureAwait(false);
                                
                                await SendPayloadToChargerAsync(chargepointName, response, webSocket);
                                
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

                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, StringConstants.ClientRequestedClosureMessage, CancellationToken.None);
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
                                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, StringConstants.ChargerNewWebRequestMessage, CancellationToken.None);
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

        private Task<string> ProcessRequestPayloadAsync(string uniqueId, string action, string payload)
        {
            string responsePayload = string.Empty;

            try
            {
                // switching based on OCPP action name
                switch (action)
                {
                    case "Authorize":
                        {
                            AuthorizeRequest authRequest = JsonConvert.DeserializeObject<AuthorizeRequest>(payload);

                            Log.Logger.Information("Authorization requested on chargepoint " + uniqueId + "  and badge ID " + authRequest.IdTag);

                            if (!OCPPCentralSystem.ChargePoints.ContainsKey(uniqueId))
                            {
                                OCPPCentralSystem.ChargePoints.TryAdd(uniqueId, new ChargePoint());
                                OCPPCentralSystem.ChargePoints[uniqueId].ID = uniqueId;
                            }

                            // always authorize any badge
                            IdTagInfo info = new IdTagInfo
                            {
                                ExpiryDateSpecified = false,
                                Status = AuthorizationStatus.Accepted
                            };

                            responsePayload = JsonConvert.SerializeObject(new AuthorizeResponse()
                            {
                                IdTagInfo = info
                            });

                            break;
                        }
                    case "BootNotification":
                        {
                            BootNotificationRequest bootNotificationRequest = JsonConvert.DeserializeObject<BootNotificationRequest>(payload);

                            Log.Logger.Information("Charge point with identity: " + uniqueId + " booted!");

                            if (!OCPPCentralSystem.ChargePoints.ContainsKey(uniqueId))
                            {
                                OCPPCentralSystem.ChargePoints.TryAdd(uniqueId, new ChargePoint());
                                OCPPCentralSystem.ChargePoints[uniqueId].ID = uniqueId;
                            }

                            responsePayload = JsonConvert.SerializeObject(new BootNotificationResponse()
                            {
                                Status = RegistrationStatus.Accepted,
                                CurrentTime = DateTime.UtcNow,
                                Interval = 60
                            });

                            break;
                        }
                    case "Heartbeat":
                        {
                            HeartbeatRequest heartbeatRequest = JsonConvert.DeserializeObject<HeartbeatRequest>(payload);

                            Log.Logger.Information("Heartbeat received from: " + uniqueId);

                            if (!OCPPCentralSystem.ChargePoints.ContainsKey(uniqueId))
                            {
                                OCPPCentralSystem.ChargePoints.TryAdd(uniqueId, new ChargePoint());
                                OCPPCentralSystem.ChargePoints[uniqueId].ID = uniqueId;
                            }

                            responsePayload = JsonConvert.SerializeObject(new HeartbeatResponse() {
                                CurrentTime = DateTime.UtcNow
                            });

                            break;
                        }
                    case "MeterValues":
                        {
                            MeterValuesRequest meterValuesRequest = JsonConvert.DeserializeObject<MeterValuesRequest>(payload);

                            Log.Logger.Information("Meter values for connector ID " + meterValuesRequest.ConnectorId + " on chargepoint " + uniqueId + ":");

                            if (!OCPPCentralSystem.ChargePoints.ContainsKey(uniqueId))
                            {
                                OCPPCentralSystem.ChargePoints.TryAdd(uniqueId, new ChargePoint());
                                OCPPCentralSystem.ChargePoints[uniqueId].ID = uniqueId;
                            }

                            if (!OCPPCentralSystem.ChargePoints[uniqueId].Connectors.ContainsKey(meterValuesRequest.ConnectorId))
                            {
                                OCPPCentralSystem.ChargePoints[uniqueId].Connectors.TryAdd(meterValuesRequest.ConnectorId, new Connector(meterValuesRequest.ConnectorId));
                            }

                            foreach (MeterValue meterValue in meterValuesRequest.MeterValue)
                            {
                                foreach (SampledValue sampledValue in meterValue.SampledValue)
                                {
                                    Log.Logger.Information("Value: " + sampledValue.Value + " " + sampledValue.Unit.ToString());
                                    
                                    if (int.TryParse(sampledValue.Value, out int parsedInt))
                                    {
                                        MeterReading reading = new MeterReading();
                                        reading.MeterValue = parsedInt;

                                        if (sampledValue.UnitSpecified)
                                        {
                                            reading.MeterValueUnit = sampledValue.Unit.ToString();
                                        }

                                        reading.Timestamp = meterValue.Timestamp;
                                        OCPPCentralSystem.ChargePoints[uniqueId].Connectors[meterValuesRequest.ConnectorId].MeterReadings.Add(reading);
                                        
                                        if (OCPPCentralSystem.ChargePoints[uniqueId].Connectors[meterValuesRequest.ConnectorId].MeterReadings.Count > 10)
                                        {
                                            OCPPCentralSystem.ChargePoints[uniqueId].Connectors[meterValuesRequest.ConnectorId].MeterReadings.RemoveAt(0);
                                        }
                                    }
                                }
                            }

                            responsePayload = JsonConvert.SerializeObject(new MeterValuesResponse());

                            break;
                        }
                    case "StartTransaction":
                        {
                            StartTransactionRequest startTransactionRequest = JsonConvert.DeserializeObject<StartTransactionRequest>(payload);

                            Log.Logger.Information("Start transaction " + _transactionNumber.ToString() + " from " + startTransactionRequest.Timestamp + " on chargepoint " + uniqueId + " on connector " + startTransactionRequest.ConnectorId + " with badge ID " + startTransactionRequest.IdTag + " and meter reading at start " + startTransactionRequest.MeterStart);

                            if (!OCPPCentralSystem.ChargePoints.ContainsKey(uniqueId))
                            {
                                OCPPCentralSystem.ChargePoints.TryAdd(uniqueId, new ChargePoint());
                                OCPPCentralSystem.ChargePoints[uniqueId].ID = uniqueId;
                            }

                            if (!OCPPCentralSystem.ChargePoints[uniqueId].Connectors.ContainsKey(startTransactionRequest.ConnectorId))
                            {
                                OCPPCentralSystem.ChargePoints[uniqueId].Connectors.TryAdd(startTransactionRequest.ConnectorId, new Connector(startTransactionRequest.ConnectorId));
                            }

                            _transactionNumber++;

                            Transaction transaction = new Transaction(_transactionNumber)
                            {
                                BadgeID = startTransactionRequest.IdTag,
                                StartTime = startTransactionRequest.Timestamp,
                                MeterValueStart = startTransactionRequest.MeterStart
                            };

                            if (!OCPPCentralSystem.ChargePoints[uniqueId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.ContainsKey(_transactionNumber))
                            {
                                OCPPCentralSystem.ChargePoints[uniqueId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.TryAdd(_transactionNumber, transaction);
                            }

                            // housekeeping: Remove transactions that are older than 1 day
                            KeyValuePair<int, Transaction>[] transactionsArray = OCPPCentralSystem.ChargePoints[uniqueId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.ToArray();
                            for (int i = 0; i < transactionsArray.Length; i++)
                            {
                                if ((transactionsArray[i].Value.StopTime != DateTime.MinValue) && (transactionsArray[i].Value.StopTime < DateTime.UtcNow.Subtract(TimeSpan.FromDays(1))))
                                {
                                    OCPPCentralSystem.ChargePoints[uniqueId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.TryRemove(transactionsArray[i].Key, out _);
                                }
                            }

                            IdTagInfo info = new IdTagInfo
                            {
                                ExpiryDateSpecified = false,
                                Status = AuthorizationStatus.Accepted
                            };

                            responsePayload = JsonConvert.SerializeObject(new StartTransactionResponse()
                            {
                                TransactionId = _transactionNumber,
                                IdTagInfo = info
                            });

                            break;
                        }
                    case "StopTransaction":
                        {
                            StopTransactionRequest stopTransactionRequest = JsonConvert.DeserializeObject<StopTransactionRequest>(payload);

                            Log.Logger.Information("Stop transaction " + stopTransactionRequest.TransactionId.ToString() + " from " + stopTransactionRequest.Timestamp + " on chargepoint " + uniqueId + " with badge ID " + stopTransactionRequest.IdTag + " and meter reading at stop " + stopTransactionRequest.MeterStop);

                            if (!OCPPCentralSystem.ChargePoints.ContainsKey(uniqueId))
                            {
                                OCPPCentralSystem.ChargePoints.TryAdd(uniqueId, new ChargePoint());
                                OCPPCentralSystem.ChargePoints[uniqueId].ID = uniqueId;
                            }

                            // find the transaction
                            KeyValuePair<int, Connector>[] connectorArray = OCPPCentralSystem.ChargePoints[uniqueId].Connectors.ToArray();
                            for (int i = 0; i < connectorArray.Length; i++)
                            {
                                if (OCPPCentralSystem.ChargePoints[uniqueId].Connectors[connectorArray[i].Key].CurrentTransactions.ContainsKey(stopTransactionRequest.TransactionId))
                                {
                                    OCPPCentralSystem.ChargePoints[uniqueId].Connectors[connectorArray[i].Key].CurrentTransactions[stopTransactionRequest.TransactionId].MeterValueFinish = stopTransactionRequest.MeterStop;
                                    OCPPCentralSystem.ChargePoints[uniqueId].Connectors[connectorArray[i].Key].CurrentTransactions[stopTransactionRequest.TransactionId].StopTime = stopTransactionRequest.Timestamp;
                                    break;
                                }
                            }

                            IdTagInfo info = new IdTagInfo
                            {
                                ExpiryDateSpecified = false,
                                Status = AuthorizationStatus.Accepted
                            };

                            responsePayload = JsonConvert.SerializeObject(new StopTransactionResponse() {
                                IdTagInfo = info
                            });

                            break;
                        }
                    case "StatusNotification":
                        {
                            StatusNotificationRequest statusNotificationRequest = JsonConvert.DeserializeObject<StatusNotificationRequest>(payload);

                            Log.Logger.Information("Chargepoint " + uniqueId + " and connector " + statusNotificationRequest.ConnectorId + " status#: " + statusNotificationRequest.Status.ToString());

                            if (!OCPPCentralSystem.ChargePoints.ContainsKey(uniqueId))
                            {
                                OCPPCentralSystem.ChargePoints.TryAdd(uniqueId, new ChargePoint());
                                OCPPCentralSystem.ChargePoints[uniqueId].ID = uniqueId;
                            }

                            if (!OCPPCentralSystem.ChargePoints[uniqueId].Connectors.ContainsKey(statusNotificationRequest.ConnectorId))
                            {
                                OCPPCentralSystem.ChargePoints[uniqueId].Connectors.TryAdd(statusNotificationRequest.ConnectorId, new Connector(statusNotificationRequest.ConnectorId));
                            }

                            OCPPCentralSystem.ChargePoints[uniqueId].ID = uniqueId;
                            OCPPCentralSystem.ChargePoints[uniqueId].Connectors[statusNotificationRequest.ConnectorId].Status = statusNotificationRequest.Status.ToString();

                            responsePayload = JsonConvert.SerializeObject(new StatusNotificationResponse());

                            break;
                        }
                    case "DataTransfer":
                        {
                            responsePayload = JsonConvert.SerializeObject(new DataTransferResponse()
                            {
                                Status = DataTransferStatus.Rejected
                            });

                            break;
                        }
                    case "DiagnosticsStatusNotification":
                        {
                            responsePayload = JsonConvert.SerializeObject(new DiagnosticsStatusNotificationResponse());

                            break;
                        }
                    case "FirmwareStatusNotification":
                        {
                            responsePayload = JsonConvert.SerializeObject(new FirmwareStatusNotificationResponse());

                            break;
                        }
                    default:
                        {
                            break;
                        }
                }

                return Task.FromResult(JsonConvert.SerializeObject(new JArray
                {
                    3, // messageTypeId for response
                    uniqueId, // uniqueId from request
                    JObject.Parse(responsePayload) // payload
                }));
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);

                return Task.FromResult(JsonConvert.SerializeObject(new JArray
                {
                    4, // messageTypeId for error response
                    uniqueId, // uniqueId from request
                    "500", // error code
                    ex.Message, // error description
                    string.Empty // empty payload
                }));
            }
        }
    }
}
