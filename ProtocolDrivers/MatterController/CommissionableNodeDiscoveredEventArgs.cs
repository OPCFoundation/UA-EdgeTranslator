namespace Matter.Core
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