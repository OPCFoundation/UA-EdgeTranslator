using Matter.Core.Fabrics;
using System.Net.Sockets;

namespace Matter.Core
{
    public class PeerNode
    {
        private UdpClient _listener;

        public PeerNode(Node node)
        {

        }

        public void StartAsync(Node node)
        {
            _listener = new UdpClient();
        }
    }
}
