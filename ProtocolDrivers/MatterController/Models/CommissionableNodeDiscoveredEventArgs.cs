namespace Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models
{
    public class CommissionableNodeDiscoveredEventArgs
    {
        public CommissionableNodeDiscoveredEventArgs(string discriminator)
        {
            Discriminator = discriminator;
        }

        public string Discriminator { get; }
    }
}
