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

        static public Task<string> ProcessRequestPayloadAsync(string cpId, string correlationId, string action, string payload)
        {
            string responsePayload = "{}";

            try
            {
                // switching based on OCPP action name
                switch (action)
                {
                    case "Authorize":

                        AuthorizeRequest authRequest = JsonConvert.DeserializeObject<AuthorizeRequest>(payload);

                        Log.Logger.Information("Authorization requested on chargepoint " + cpId + "  and badge ID " + authRequest.IdTag);

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

                        BootNotificationRequest bootNotificationRequest = JsonConvert.DeserializeObject<BootNotificationRequest>(payload);

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

                        MeterValuesRequest meterValuesRequest = JsonConvert.DeserializeObject<MeterValuesRequest>(payload);

                        Log.Logger.Information("Meter values for connector ID " + meterValuesRequest.ConnectorId + " on chargepoint " + cpId + ":");

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                        }

                        if (!OCPPCentralSystem.ChargePoints[cpId].Connectors.ContainsKey(meterValuesRequest.ConnectorId))
                        {
                            OCPPCentralSystem.ChargePoints[cpId].Connectors.TryAdd(meterValuesRequest.ConnectorId, new Connector(meterValuesRequest.ConnectorId));
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
                                    OCPPCentralSystem.ChargePoints[cpId].Connectors[meterValuesRequest.ConnectorId].MeterReadings.Add(reading);

                                    if (OCPPCentralSystem.ChargePoints[cpId].Connectors[meterValuesRequest.ConnectorId].MeterReadings.Count > 10)
                                    {
                                        OCPPCentralSystem.ChargePoints[cpId].Connectors[meterValuesRequest.ConnectorId].MeterReadings.RemoveAt(0);
                                    }
                                }
                            }
                        }

                        responsePayload = JsonConvert.SerializeObject(new MeterValuesResponse());

                        break;

                    case "StartTransaction":

                        StartTransactionRequest startTransactionRequest = JsonConvert.DeserializeObject<StartTransactionRequest>(payload);

                        Log.Logger.Information("Start transaction " + _transactionNumber.ToString() + " from " + startTransactionRequest.Timestamp + " on chargepoint " + cpId + " on connector " + startTransactionRequest.ConnectorId + " with badge ID " + startTransactionRequest.IdTag + " and meter reading at start " + startTransactionRequest.MeterStart);

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                        }

                        if (!OCPPCentralSystem.ChargePoints[cpId].Connectors.ContainsKey(startTransactionRequest.ConnectorId))
                        {
                            OCPPCentralSystem.ChargePoints[cpId].Connectors.TryAdd(startTransactionRequest.ConnectorId, new Connector(startTransactionRequest.ConnectorId));
                        }

                        _transactionNumber++;

                        ChargePointTransaction transaction = new()
                        {
                            ID = _transactionNumber,
                            BadgeID = startTransactionRequest.IdTag,
                            StartTime = startTransactionRequest.Timestamp,
                            MeterValueStart = startTransactionRequest.MeterStart
                        };

                        if (!OCPPCentralSystem.ChargePoints[cpId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.ContainsKey(_transactionNumber))
                        {
                            OCPPCentralSystem.ChargePoints[cpId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.TryAdd(_transactionNumber, transaction);
                        }

                        // housekeeping: Remove transactions that are older than 1 day
                        KeyValuePair<int, ChargePointTransaction>[] transactionsArray = OCPPCentralSystem.ChargePoints[cpId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.ToArray();
                        for (int i = 0; i < transactionsArray.Length; i++)
                        {
                            if ((transactionsArray[i].Value.StopTime != DateTime.MinValue) && (transactionsArray[i].Value.StopTime < DateTime.UtcNow.Subtract(TimeSpan.FromDays(1))))
                            {
                                OCPPCentralSystem.ChargePoints[cpId].Connectors[startTransactionRequest.ConnectorId].CurrentTransactions.TryRemove(transactionsArray[i].Key, out _);
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

                        Log.Logger.Information("Stop transaction " + stopTransactionRequest.TransactionId.ToString() + " from " + stopTransactionRequest.Timestamp + " on chargepoint " + cpId + " with badge ID " + stopTransactionRequest.IdTag + " and meter reading at stop " + stopTransactionRequest.MeterStop);

                        if (!OCPPCentralSystem.ChargePoints.ContainsKey(cpId))
                        {
                            OCPPCentralSystem.ChargePoints.TryAdd(cpId, new ChargePoint());
                            OCPPCentralSystem.ChargePoints[cpId].ID = cpId;
                        }

                        // find the transaction
                        KeyValuePair<int, Connector>[] connectorArray = OCPPCentralSystem.ChargePoints[cpId].Connectors.ToArray();
                        for (int i = 0; i < connectorArray.Length; i++)
                        {
                            if (OCPPCentralSystem.ChargePoints[cpId].Connectors[connectorArray[i].Key].CurrentTransactions.ContainsKey(stopTransactionRequest.TransactionId))
                            {
                                OCPPCentralSystem.ChargePoints[cpId].Connectors[connectorArray[i].Key].CurrentTransactions[stopTransactionRequest.TransactionId].MeterValueFinish = stopTransactionRequest.MeterStop;
                                OCPPCentralSystem.ChargePoints[cpId].Connectors[connectorArray[i].Key].CurrentTransactions[stopTransactionRequest.TransactionId].StopTime = stopTransactionRequest.Timestamp;
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

                        Log.Logger.Information("Chargepoint " + cpId + " and connector " + statusNotificationRequest.ConnectorId + " status#: " + statusNotificationRequest.Status.ToString());

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
                        OCPPCentralSystem.ChargePoints[cpId].Connectors[statusNotificationRequest.ConnectorId].Status = statusNotificationRequest.Status.ToString();

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
    }
}
