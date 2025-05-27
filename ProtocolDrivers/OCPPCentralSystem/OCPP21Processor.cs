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

    public class OCPP21Processor
    {
        static public Task<string> ProcessRequestPayloadAsync(string uniqueId, string action, string payload)
        {
            string responsePayload = string.Empty;

            try
            {
                // switching based on OCPP action name
                switch (action)
                {
                    case "Authorize":

                        AuthorizeRequest21 authRequest = JsonConvert.DeserializeObject<AuthorizeRequest21>(payload);

                        Log.Logger.Information("Authorization requested on chargepoint " + uniqueId + "  and badge ID " + authRequest.IdToken);

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

                    case "BootNotification":

                        BootNotificationRequest21 bootNotificationRequest = JsonConvert.DeserializeObject<BootNotificationRequest21>(payload);

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

                    case "Heartbeat":

                        HeartbeatRequest21 heartbeatRequest = JsonConvert.DeserializeObject<HeartbeatRequest21>(payload);

                        Log.Logger.Information("Heartbeat received from: " + uniqueId);

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(uniqueId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(uniqueId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[uniqueId].ID = uniqueId;
                        }

                        responsePayload = JsonConvert.SerializeObject(new HeartbeatResponse()
                        {
                            CurrentTime = DateTime.UtcNow
                        });

                        break;

                    case "MeterValues":

                        MeterValuesRequest21 meterValuesRequest = JsonConvert.DeserializeObject<MeterValuesRequest21>(payload);

                        Log.Logger.Information("Meter values for connector ID " + meterValuesRequest.EvseId + " on chargepoint " + uniqueId + ":");

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(uniqueId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(uniqueId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[uniqueId].ID = uniqueId;
                        }

                        if (!OCPPCentralSystem.ChargePoints[uniqueId].Connectors.ContainsKey(meterValuesRequest.EvseId))
                        {
                            OCPPCentralSystem.ChargePoints[uniqueId].Connectors.TryAdd(meterValuesRequest.EvseId, new Connector(meterValuesRequest.EvseId));
                        }

                        foreach (MeterValue21 meterValue in meterValuesRequest.MeterValue)
                        {
                            foreach (SampledValue21 sampledValue in meterValue.SampledValue)
                            {
                                Log.Logger.Information("Value: " + sampledValue.Value + " " + sampledValue.UnitOfMeasure.Unit.ToString());

                                MeterReading reading = new MeterReading();
                                reading.MeterValue = (int)sampledValue.Value;

                                if (!string.IsNullOrEmpty(sampledValue.UnitOfMeasure.Unit))
                                {
                                    reading.MeterValueUnit = sampledValue.UnitOfMeasure.Unit.ToString();
                                }

                                reading.Timestamp = meterValue.Timestamp;
                                OCPPCentralSystem.ChargePoints[uniqueId].Connectors[meterValuesRequest.EvseId].MeterReadings.Add(reading);

                                if (OCPPCentralSystem.ChargePoints[uniqueId].Connectors[meterValuesRequest.EvseId].MeterReadings.Count > 10)
                                {
                                    OCPPCentralSystem.ChargePoints[uniqueId].Connectors[meterValuesRequest.EvseId].MeterReadings.RemoveAt(0);
                                }
                            }
                        }

                        responsePayload = JsonConvert.SerializeObject(new MeterValuesResponse());

                        break;

                    case "TransactionEvent":

                        TransactionEventRequest transactionEventRequest = JsonConvert.DeserializeObject<TransactionEventRequest>(payload);

                        Log.Logger.Information("Start transaction from " + transactionEventRequest.Timestamp + " on chargepoint " + uniqueId + " with badge ID " + transactionEventRequest.IdToken);

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(transactionEventRequest.Evse.Id.ToString()))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(transactionEventRequest.Evse.Id.ToString(), new ChargePoint());
                            OCPPCentralSystem.ChargePoints[transactionEventRequest.Evse.Id.ToString()].ID = transactionEventRequest.Evse.Id.ToString();
                        }

                        if (!OCPPCentralSystem.ChargePoints[transactionEventRequest.Evse.Id.ToString()].Connectors.ContainsKey(transactionEventRequest.Evse.ConnectorId))
                        {
                            OCPPCentralSystem.ChargePoints[transactionEventRequest.Evse.Id.ToString()].Connectors.TryAdd(transactionEventRequest.Evse.ConnectorId, new Connector(transactionEventRequest.Evse.ConnectorId));
                        }

                        ChargePointTransaction transaction = new()
                        {
                            ID = transactionEventRequest.IdToken.IdToken.GetHashCode(),
                            BadgeID = transactionEventRequest.TransactionInfo.TransactionId,
                            StartTime = transactionEventRequest.Timestamp,
                            MeterValueStart = transactionEventRequest.TransactionInfo.TimeSpentCharging
                        };

                        if (!OCPPCentralSystem.ChargePoints[transactionEventRequest.Evse.Id.ToString()].Connectors[transactionEventRequest.Evse.ConnectorId].CurrentTransactions.ContainsKey(transactionEventRequest.TransactionInfo.TransactionId.GetHashCode()))
                        {
                            OCPPCentralSystem.ChargePoints[transactionEventRequest.Evse.Id.ToString()].Connectors[transactionEventRequest.Evse.ConnectorId].CurrentTransactions.TryAdd(transactionEventRequest.TransactionInfo.TransactionId.GetHashCode(), transaction);
                        }

                        // housekeeping: Remove transactions that are older than 1 day
                        KeyValuePair<int, ChargePointTransaction>[] transactionsArray = OCPPCentralSystem.ChargePoints[transactionEventRequest.Evse.Id.ToString()].Connectors[transactionEventRequest.Evse.ConnectorId].CurrentTransactions.ToArray();
                        for (int i = 0; i < transactionsArray.Length; i++)
                        {
                            if ((transactionsArray[i].Value.StopTime != DateTime.MinValue) && (transactionsArray[i].Value.StopTime < DateTime.UtcNow.Subtract(TimeSpan.FromDays(1))))
                            {
                                OCPPCentralSystem.ChargePoints[transactionEventRequest.Evse.Id.ToString()].Connectors[transactionEventRequest.Evse.ConnectorId].CurrentTransactions.TryRemove(transactionsArray[i].Key, out _);
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

                   case "StatusNotification":

                        StatusNotificationRequest21 statusNotificationRequest = JsonConvert.DeserializeObject<StatusNotificationRequest21>(payload);

                        Log.Logger.Information("Chargepoint " + uniqueId + " and connector " + statusNotificationRequest.ConnectorId + " status#: " + statusNotificationRequest.ConnectorStatus.ToString());

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
                        OCPPCentralSystem.ChargePoints[uniqueId].Connectors[statusNotificationRequest.ConnectorId].Status = statusNotificationRequest.ConnectorStatus.ToString();

                        responsePayload = JsonConvert.SerializeObject(new StatusNotificationResponse());

                        break;

                    case "DataTransfer":

                        responsePayload = JsonConvert.SerializeObject(new DataTransferResponse21()
                        {
                            Status = DataTransferStatus.Rejected
                        });

                        break;

                    case "DiagnosticsStatusNotification":

                        responsePayload = JsonConvert.SerializeObject(new DiagnosticsStatusNotificationResponse());

                        break;

                    case "FirmwareStatusNotification":

                        responsePayload = JsonConvert.SerializeObject(new FirmwareStatusNotificationResponse());

                        break;

                    default:

                        break;
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
