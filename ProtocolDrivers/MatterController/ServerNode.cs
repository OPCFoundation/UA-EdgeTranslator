using System.Net.Sockets;

namespace Matter.Core
{
    public class ServerNode
    {
        private UdpClient _listener;

        public ServerNode()
        {

        }

        public void StartAsync()
        {
            _listener = new UdpClient();
        }
    }
}
