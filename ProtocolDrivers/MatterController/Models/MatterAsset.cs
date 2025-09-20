namespace Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models
{
    using System.Net;
    using Matter.Core;

    public class MatterAsset
    {
        public IPAddress IpAddress { get; set; }

        public ushort Port { get; set; }

        public Node Node { get; set; }

        public uint Passcode { get; set; }
    }
}
