namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using OCPPCentralSystem;
    using OCPPCentralSystem.Models;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class OCPPCentralSystemAsset : IAsset
    {
        public static ConcurrentDictionary<string, ChargePoint> ChargePoints { get; set; } = new();

        public bool IsConnected => true;

        public OCPPCentralSystemAsset()
        {
            _ = Task.Run(CentralSystemServer.RunServerAsync);
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
