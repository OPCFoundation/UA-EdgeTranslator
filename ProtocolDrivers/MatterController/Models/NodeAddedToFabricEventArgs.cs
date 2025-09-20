using Org.BouncyCastle.Math;

namespace Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models
{
    public class NodeAddedToFabricEventArgs
    {
        public BigInteger NodeId { get; internal set; }
    }
}
