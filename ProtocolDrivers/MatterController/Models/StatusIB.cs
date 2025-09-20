namespace Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models
{
    public class StatusIB
    {
        public StatusIB(int tag, MatterTLV payload)
        {
            payload.OpenStructure(tag);

            Status = payload.GetUnsignedInt8(0);
            ClusterStatus = payload.GetUnsignedInt8(1);

            payload.CloseContainer();
        }

        public byte Status { get; }

        public byte ClusterStatus { get; }
    }
}
