namespace Matter.Core
{
    using System.Threading.Tasks;

    public interface INodeRegister
    {
        delegate void CommissionableNodeDiscovered(object sender, CommissionableNodeDiscoveredEventArgs e);
        event CommissionableNodeDiscovered CommissionableNodeDiscoveredEvent;

        void AddCommissionedNode(string nodeIdAndCompressedFabricIdentifier, ushort port, string[] addresses);

        void AddCommissionableNode(string nodeIdAndCompressedFabricIdentifier, ushort discriminator, ushort port, string[] addresses);

        string[] GetCommissionedNodeAddresses(string nodeIdAndCompressedFabricIdentifier);

        Task<NodeRegisterDetails> GetCommissionableNodeForDiscriminatorAsync(ushort discriminator);
    }
}
