namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using global::OCPPCentralSystem;
    using global::OCPPCentralSystem.Models;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class OCPPCentralSystem : IAsset
    {
        public static ConcurrentDictionary<string, ChargePoint> ChargePoints { get; set; } = new();

        public bool IsConnected => true;

        public OCPPCentralSystem()
        {
            _ = Task.Run(() => CentralSystemServer.RunServerAsync());
        }

        public List<string> Discover()
        {
            List<string> deviceList = new List<string>();
            try
            {
                // Discover all charge points
                foreach (var chargePoint in ChargePoints)
                {
                    deviceList.Add("ocpp://" + chargePoint.Key);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Error during discovery of OCPP devices.");
            }

            return deviceList;
        }

        public ThingDescription BrowseAndGenerateTD(string name, string endpoint)
        {
            string[] endpointParts = endpoint.Split(new char[] { ':', '/' });

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + name,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "Thing" },
                Name = name,
                Base = endpoint,
                Title = name,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            // add actions
            td.Actions.Add("ChangeAvailability", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "ConnectorId", new Property() { Type = TypeEnum.String } },
                        { "Type", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("ChangeConfiguration", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "Key", new Property() { Type = TypeEnum.String } },
                        { "Value", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("ClearCache", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("GetConfiguration", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "Key", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("RemoteStartTransaction", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "IdTag", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("GetTransactionStatusRequest", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "TransactionId", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("RemoteStopTransaction", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "TransactionId", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("Reset", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("UnlockConnector", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "ConnectorId", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("SetChargingProfile", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "ConnectorId", new Property() { Type = TypeEnum.String } },
                        { "Limit", new Property() { Type = TypeEnum.String } },
                        { "NumberOfPhases", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("SetVariables", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "AttributeType", new Property() { Type = TypeEnum.String } },
                        { "AttributeValue", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("GetVariables", new TDAction
            {
                Input = new TDArguments() {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "AttributeType", new Property() { Type = TypeEnum.String } }
                    }
                }
            });

            // add properties for the requested charge point
            if (ChargePoints.TryGetValue(endpointParts[3], out ChargePoint chargePoint))
            {
                foreach (Connector connector in chargePoint.Connectors.Values)
                {
                    td.Properties.Add("Connector" + connector.ID.ToString() + "Meter", new Property {
                        Type = TypeEnum.Number,
                        ReadOnly = true,
                        Observable = true,
                        Forms = new object[] {
                            new GenericForm() {
                                Type = TypeString.Float,
                                Href = endpoint + "?" + connector.ID.ToString()
                            }
                        }
                    });

                    td.Properties.Add("Connector" + connector.ID.ToString() + "Status", new Property {
                        Type = TypeEnum.String,
                        ReadOnly = true,
                        Observable = true,
                        Forms = new object[] {
                            new GenericForm() {
                                Type = TypeString.Float,
                                Href = endpoint + "?" + connector.ID.ToString()
                            }
                        }
                    });
                }
            }

            return td;
        }

        public void Connect(string ipAddress, int port)
        {
            // nothing to do, since we are the server in OCPP and the charging stations connect to us
        }

        public void Disconnect()
        {
            // nothing to do
        }

        public string GetRemoteEndpoint()
        {
            return string.Empty;
        }

        public object Read(AssetTag tag)
        {
            object value = null;

            string[] addressParts = tag.Address.Split(['?', '/']);

            // find our charge point
            foreach (ChargePoint chargePoint in ChargePoints.Values)
            {
                if (chargePoint.ID == addressParts[2])
                {
                    // find the connector
                    if (chargePoint.Connectors.TryGetValue(int.Parse(addressParts[3]), out Connector connector))
                    {
                        if (tag.Name.EndsWith("Meter"))
                        {
                            // read the meter value
                            if (connector.MeterReadings.Count > 0)
                            {
                                // pick the last meter reading
                                value = connector.MeterReadings[connector.MeterReadings.Count - 1].MeterValue.ToString();
                            }
                            else
                            {
                                value = "0"; // no meter readings available
                            }
                        }
                        else if (tag.Name.EndsWith("Status"))
                        {
                            // read the status of the connector
                            value = connector.Status;
                        }
                        else
                        {
                            throw new ArgumentException("Unknown tag name: " + tag.Name);
                        }
                    }
                }
            }

            return value;
        }

        public void Write(AssetTag tag, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "Float")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "Boolean")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "Integer")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by LoRaWAN.");
            }

            // TODO: Implement the write logic to the charge point
        }

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            if (inputArgs.Count > 0)
            {
                if (WebsocketJsonMiddlewareOCPP.ExecuteCommand(inputArgs[0].ToString(), method.BrowseName.Name, inputArgs.Select(arg => arg?.ToString()).ToArray(), outputArgs.Select(arg => arg?.ToString()).ToArray()))
                {
                    return "Action executed successfully.";
                }
                else
                {
                    throw new Exception("Failed to execute action: " + method.BrowseName.Name);
                }
            }
            else
            {
                throw new ArgumentException("No input arguments provided for action: " + method.BrowseName.Name);
            }
        }
    }
}
