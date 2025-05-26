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

        private static ConcurrentDictionary<string, ChargePointConnection> activeCharger = new ConcurrentDictionary<string, ChargePointConnection>();
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
                Log.Logger.Error(ex.StackTrace);

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

                if (!activeCharger.ContainsKey(chargepointName))
                {
                    activeCharger.TryAdd(chargepointName, new ChargePointConnection(chargepointName, socket));
                    Log.Logger.Information($"No. of active chargers : {activeCharger.Count}");
                }
                else
                {
                    try
                    {
                        var oldSocket = activeCharger[chargepointName].WebSocket;
                        activeCharger[chargepointName].WebSocket = socket;

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
                        Log.Logger.Error(ex.StackTrace);
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
                Log.Logger.Error(ex.StackTrace);
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

                if (webSocket.State != WebSocketState.Open && activeCharger.ContainsKey(chargepointName) && activeCharger[chargepointName].WebSocket == webSocket)
                {
                    await RemoveConnectionsAsync(chargepointName, webSocket);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
                Log.Logger.Error(ex.StackTrace);
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

                    //When the charger sends close frame
                    if (result.CloseStatus.HasValue)
                    {
                        if (webSocket != activeCharger[chargepointName].WebSocket)
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

                    //Appending received data
                    payloadString += Encoding.UTF8.GetString(data.Array, 0, result.Count);

                } while (!result.EndOfMessage);

                return payloadString;
            }
            catch (WebSocketException websocex)
            {
                if (webSocket != activeCharger[chargepointName].WebSocket)
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
                Log.Logger.Error(ex.StackTrace);
            }

            return null;
        }

        private async Task SendPayloadToChargerAsync(string chargepointName, object payload, WebSocket webSocket)
        {
            var charger = activeCharger[chargepointName];

            try
            {
                charger.WebsocketBusy = true;

                var settings = new JsonSerializerSettings { DateFormatString = StringConstants.DateTimeFormat, NullValueHandling = NullValueHandling.Ignore };
                var serializedPayload = JsonConvert.SerializeObject(payload, settings);

                ArraySegment<byte> data = Encoding.UTF8.GetBytes(serializedPayload);

                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.SendAsync(data, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
                Log.Logger.Error(ex.StackTrace);
            }

            charger.WebsocketBusy = false;
        }

        private JArray ProcessPayload(string payloadString, string chargepointName)
        {
            try
            {
                if (payloadString != null)
                {
                    var basePayload = JsonConvert.DeserializeObject<JArray>(payloadString);
                    return basePayload;
                }
                else
                {
                    Log.Logger.Error($"Null payload received for {chargepointName}");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
                Log.Logger.Error(ex.StackTrace);
            }

            return null;
        }

        private Task<JArray> ProcessRequestPayloadAsync(string chargepointName, RequestPayload requestPayload)
        {
            string action = string.Empty;

            try
            {
                action = requestPayload.Action;

                object responsePayload = null;
                string url = string.Empty;

                //switching based on OCPP action name
                switch (action)
                {
                    case "Authorize":
                    {
                        AuthorizeRequest request = requestPayload.Payload.ToObject<AuthorizeRequest>();

                        Log.Logger.Information("Authorization requested on chargepoint " + request.ChargeBoxIdentity + "  and badge ID " + request.IdTag);

                        // always authorize any badge for now
                        IdTagInfo info = new IdTagInfo
                        {
                            ExpiryDateSpecified = false,
                            Status = AuthorizationStatus.Accepted
                        };

                        AuthorizeResponse response = new AuthorizeResponse() { IdTagInfo = info };
                        responsePayload = new ResponsePayload(requestPayload.UniqueId, response);
                        break;
                    }
                    case "BootNotification":
                    {
                        BootNotificationRequest request = requestPayload.Payload.ToObject<BootNotificationRequest>();

                        Log.Logger.Information("Charge point with identity: " + request.ChargeBoxIdentity + " booted!");

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(request.ChargeBoxIdentity))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(request.ChargeBoxIdentity, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].ID = request.ChargeBoxIdentity;
                        }

                        BootNotificationResponse response = new BootNotificationResponse() {
                            Status = RegistrationStatus.Accepted,
                            CurrentTime = DateTime.UtcNow,
                            Interval = 60
                        };

                        responsePayload = new ResponsePayload(requestPayload.UniqueId, response);
                        break;
                    }
                    case "Heartbeat":
                    {
                        HeartbeatRequest request = requestPayload.Payload.ToObject<HeartbeatRequest>();

                        Log.Logger.Information("Heartbeat received from: " + request.ChargeBoxIdentity);

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(request.ChargeBoxIdentity))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(request.ChargeBoxIdentity, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].ID = request.ChargeBoxIdentity;
                        }

                        HeartbeatResponse response = new HeartbeatResponse() { CurrentTime = DateTime.UtcNow };
                        responsePayload = new ResponsePayload(requestPayload.UniqueId, response);
                        break;
                    }
                    case "MeterValues":
                    {
                        MeterValuesRequest request = requestPayload.Payload.ToObject<MeterValuesRequest>();

                        Log.Logger.Information("Meter values for connector ID " + request.ConnectorId + " on chargepoint " + request.ChargeBoxIdentity + ":");

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(request.ChargeBoxIdentity))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(request.ChargeBoxIdentity, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].ID = request.ChargeBoxIdentity;
                        }

                        if (!OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors.ContainsKey(request.ConnectorId))
                        {
                            OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors.TryAdd(request.ConnectorId, new Connector(request.ConnectorId));
                        }

                        foreach (MeterValue meterValue in request.MeterValue)
                        {
                            foreach (SampledValue sampledValue in meterValue.SampledValue)
                            {
                                Log.Logger.Information("Value: " + sampledValue.Value + " " + sampledValue.Unit.ToString());
                                int parsedInt = 0;
                                if (int.TryParse(sampledValue.Value, out parsedInt))
                                {
                                    MeterReading reading = new MeterReading();
                                    reading.MeterValue = parsedInt;
                                    if (sampledValue.UnitSpecified)
                                    {
                                        reading.MeterValueUnit = sampledValue.Unit.ToString();
                                    }
                                    reading.Timestamp = meterValue.Timestamp;
                                    OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[request.ConnectorId].MeterReadings.Add(reading);
                                    if (OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[request.ConnectorId].MeterReadings.Count > 10)
                                    {
                                        OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[request.ConnectorId].MeterReadings.RemoveAt(0);
                                    }
                                }
                            }
                        }

                        MeterValuesResponse response = new MeterValuesResponse();
                        responsePayload = new ResponsePayload(requestPayload.UniqueId, response);
                        break;
                    }
                    case "StartTransaction":
                    {
                        StartTransactionRequest request = requestPayload.Payload.ToObject<StartTransactionRequest>();

                        Log.Logger.Information("Start transaction " + _transactionNumber.ToString() + " from " + request.Timestamp + " on chargepoint " + request.ChargeBoxIdentity + " on connector " + request.ConnectorId + " with badge ID " + request.IdTag + " and meter reading at start " + request.MeterStart);

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(request.ChargeBoxIdentity))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(request.ChargeBoxIdentity, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].ID = request.ChargeBoxIdentity;
                        }

                        if (!OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors.ContainsKey(request.ConnectorId))
                        {
                            OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors.TryAdd(request.ConnectorId, new Connector(request.ConnectorId));
                        }
                        _transactionNumber++;
                        Transaction transaction = new Transaction(_transactionNumber)
                        {
                            BadgeID = request.IdTag,
                            StartTime = request.Timestamp,
                            MeterValueStart = request.MeterStart
                        };

                        if (!OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[request.ConnectorId].CurrentTransactions.ContainsKey(_transactionNumber))
                        {
                            OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[request.ConnectorId].CurrentTransactions.TryAdd(_transactionNumber, transaction);
                        }

                        KeyValuePair<int, Transaction>[] transactionsArray = OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[request.ConnectorId].CurrentTransactions.ToArray();
                        for (int i = 0; i < transactionsArray.Length; i++)
                        {
                            if ((transactionsArray[i].Value.StopTime != DateTime.MinValue) && (transactionsArray[i].Value.StopTime < DateTime.UtcNow.Subtract(TimeSpan.FromDays(1))))
                            {
                                OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[request.ConnectorId].CurrentTransactions.TryRemove(transactionsArray[i].Key, out _);
                            }
                        }

                        IdTagInfo info = new IdTagInfo
                        {
                            ExpiryDateSpecified = false,
                            Status = AuthorizationStatus.Accepted
                        };

                        StartTransactionResponse response = new StartTransactionResponse() {
                            TransactionId = _transactionNumber,
                            IdTagInfo = info
                        };

                        responsePayload = new ResponsePayload(requestPayload.UniqueId, response);
                        break;
                    }
                    case "StopTransaction":
                        {
                            StopTransactionRequest request = requestPayload.Payload.ToObject<StopTransactionRequest>();

                            Log.Logger.Information("Stop transaction " + request.TransactionId.ToString() + " from " + request.Timestamp + " on chargepoint " + request.ChargeBoxIdentity + " with badge ID " + request.IdTag + " and meter reading at stop " + request.MeterStop);

                            if (!OCPPCentralSystem.ChargePoints.ContainsKey(request.ChargeBoxIdentity))
                            {
                                OCPPCentralSystem.ChargePoints.TryAdd(request.ChargeBoxIdentity, new ChargePoint());
                                OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].ID = request.ChargeBoxIdentity;
                            }

                            // find the transaction
                            KeyValuePair<int, Connector>[] connectorArray = OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors.ToArray();
                            for (int i = 0; i < connectorArray.Length; i++)
                            {
                                if (OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[connectorArray[i].Key].CurrentTransactions.ContainsKey(request.TransactionId))
                                {
                                    OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[connectorArray[i].Key].CurrentTransactions[request.TransactionId].MeterValueFinish = request.MeterStop;
                                    OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[connectorArray[i].Key].CurrentTransactions[request.TransactionId].StopTime = request.Timestamp;
                                    break;
                                }
                            }

                            IdTagInfo info = new IdTagInfo
                            {
                                ExpiryDateSpecified = false,
                                Status = AuthorizationStatus.Accepted
                            };

                        StopTransactionResponse response = new StopTransactionResponse() { IdTagInfo = info };
                        responsePayload = new ResponsePayload(requestPayload.UniqueId, response);
                        break;
                    }
                    case "StatusNotification":
                    {
                        StatusNotificationRequest request = requestPayload.Payload.ToObject<StatusNotificationRequest>();

                        Log.Logger.Information("Chargepoint " + request.ChargeBoxIdentity + " and connector " + request.ConnectorId + " status#: " + request.Status.ToString());

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(request.ChargeBoxIdentity))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(request.ChargeBoxIdentity, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].ID = request.ChargeBoxIdentity;
                        }

                        if (!OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors.ContainsKey(request.ConnectorId))
                        {
                            OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors.TryAdd(request.ConnectorId, new Connector(request.ConnectorId));
                        }

                        OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].ID = request.ChargeBoxIdentity;
                        OCPPCentralSystem.ChargePoints[request.ChargeBoxIdentity].Connectors[request.ConnectorId].Status = request.Status.ToString();

                        StatusNotificationResponse response = new StatusNotificationResponse();
                        responsePayload = new ResponsePayload(requestPayload.UniqueId, response);
                        break;
                    }
                    case "DataTransfer":
                    {
                        DataTransferResponse response = new DataTransferResponse() {
                            Status = DataTransferStatus.Rejected
                        };

                        responsePayload = new ResponsePayload(requestPayload.UniqueId, response);
                        break;
                    }
                    case "DiagnosticsStatusNotification":
                    {
                        DiagnosticsStatusNotificationResponse response = new DiagnosticsStatusNotificationResponse();
                        responsePayload = new ResponsePayload(requestPayload.UniqueId, response);
                        break;
                    }
                    case "FirmwareStatusNotification":
                    {
                        FirmwareStatusNotificationResponse response = new FirmwareStatusNotificationResponse();
                        responsePayload = new ResponsePayload(requestPayload.UniqueId, response);
                        break;
                    }
                    default:
                    {
                        responsePayload = new ErrorPayload(requestPayload.UniqueId, StringConstants.NotImplemented);
                        break;
                    }
                }

                if (responsePayload != null)
                {
                    if (((BasePayload)responsePayload).MessageTypeId == 3)
                    {
                        ResponsePayload response = (ResponsePayload)responsePayload;
                        return Task.FromResult(response.WrappedPayload);
                    }
                    else
                    {
                        ErrorPayload error = (ErrorPayload)responsePayload;
                        return Task.FromResult(error.WrappedPayload);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
                Log.Logger.Error(ex.StackTrace);
            }

            return null;
        }

        private async Task ProcessResponsePayloadAsync(string chargepointName, ResponsePayload responsePayload)
        {
            //Placeholder to process response payloads from charger for CentralSystem initiated commands
            await Task.Delay(1000);
        }

        private async Task ProcessErrorPayloadAsync(string chargepointName, ErrorPayload errorPayload)
        {
            //Placeholder to process error payloads from charger for CentralSystem initiated commands
            await Task.Delay(1000);
        }

        private async Task RemoveConnectionsAsync(string chargepointName, WebSocket webSocket)
        {
            try
            {
                if (activeCharger.TryRemove(chargepointName, out ChargePointConnection charger))
                {
                    Log.Logger.Information($"Removed charger {chargepointName}");
                }
                else
                {
                    Log.Logger.Error($"Cannot remove charger {chargepointName}");
                }

                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, StringConstants.ClientRequestedClosureMessage, CancellationToken.None);
                Log.Logger.Information($"Closed websocket for charger {chargepointName}. Remaining active chargers : {activeCharger.Count}");

            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
                Log.Logger.Error(ex.StackTrace);
            }
        }

        private async Task HandlePayloadsAsync(string chargepointName, WebSocket webSocket)
        {
            while (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    string payloadString = await ReceiveDataFromChargerAsync(webSocket, chargepointName);
                    var payload = ProcessPayload(payloadString, chargepointName);

                    if (payload != null)
                    {
                        JArray response = null;

                        //switching based on messageTypeId
                        switch ((int)payload[0])
                        {
                            case 2:
                                RequestPayload requestPayload = new RequestPayload(payload);
                                response = await ProcessRequestPayloadAsync(chargepointName, requestPayload);
                                break;

                            case 3:
                                ResponsePayload responsePayload = new ResponsePayload(payload);
                                await ProcessResponsePayloadAsync(chargepointName, responsePayload);
                                break;

                            case 4:
                                ErrorPayload errorPayload = new ErrorPayload(payload);
                                await ProcessErrorPayloadAsync(chargepointName, errorPayload);
                                continue;

                            default:
                                break;
                        }

                        if (response != null)
                        {
                            await SendPayloadToChargerAsync(chargepointName, response, webSocket);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("Exception: " + ex.Message);
                    Log.Logger.Error(ex.StackTrace);
                }
            }
        }
    }
}
