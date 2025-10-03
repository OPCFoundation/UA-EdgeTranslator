namespace Matter.Core
{
    public class NodeRegisterDetails
    {
        public NodeRegisterDetails(string nodeName, ushort discriminator, ushort port, string[] addresses)
        {
            NodeName = nodeName;
            Discriminator = discriminator;
            Port = port;
            Addresses = addresses;
        }

        public string NodeName { get; set; }

        public ushort Discriminator { get; set; }

        public ushort Port { get; set; }

        public string[] Addresses { get; set; } = [];
    }
}
