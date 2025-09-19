namespace Matter.Core.Commissioning
{
    public class CommissioningPayload
    {
        public ushort Discriminator { get; set; }

        public uint Passcode { get;  set; }
    }
}