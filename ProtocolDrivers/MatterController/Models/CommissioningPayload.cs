namespace Matter.Core
{
    public class CommissioningPayload
    {
        public ushort Discriminator { get; set; }

        public uint Passcode { get;  set; }
    }
}
