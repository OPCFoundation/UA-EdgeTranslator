using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Matter.Core
{
    internal class UdpConnection : IConnection
    {
        private UdpClient _udpClient;
        private Channel<byte[]> _receivedDataChannel = Channel.CreateBounded<byte[]>(500);
        private IPAddress _ipAddress;
        private ushort _port;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        public event EventHandler ConnectionClosed;

        public UdpConnection(IPAddress address, ushort port)
        {
            _ipAddress = address;
            _port = port;

            _cancellationTokenSource = new CancellationTokenSource();

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                _udpClient = new UdpClient(AddressFamily.InterNetwork);
            }
            else if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                _udpClient = new UdpClient(AddressFamily.InterNetworkV6);
            }
        }

        public IConnection OpenConnection()
        {
            _udpClient.Connect(_ipAddress, _port);

            Task.Factory.StartNew(ProcessIncomingData);

            return this;
        }

        public void Close()
        {
            _cancellationTokenSource.Cancel();

            _udpClient!.Close();
            _udpClient = null;
        }

        public bool IsConnectionEstablished => _udpClient != null && _udpClient.Client.Connected;

        public async Task ProcessIncomingData()
        {
            try
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    UdpReceiveResult result = await _udpClient.ReceiveAsync();

                    var bytes = result.Buffer;

                    await _receivedDataChannel.Writer.WriteAsync(bytes);
                }
            }
            catch (Exception)
            {
                Console.WriteLine("UdpConnection: Error receiving data, closing connection.");
                ConnectionClosed?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            try
            {
                return await _receivedDataChannel.Reader.ReadAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public async Task SendAsync(byte[] bytes)
        {
            await _udpClient.SendAsync(bytes, _cancellationTokenSource.Token).ConfigureAwait(false);
        }
    }
}
