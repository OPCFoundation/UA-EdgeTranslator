using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Matter.Core
{
    public class UnsecureSession : ISession
    {
        private IConnection _connection;

        private static uint _messageCounter = 0;

        public UnsecureSession(IConnection connection)
        {
            _connection = connection;
            _messageCounter = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        }

        public IConnection Connection => _connection;

        public ulong SourceNodeId { get; } = 0;

        public ulong DestinationNodeId { get; } = 0;

        public ushort SessionId { get; set; } = 0;

        public ushort PeerSessionId { get; } = 0;

        public bool UseMRP => false;

        public uint MessageCounter => _messageCounter++;

        public MessageExchange CreateExchange()
        {
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
            MessageFrameParts parts = new(messageFrame);
            return parts.Header.Concat(parts.MessagePayload).ToArray();
        }

        public MessageFrame Decode(MessageFrameParts parts)
        {
            MessageFrame messageFrame = parts.MessageFrameWithHeaders();
            messageFrame.MessagePayload = new MessagePayload(parts.MessagePayload);
            return messageFrame;
        }
    }
}
