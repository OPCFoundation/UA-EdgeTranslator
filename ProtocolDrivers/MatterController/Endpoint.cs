namespace Matter.Core
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