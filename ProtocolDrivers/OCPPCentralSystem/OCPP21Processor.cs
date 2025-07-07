namespace OCPPCentralSystem
{
    using global::OCPPCentralSystem.Models;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    // Mandatory commands (charge point - i.e. the remote connected client - initiated):
    // Authorize
    // BootNotification
    // DataTransfer
    // Heartbeat
    // MeterValues
    // TransactionEvent
    // NotifyEvent
    // NotifyMonitoring
    // NotifyReport
    // StatusNotification

    // Mandatory commands (central system - i.e. us! - initiated):
    // SetVariables
    // GetVariables
    // ChangeAvailability
    // ClearCache
    // RemoteStartTransaction
    // GetTransactionStatus
    // RemoteStopTransaction
    // Reset
    // SetChargingProfile
    // UnlockConnector
    // LogStatusNotification

    public class OCPP21Processor
    {
        static public Task<string> ProcessRequestPayloadAsync(string cpId, string correlationId, string action, string payload)
        {
            string responsePayload = "{}";

            try
            {
                // switching based on OCPP action name
                switch (action)
                {
                    case "Authorize":

                        AuthorizeRequest21 authRequest = JsonConvert.DeserializeObject<AuthorizeRequest21>(payload);

                        Log.Logger.Information("Authorization requested on chargepoint " + cpId + "  and badge ID " + authRequest.IdToken.IdToken);

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
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

                    case "BootNotification":

                        BootNotificationRequest21 bootNotificationRequest = JsonConvert.DeserializeObject<BootNotificationRequest21>(payload);

                        Log.Logger.Information("Charge point with identity: " + cpId + " booted!");

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                        }

                        responsePayload = JsonConvert.SerializeObject(new BootNotificationResponse()
                        {
                            Status = RegistrationStatus.Accepted,
                            CurrentTime = DateTime.UtcNow,
                            Interval = 60
                        });

                        break;

                    case "Heartbeat":

                        HeartbeatRequest heartbeatRequest = JsonConvert.DeserializeObject<HeartbeatRequest>(payload);

                        responsePayload = JsonConvert.SerializeObject(new HeartbeatResponse()
                        {
                            CurrentTime = DateTime.UtcNow
                        });

                        break;

                    case "MeterValues":

                        MeterValuesRequest21 meterValuesRequest = JsonConvert.DeserializeObject<MeterValuesRequest21>(payload);

                        Log.Logger.Information("Meter values for connector ID " + meterValuesRequest.EvseId + " on chargepoint " + cpId + ":");

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                        }

                        if (!OCPPCentralSystem.ChargePoints[cpId].Connectors.ContainsKey(meterValuesRequest.EvseId))
                        {
                            OCPPCentralSystem.ChargePoints[cpId].Connectors.TryAdd(meterValuesRequest.EvseId, new Connector(meterValuesRequest.EvseId));
                        }

                        foreach (MeterValue21 meterValue in meterValuesRequest.MeterValue)
                        {
                            foreach (SampledValue21 sampledValue in meterValue.SampledValue)
                            {
                                Log.Logger.Information("Value: " + sampledValue.Value + " " + sampledValue.UnitOfMeasure?.Unit?.ToString());

                                MeterReading reading = new MeterReading();
                                reading.MeterValue = (int)sampledValue.Value;

                                if (!string.IsNullOrEmpty(sampledValue.UnitOfMeasure?.Unit))
                                {
                                    reading.MeterValueUnit = sampledValue.UnitOfMeasure?.Unit?.ToString();
                                }

                                reading.Timestamp = meterValue.Timestamp;
                                OCPPCentralSystem.ChargePoints[cpId].Connectors[meterValuesRequest.EvseId].MeterReadings.Add(reading);

                                if (OCPPCentralSystem.ChargePoints[cpId].Connectors[meterValuesRequest.EvseId].MeterReadings.Count > 10)
                                {
                                    OCPPCentralSystem.ChargePoints[cpId].Connectors[meterValuesRequest.EvseId].MeterReadings.RemoveAt(0);
                                }
                            }
                        }

                        responsePayload = JsonConvert.SerializeObject(new MeterValuesResponse());

                        break;

                    case "TransactionEvent":

                        TransactionEventRequest transactionEventRequest = JsonConvert.DeserializeObject<TransactionEventRequest>(payload);

                        Log.Logger.Information("Transaction from " + transactionEventRequest.Timestamp + " on chargepoint " + cpId + " with badge ID " + transactionEventRequest.IdToken.IdToken);

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                        }

                        if (!OCPPCentralSystem.ChargePoints[cpId].Connectors.ContainsKey(transactionEventRequest.Evse.ConnectorId))
                        {
                            OCPPCentralSystem.ChargePoints[cpId].Connectors.TryAdd(transactionEventRequest.Evse.ConnectorId, new Connector(transactionEventRequest.Evse.ConnectorId));
                        }

                        ChargePointTransaction transaction = new()
                        {
                            ID = transactionEventRequest.IdToken.IdToken.GetHashCode(),
                            BadgeID = transactionEventRequest.TransactionInfo.TransactionId,
                            StartTime = transactionEventRequest.Timestamp,
                            MeterValueStart = transactionEventRequest.TransactionInfo.TimeSpentCharging
                        };

                        if (!OCPPCentralSystem.ChargePoints[cpId].Connectors[transactionEventRequest.Evse.ConnectorId].CurrentTransactions.ContainsKey(transactionEventRequest.TransactionInfo.TransactionId.GetHashCode()))
                        {
                            OCPPCentralSystem.ChargePoints[cpId].Connectors[transactionEventRequest.Evse.ConnectorId].CurrentTransactions.TryAdd(transactionEventRequest.TransactionInfo.TransactionId.GetHashCode(), transaction);
                        }

                        // housekeeping: Remove transactions that are older than 1 day
                        KeyValuePair<int, ChargePointTransaction>[] transactionsArray = OCPPCentralSystem.ChargePoints[cpId].Connectors[transactionEventRequest.Evse.ConnectorId].CurrentTransactions.ToArray();
                        for (int i = 0; i < transactionsArray.Length; i++)
                        {
                            if ((transactionsArray[i].Value.StopTime != DateTime.MinValue) && (transactionsArray[i].Value.StopTime < DateTime.UtcNow.Subtract(TimeSpan.FromDays(1))))
                            {
                                OCPPCentralSystem.ChargePoints[cpId].Connectors[transactionEventRequest.Evse.ConnectorId].CurrentTransactions.TryRemove(transactionsArray[i].Key, out _);
                            }
                        }

                        IdTagInfo taginfo = new IdTagInfo
                        {
                            ExpiryDateSpecified = false,
                            Status = AuthorizationStatus.Accepted
                        };

                        responsePayload = JsonConvert.SerializeObject(new TransactionEventResponse()
                        {
                            IdTokenInfo = new IdTokenInfo
                            {
                                Status = AuthorizationStatus.Accepted
                            }
                        });

                        break;

                    case "NotifyEvent":

                            NotifyEventRequest notifyEventRequest = JsonConvert.DeserializeObject<NotifyEventRequest>(payload);

                            Log.Logger.Information("Event notification on chargepoint " + cpId);

                            if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                            {
                                OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                                OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                            }

                            foreach (var eventData in notifyEventRequest.EventData)
                            {
                                Log.Logger.Information("Event Data: " + eventData.ToString());
                            }

                            responsePayload = JsonConvert.SerializeObject(new NotifyEventResponse());

                            break;

                    case "NotifyMonitoringReport":

                        NotifyMonitoringReportRequest notifyMonitoringReportRequest = JsonConvert.DeserializeObject<NotifyMonitoringReportRequest>(payload);

                        Log.Logger.Information("Monitoring report on chargepoint " + cpId);

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                        }

                        responsePayload = JsonConvert.SerializeObject(new NotifyMonitoringReportResponse());

                        break;

                    case "NotifyReport":

                        NotifyReportRequest notifyReportRequest = JsonConvert.DeserializeObject<NotifyReportRequest>(payload);

                        Log.Logger.Information("Report notification on chargepoint " + cpId + ": " + notifyReportRequest.ReportData.ToString());

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                        }

                        responsePayload = JsonConvert.SerializeObject(new NotifyReportResponse());

                        break;

                    case "StatusNotification":

                        StatusNotificationRequest21 statusNotificationRequest = JsonConvert.DeserializeObject<StatusNotificationRequest21>(payload);

                        Log.Logger.Information("Chargepoint " + cpId + " and connector " + statusNotificationRequest.ConnectorId + " status#: " + statusNotificationRequest.ConnectorStatus.ToString());

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                        }

                        if (!OCPPCentralSystem.ChargePoints[cpId].Connectors.ContainsKey(statusNotificationRequest.ConnectorId))
                        {
                            OCPPCentralSystem.ChargePoints[cpId].Connectors.TryAdd(statusNotificationRequest.ConnectorId, new Connector(statusNotificationRequest.ConnectorId));
                        }

                        OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                        OCPPCentralSystem.ChargePoints[cpId].Connectors[statusNotificationRequest.ConnectorId].Status = statusNotificationRequest.ConnectorStatus.ToString();

                        responsePayload = JsonConvert.SerializeObject(new StatusNotificationResponse());

                        break;

                    case "DataTransfer":

                        responsePayload = JsonConvert.SerializeObject(new DataTransferResponse21()
                        {
                            Status = DataTransferStatus.Rejected
                        });

                        break;

                    default:

                        break;
                }

                return Task.FromResult(JsonConvert.SerializeObject(new JArray
                {
                    3, // messageTypeId for response
                    correlationId, // correlationId from request
                    JObject.Parse(responsePayload) // payload
                }));
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);

                return Task.FromResult(JsonConvert.SerializeObject(new JArray
                {
                    4, // messageTypeId for error response
                    correlationId, // correlationId from request
                    "500", // error code
                    ex.Message, // error description
                    string.Empty // empty payload
                }));
            }
        }

        public static Task SendCentralStationCommand(string cpId, string action, string[] arguments)
        {
            string requestPayload = "{}";

            if (OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
            {
                Log.Logger.Information("Sending command " + action + " to chargepoint " + cpId);
            }
            else
            {
                Log.Logger.Error("Chargepoint " + cpId + " not found. Cannot send command: " + action);
            }

            try
            {
                // switching based on OCPP action name
                switch (action)
                {
                    case "SetVariables":
                        if (arguments.Length < 2)
                        {
                            Log.Logger.Error("SetVariables requires at least 2 arguments: variable type and value.");
                            return Task.CompletedTask;
                        }

                        SetVariablesRequest setVariablesRequest = new() {
                          SetVariableData = new() {
                              new SetVariableData {
                                  AttributeType = (AttributeType)Enum.Parse(typeof(AttributeType), arguments[0], true),
                                  AttributeValue = new List<string> { arguments[1] },
                                  Component = new Component(),
                                  Variable = new Variable()
                              }
                          }
                        };

                        Log.Logger.Information("Setting variable " + arguments[0] + " on chargepoint " + cpId + " to value: " + arguments[1]);

                        requestPayload = JsonConvert.SerializeObject(setVariablesRequest);

                        break;

                    case "GetVariables":

                        if (arguments.Length < 1)
                        {
                            Log.Logger.Error("GetVariables requires at least 1 argument: variable type.");
                            return Task.CompletedTask;
                        }

                        GetVariablesRequest getVariablesRequest = new() {
                            GetVariableData = new List<GetVariableData>
                            {
                                 new GetVariableData
                                 {
                                     AttributeType = (AttributeType)Enum.Parse(typeof(AttributeType), arguments[0], true),
                                     Component = new Component(),
                                     Variable = new Variable()
                                 }
                            }
                        };

                        Log.Logger.Information("Getting variables of type " + arguments[0] + " from chargepoint " + cpId);

                        requestPayload = JsonConvert.SerializeObject(getVariablesRequest);

                        break;

                    case "ChangeAvailability":

                        if (arguments.Length < 2)
                        {
                            Log.Logger.Error("ChangeAvailability requires at least 2 arguments: connectorId and type.");
                            return Task.CompletedTask;
                        }

                        ChangeAvailabilityRequest changeAvailabilityRequest = new();
                        changeAvailabilityRequest.ConnectorId = int.Parse(arguments[0]);
                        changeAvailabilityRequest.Type = (Availability)Enum.Parse(typeof(Availability), arguments[1], true);

                        Log.Logger.Information("Changing availability of connector " + changeAvailabilityRequest.ConnectorId + " on chargepoint " + cpId + " to " + changeAvailabilityRequest.Type.ToString());

                        requestPayload = JsonConvert.SerializeObject(changeAvailabilityRequest);

                        break;

                    case "ClearCache":

                        Log.Logger.Information($"ClearCache requested on chargepoint {cpId}");

                        requestPayload = JsonConvert.SerializeObject(new ClearCacheRequest());

                        break;

                    case "RemoteStartTransaction":

                        if (arguments.Length < 1)
                        {
                            Log.Logger.Error("RemoteStartTransaction requires at least 1 argument: idTag.");
                            return Task.CompletedTask;
                        }

                        RemoteStartTransactionRequest remoteStartRequest = new() {
                            IdTag = arguments[0]
                        };

                        Log.Logger.Information($"RemoteStartTransaction requested on chargepoint {cpId} for idTag: {remoteStartRequest.IdTag}");

                        requestPayload = JsonConvert.SerializeObject(remoteStartRequest);

                        break;

                    case "GetTransactionStatusRequest":

                        if (arguments.Length < 1)
                        {
                            Log.Logger.Error("GetTransactionStatusRequest requires at least 1 argument: transactionId.");
                            return Task.CompletedTask;
                        }

                        GetTransactionStatusRequest getTransactionStatusRequest = new()
                        {
                            TransactionId = arguments[0]
                        };

                        Log.Logger.Information($"GetTransactionStatusRequest requested on chargepoint {cpId} for transactionId: {getTransactionStatusRequest.TransactionId}");

                        requestPayload = JsonConvert.SerializeObject(getTransactionStatusRequest);

                        break;

                    case "RemoteStopTransaction":

                        if (arguments.Length < 1)
                        {
                            Log.Logger.Error("RemoteStopTransaction requires at least 1 argument: transactionId.");
                            return Task.CompletedTask;
                        }

                        RemoteStopTransactionRequest remoteStopRequest = new();
                        remoteStopRequest.TransactionId = int.Parse(arguments[0]);

                        Log.Logger.Information($"RemoteStopTransaction requested on chargepoint {cpId} for transactionId: {remoteStopRequest.TransactionId}");

                        requestPayload = JsonConvert.SerializeObject(remoteStopRequest);

                        break;

                    case "Reset":

                        ResetRequest resetRequest = new();
                        resetRequest.Type = ResetType.Hard;

                        Log.Logger.Information($"Reset requested on chargepoint {cpId} type: {resetRequest.Type}");

                        requestPayload = JsonConvert.SerializeObject(resetRequest);

                        break;

                    case "SetChargingProfile":

                        if (arguments.Length < 3)
                        {
                            Log.Logger.Error("SetChargingProfile requires at least 3 arguments: EVSE ID, limit and number of phases.");
                            return Task.CompletedTask;
                        }

                        SetChargingProfileRequest21 setChargingProfileRequest = new() {
                            EvseId = int.Parse(arguments[0]),
                            ChargingProfile = new ChargingProfile21 {
                                ChargingProfileKind = ChargingProfileKind.Absolute,
                                ChargingProfilePurpose = ChargingProfilePurpose.ChargePointMaxProfile,
                                Id = 1,
                                StackLevel = 0,
                                ChargingSchedule = new List<ChargingSchedule21>() {
                                    new ChargingSchedule21 {
                                        Id = 1,
                                        ChargingRateUnit = ChargingRateUnit.W,
                                        ChargingSchedulePeriod = new List<ChargingSchedulePeriod>() {
                                            new ChargingSchedulePeriod {
                                                StartPeriod = 0,
                                                Limit = int.Parse(arguments[1]),
                                                NumberPhases = int.Parse(arguments[2])
                                            }
                                        }
                                    }
                                }
                            }
                        };

                        Log.Logger.Information($"SetChargingProfile requested on chargepoint {arguments[0]} for EvseId: {setChargingProfileRequest.EvseId} with limit: {arguments[1]}");

                        requestPayload = JsonConvert.SerializeObject(setChargingProfileRequest);

                        break;

                   case "UnlockConnector":

                        if (arguments.Length < 1)
                        {
                            Log.Logger.Error("UnlockConnector requires at least 1 argument: connectorId.");
                            return Task.CompletedTask;
                        }

                        UnlockConnectorRequest unlockRequest = new();
                        unlockRequest.ConnectorId = int.Parse(arguments[0]);

                        Log.Logger.Information($"UnlockConnector requested on chargepoint {cpId} for connectorId: {unlockRequest.ConnectorId}");

                        requestPayload = JsonConvert.SerializeObject(unlockRequest);

                        break;

                    default:

                        break;
                }

                string serializedCommand = JsonConvert.SerializeObject(new JArray
                {
                    2, // messageTypeId for request
                    Guid.NewGuid().ToString("N"), // correlationId for request
                    JObject.Parse(requestPayload) // payload
                });

                WebsocketJsonMiddlewareOCPP.PendingMessagess.TryAdd(cpId, serializedCommand);

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);

                return Task.CompletedTask;
            }
        }

        public static Task ProcessErrorPayloadAsync(string cpId, string payload)
        {
            Log.Logger.Error("Error processing payload for chargepoint " + cpId + " - " + payload);
            return Task.CompletedTask;
        }

        public static Task ProcessResponsePayloadAsync(string cpId, string correlationId, string payload)
        {
            try
            {
                Log.Logger.Information("Response for chargepoint " + cpId + ": " + payload);
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Exception: " + ex.Message);
            }

            return Task.CompletedTask;
        }
    }
}
