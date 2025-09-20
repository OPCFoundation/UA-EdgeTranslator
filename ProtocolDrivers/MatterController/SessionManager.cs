using Matter.Core.Fabrics;
using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Matter.Core.Sessions
{
    public class SessionManager : ISessionManager
    {
        private Dictionary<Node, ISession> _secureSessions = new();
        private Channel<Node> _connectionsQueue = Channel.CreateUnbounded<Node>();
        //private readonly INodeRegister _nodeRegister;

        public SessionManager()
        {
            //_nodeRegister = nodeRegister ?? throw new ArgumentNullException(nameof(nodeRegister));
        }

        public ISession GetSecureSession(Node node)
        {
            return _secureSessions[node];
        }

        public async Task Start(Fabric fabric)
        {
            foreach (var node in fabric.Nodes)
            {
                await _connectionsQueue.Writer.WriteAsync(node);
            }

            while (true)
            {
                Console.WriteLine($"Waiting for a node that we need connect to");

                var nodeNeedingConnection = await _connectionsQueue.Reader.ReadAsync();

                try
                {
                    var fullNodeName = nodeNeedingConnection.Fabric.GetFullNodeName(nodeNeedingConnection);
                    Console.WriteLine($"Attempting to connect to node {fullNodeName}...");

                    await nodeNeedingConnection.Connect();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to connect to node {nodeNeedingConnection.NodeName}: {ex.Message}");
                }
            }
        }
    }
}
