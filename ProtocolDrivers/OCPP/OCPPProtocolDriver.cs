namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using OCPPCentralSystem.Models;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;

    public class OCPPProtocolDriver: IProtocolDriver
    {
        public string Scheme => "ocpp";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/ocpp";

        private readonly OCPPCentralSystemAsset _ocppCentralSystem = new();

        public IEnumerable<string> Discover()
        {
            List<string> deviceList = new List<string>();
            try
            {
                // Discover all charge points
                foreach (var chargePoint in OCPPCentralSystemAsset.ChargePoints)
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

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            string[] endpointParts = assetEndpoint.Split([':', '/']);

            ThingDescription td = new()
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1"],
                Id = "urn:" + assetName,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Base = assetEndpoint,
                Title = assetName,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            // add actions
            td.Actions.Add("ChangeAvailability", new TDAction
            {
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "ConnectorId", new Property() { Type = TypeEnum.String } },
                        { "Type", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("ChangeConfiguration", new TDAction
            {
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "Key", new Property() { Type = TypeEnum.String } },
                        { "Value", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("ClearCache", new TDAction
            {
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("GetConfiguration", new TDAction
            {
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "Key", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("RemoteStartTransaction", new TDAction
            {
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "IdTag", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("GetTransactionStatusRequest", new TDAction
            {
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "TransactionId", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("RemoteStopTransaction", new TDAction
            {
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "TransactionId", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("Reset", new TDAction
            {
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("UnlockConnector", new TDAction
            {
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "ConnectorId", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("SetChargingProfile", new TDAction
            {
                Input = new TDArguments()
                {
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
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "AttributeType", new Property() { Type = TypeEnum.String } },
                        { "AttributeValue", new Property() { Type = TypeEnum.String } }
                    }
                }
            });
            td.Actions.Add("GetVariables", new TDAction
            {
                Input = new TDArguments()
                {
                    Properties = new Dictionary<string, Property>() {
                        { "ChargePointId", new Property() { Type = TypeEnum.String } },
                        { "AttributeType", new Property() { Type = TypeEnum.String } }
                    }
                }
            });

            // add properties for the requested charge point
            if (OCPPCentralSystemAsset.ChargePoints.TryGetValue(endpointParts[3], out ChargePoint chargePoint))
            {
                foreach (Connector connector in chargePoint.Connectors.Values)
                {
                    td.Properties.Add("Connector" + connector.ID.ToString() + "Meter", new Property
                    {
                        Type = TypeEnum.Number,
                        ReadOnly = true,
                        Observable = true,
                        Forms = new object[] {
                            new GenericForm() {
                                Type = TypeString.Float,
                                Href = assetEndpoint + "?" + connector.ID.ToString()
                            }
                        }
                    });

                    td.Properties.Add("Connector" + connector.ID.ToString() + "Status", new Property
                    {
                        Type = TypeEnum.String,
                        ReadOnly = true,
                        Observable = true,
                        Forms = new object[] {
                            new GenericForm() {
                                Type = TypeString.Float,
                                Href = assetEndpoint + "?" + connector.ID.ToString()
                            }
                        }
                    });
                }
            }

            return td;
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            unitId = 1;

            string[] address = td.Base.Split([':', '/']);
            if ((address.Length != 4) || (address[0] != "ocpp"))
            {
                throw new Exception("Expected OCPP Gateway address in the format ocpp://assetname!");
            }

            // in the case of OCPP, we don't check if we can reach the gateway as the gateway needs to contact us during onboarding
            return _ocppCentralSystem;
        }

        public AssetTag CreateTag(
            ThingDescription td,
            object form,
            string assetId,
            byte unitId,
            string variableId,
            string mappedUAExpandedNodeId,
            string mappedUAFieldPath)
        {
            GenericForm ocppForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = ocppForm.Href,
                UnitID = unitId,
                Type = ocppForm.Type.ToString(),
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }
    }
}
