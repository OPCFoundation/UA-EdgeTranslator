using Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Matter.Core.Sessions
{
    public class UnsecureSession : ISession
    {
        private IConnection _connection;

        private static uint _messageCounter = 0;

        public UnsecureSession(IConnection connection)
        {
            _connection = connection;
            _messageCounter = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));

            SessionId = 0x00;
            PeerSessionId = 0x00;
        }

        public IConnection Connection => _connection;

        public IConnection CreateNewConnection()
        {
            return _connection.OpenConnection();
        }

        public ulong SourceNodeId { get; } = 0x00;

        public ulong DestinationNodeId { get; } = 0x00;

        public ushort SessionId { get; set; }

        public ushort PeerSessionId { get; }

        public bool UseMRP => false;

        public uint MessageCounter => _messageCounter++;

        public void Close()
        {
            _connection.Close();
        }

        public MessageExchange CreateExchange()
        {
            // We're going to Exchange messages in this session, so we need an MessageExchange
            // to track it (4.10).
            //
            // TODO Ensure the ExchangeId is unique!
            //
            var randomBytes = new byte[2];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);

                ushort trueRandom = BitConverter.ToUInt16(randomBytes, 0);

                var exchangeId = trueRandom;

                Console.WriteLine($"Created Unsecure Exchange ID: {exchangeId}");

                return new MessageExchange(exchangeId, this);
            }
        }

        public async Task SendAsync(byte[] message)
        {
            await _connection.SendAsync(message);
        }

        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            return await _connection.ReadAsync(token);
        }

        public byte[] Encode(MessageFrame messageFrame)
        {
            var parts = new MessageFrameParts(messageFrame);
            return parts.Header.Concat(parts.MessagePayload).ToArray();
        }

        public MessageFrame Decode(MessageFrameParts parts)
        {
            var messageFrame = parts.MessageFrameWithHeaders();
            messageFrame.MessagePayload = new MessagePayload(parts.MessagePayload);
            return messageFrame;
        }
    }
}
