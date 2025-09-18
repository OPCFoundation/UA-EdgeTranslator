
namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using MatterDotNet.Entities;
    using MatterDotNet.OperationalDiscovery;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.Text;

    public class MatterClient : IAsset
    {
        private Controller _controller;

        public void Connect(string ipAddress, int port)
        {
            try
            {
                // Parse configJson to extract device info and attribute mappings
                string[] commissionPayload = ipAddress.Split(['/']);

                _controller = new Controller(commissionPayload[2]);

                CommissioningState state = _controller.StartCommissioning(CommissioningPayload.FromQR("MT:" + commissionPayload[3])).GetAwaiter().GetResult();
                _controller.CompleteCommissioning(state).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to connect to Matter device: " + ex.Message);
            }
        }

        public List<string> Discover()
        {
            // Matter does not support discovery
            return new List<string>();
        }

        public ThingDescription BrowseAndGenerateTD(string name, string endpoint)
        {
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

            return td;
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

            // TODO: Implement the read logic from the Matter device

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

            // TODO: Implement the write logic to the Matter device
        }

        public string ExecuteAction(MethodState method, string[] inputArgs, ref string[] outputArgs)
        {
            throw new NotImplementedException();
        }
    }
}
