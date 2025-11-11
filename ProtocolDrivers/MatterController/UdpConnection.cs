using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Matter.Core
{
    internal class UdpConnection : IConnection
    {
        private UdpClient _udpClient;
        private IPAddress _ipAddress;
        private ushort _port;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public UdpConnection(IPAddress address, ushort port)
        {
            _ipAddress = address;
            _port = port;

            _cancellationTokenSource = new CancellationTokenSource();
            _udpClient = new UdpClient(address.AddressFamily);
        }

        public IConnection OpenConnection()
        {
            _udpClient.Connect(_ipAddress, _port);
            return this;
        }

        public void Close()
        {
            _cancellationTokenSource.Cancel();

            _udpClient.Close();
            _udpClient = null;
        }

        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            try
            {
                UdpReceiveResult result = await _udpClient.ReceiveAsync(token).ConfigureAwait(false);
                return result.Buffer;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
                Console.WriteLine("UdpConnection: Error receiving data, closing connection.");
                _udpClient.Close();
                _udpClient = null;
                return null;
            }
        }

        public async Task SendAsync(byte[] bytes)
        {
            await _udpClient.SendAsync(bytes, _cancellationTokenSource.Token).ConfigureAwait(false);
        }
    }
}
