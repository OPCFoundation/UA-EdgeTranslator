using Matter.Core.Events;
using Org.BouncyCastle.Math;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Matter.Core
{
    public interface IMatterController
    {
        delegate void MatterNodeAddedToFabric(object sender, MatterNodeAddedToFabricEventArgs e);
        event MatterNodeAddedToFabric MatterNodeAddedToFabricEvent;

        //delegate void ReconnectedToNode(object sender, Node node);
        //event ReconnectedToNode ReconnectedToNodeEvent;

        delegate void CommissionableNodeDiscovered(object sender);
        event CommissionableNodeDiscovered CommissionableNodeDiscoveredEvent;

        Task InitAsync();

        Task<ICommissioner> CreateCommissionerAsync();

        Task<IEnumerable<Node>> GetNodesAsync();

        Task<Node> GetNodeAsync(BigInteger nodeId);

        Task RunAsync();
    }
}
