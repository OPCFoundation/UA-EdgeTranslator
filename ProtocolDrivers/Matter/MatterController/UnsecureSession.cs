using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Matter.Core
{
    public class UnsecureSession : ISession
    {
        private IConnection _connection;

        public UnsecureSession(IConnection connection)
        {
            _connection = connection;
        }

        public IConnection Connection => _connection;

        public ulong SourceNodeId { get; set; } = 0;

        public ulong DestinationNodeId { get; set; } = 0;

        public ushort SessionId { get; set; } = 0;

        public ushort PeerSessionId { get; } = 0;

        public bool UseMRP { get; set; } = false;

        public uint MessageCounter { get; set; } = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));

        public MessageExchange CreateExchange(ulong sourceNodeId, ulong destinationNodeId)
        {
            SourceNodeId = sourceNodeId;
            DestinationNodeId = destinationNodeId;

            return new MessageExchange(BitConverter.ToUInt16(RandomNumberGenerator.GetBytes(2)), this);
        }

        public async Task SendAsync(byte[] message)
        {
            await _connection.SendAsync(message).ConfigureAwait(false);
        }

        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            return await _connection.ReadAsync(token).ConfigureAwait(false);
        }

        public byte[] Encode(MessageFrame messageFrame)
        {
            return messageFrame.Serialize();
        }

        public MessageFrame Decode(byte[] messageFrameBytes)
        {
            return MessageFrame.Deserialize(messageFrameBytes, true);
        }
    }
}
