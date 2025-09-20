
namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Matter.Core;
    using Matter.Core.Fabrics;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;

    public class MatterClient : IAsset
    {
        private Matter.Core.MatterController _controller;

        public void Connect(string ipAddress, int port)
        {
            string[] ipParts = ipAddress.Split(['/']);

            try
            {
                IFabricStorageProvider fabricStorageProvider = new FabricDiskStorage(Directory.GetCurrentDirectory());
                _controller = new Matter.Core.MatterController(fabricStorageProvider);

                _controller.InitAsync().GetAwaiter().GetResult();
                Task.Run(() => _controller.RunAsync().GetAwaiter().GetResult());

                Node asset = _controller.Fabric.AddCommissionedNodeAsync(new Org.BouncyCastle.Math.BigInteger(ipParts[4]), IPAddress.Parse(ipParts[2]), ushort.Parse(ipParts[3]));
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
