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

    public class OCPP16Processor
    {
        static private int _transactionNumber = 0;

        static public Task<string> ProcessRequestPayloadAsync(string uniqueId, string action, string payload)
        {
            string responsePayload = string.Empty;

            try
            {
                // switching based on OCPP action name
                switch (action)
                {
                    case "Authorize":

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

                    case "BootNotification":

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

                    case "Heartbeat":

                        HeartbeatRequest heartbeatRequest = JsonConvert.DeserializeObject<HeartbeatRequest>(payload);

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

                    case "StartTransaction":

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

                        ChargePointTransaction transaction = new()
                        {
                            ID = _transactionNumber,
                            BadgeID = startTransactionRequest.IdTag,
                            StartTime = startTransactionRequest.Timestamp,
                            MeterValueStart = startTransactionRequest.MeterStart
                        };

                        if (!OCPPCentralSystem.ChargePoints[uniqueId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.ContainsKey(_transactionNumber))
                        {
                            OCPPCentralSystem.ChargePoints[uniqueId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.TryAdd(_transactionNumber, transaction);
                        }

                        // housekeeping: Remove transactions that are older than 1 day
                        KeyValuePair<int, ChargePointTransaction>[] transactionsArray = OCPPCentralSystem.ChargePoints[uniqueId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.ToArray();
                        for (int i = 0; i < transactionsArray.Length; i++)
                        {
                            if ((transactionsArray[i].Value.StopTime != DateTime.MinValue) && (transactionsArray[i].Value.StopTime < DateTime.UtcNow.Subtract(TimeSpan.FromDays(1))))
                            {
                                OCPPCentralSystem.ChargePoints[uniqueId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.TryRemove(transactionsArray[i].Key, out _);
                            }
                        }

                        IdTagInfo taginfo = new IdTagInfo
                        {
                            ExpiryDateSpecified = false,
                            Status = AuthorizationStatus.Accepted
                        };

                        responsePayload = JsonConvert.SerializeObject(new StartTransactionResponse()
                        {
                            TransactionId = _transactionNumber,
                            IdTagInfo = taginfo
                        });

                        break;

                    case "StopTransaction":

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

                        IdTagInfo taginfo2 = new IdTagInfo
                        {
                            ExpiryDateSpecified = false,
                            Status = AuthorizationStatus.Accepted
                        };

                        responsePayload = JsonConvert.SerializeObject(new StopTransactionResponse()
                        {
                            IdTagInfo = taginfo2
                        });

                        break;

                    case "StatusNotification":

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

                    case "DataTransfer":

                        responsePayload = JsonConvert.SerializeObject(new DataTransferResponse()
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
