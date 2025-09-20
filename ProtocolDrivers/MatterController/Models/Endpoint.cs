namespace Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models
{
    public class Endpoint
    {
        public Endpoint(uint endpointId)
        {
            EndpointId = endpointId;
        }

        public uint EndpointId { get; }

        public ulong DeviceType { get; set; }
    }
}
