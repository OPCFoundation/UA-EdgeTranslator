using Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Matter.Core
{
    internal class NodeRegister : INodeRegister
    {
        private readonly ConcurrentDictionary<string, string[]> _commissionedNodes = new();
        private readonly ConcurrentDictionary<string, NodeRegisterDetails> _commissionalbleNodes = new();

        public event INodeRegister.CommissionableNodeDiscovered CommissionableNodeDiscoveredEvent;

        public void AddCommissionableNode(string nodeIdAndCompressedFabricIdentifier, ushort discriminator, ushort port, string[] addresses)
        {
            _commissionalbleNodes.AddOrUpdate(nodeIdAndCompressedFabricIdentifier, new NodeRegisterDetails(nodeIdAndCompressedFabricIdentifier, discriminator, port, addresses), (key, oldValue) => new NodeRegisterDetails(nodeIdAndCompressedFabricIdentifier, discriminator, port, addresses));

            CommissionableNodeDiscoveredEvent(this, new CommissionableNodeDiscoveredEventArgs(nodeIdAndCompressedFabricIdentifier));
        }

        public void AddCommissionedNode(string nodeIdAndCompressedFabricIdentifier, ushort port, string[] addresses)
        {
            _commissionedNodes.AddOrUpdate(nodeIdAndCompressedFabricIdentifier, addresses, (key, oldValue) => addresses);
        }

        public Task<NodeRegisterDetails> GetCommissionableNodeForDiscriminatorAsync(ushort discriminator)
        {
            foreach (var commissionableNode in _commissionalbleNodes)
            {
                if (commissionableNode.Value.Discriminator >> 12 == discriminator >> 12)
                {
                    return Task.FromResult(commissionableNode.Value);
                }
            }

            return Task.FromResult<NodeRegisterDetails>(null);
        }

        public string[] GetCommissionedNodeAddresses(string nodeIdAndCompressedFabricIdentifier)
        {
            if (_commissionedNodes.TryGetValue(nodeIdAndCompressedFabricIdentifier, out var addresses))
            {
                return addresses;
            }

            return Array.Empty<string>();
        }
    }
}
