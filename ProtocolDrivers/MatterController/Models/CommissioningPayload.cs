namespace Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models
{
    public class CommissioningPayload
    {
        public ushort Discriminator { get; set; }

        public uint Passcode { get;  set; }
    }
}
