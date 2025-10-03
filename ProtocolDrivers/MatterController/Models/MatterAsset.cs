namespace Matter.Core
{
    public class MatterAsset
    {
        public System.Net.IPAddress IPAddress { get; set; }

        public ushort Port { get; set; }

        public Matter.Core.Node Node { get; set; }

        public uint Passcode { get; set; }
    }
}
