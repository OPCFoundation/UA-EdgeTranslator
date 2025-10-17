using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Matter.Core.Sessions
{
    public class CaseSecureSession : ISession
    {
        private readonly IConnection _connection;
        private readonly byte[] _encryptionKey;
        private readonly byte[] _decryptionKey;
        private uint _messageCounter = 0;

        public CaseSecureSession(IConnection connection,
                                 ulong sourceNodeId,
                                 ulong destinationNodeId,
                                 ushort sessionId,
                                 ushort peerSessionId,
                                 byte[] encryptionKey,
                                 byte[] decryptionKey)
        {
            _connection = connection;
            _encryptionKey = encryptionKey;
            _decryptionKey = decryptionKey;

            SourceNodeId = sourceNodeId;
            DestinationNodeId = destinationNodeId;

            SessionId = sessionId;
            PeerSessionId = peerSessionId;

            _messageCounter = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        }

        public void Close()
        {
            _connection.Close();
        }

        public IConnection CreateNewConnection()
        {
            return _connection.OpenConnection();
        }

        public IConnection Connection => _connection;

        public ulong SourceNodeId { get; }

        public ulong DestinationNodeId { get; }

        public ushort SessionId { get; }

        public ushort PeerSessionId { get; }

        public bool UseMRP => true;

        public uint MessageCounter => _messageCounter++;

        public MessageExchange CreateExchange()
        {
            // We're going to Exchange messages in this session, so we need an MessageExchange
            // to track it (4.10).
            //
            using var rng = RandomNumberGenerator.Create();

            var randomBytes = new byte[2];

            rng.GetBytes(randomBytes);
            ushort trueRandom = BitConverter.ToUInt16(randomBytes, 0);

            var exchangeId = trueRandom;

            return new MessageExchange(exchangeId, this);
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
            var parts = new MessageFrameParts(messageFrame);

            var memoryStream = new MemoryStream();
            var nonceWriter = new BinaryWriter(memoryStream);

            nonceWriter.Write((byte)messageFrame.SecurityFlags);
            nonceWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
            nonceWriter.Write(BitConverter.GetBytes(messageFrame.SourceNodeID));

            var nonce = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var additionalDataWriter = new BinaryWriter(memoryStream);

            additionalDataWriter.Write((byte)messageFrame.MessageFlags);
            additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.SessionID));
            additionalDataWriter.Write((byte)messageFrame.SecurityFlags);
            additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
            additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.SourceNodeID));

            var additionalData = memoryStream.ToArray();

            byte[] encryptedPayload = new byte[parts.MessagePayload.Length];
            byte[] tag = new byte[16];

            var encryptor = new AesCcm(_encryptionKey);
            encryptor.Encrypt(nonce, parts.MessagePayload, encryptedPayload, tag, additionalData);

            var totalPayload = encryptedPayload.Concat(tag);

            return parts.Header.Concat(totalPayload).ToArray();
        }

        public MessageFrame Decode(MessageFrameParts parts)
        {
            // Run this through the decoder. We need to start reading the bytes until we
            // get to the payload. We then need to decrypt the payload.

            var messageFrame = parts.MessageFrameWithHeaders();
            var memoryStream = new MemoryStream();
            var nonceWriter = new BinaryWriter(memoryStream);

            nonceWriter.Write((byte)messageFrame.SecurityFlags);
            nonceWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));

            // We are receiving a message from the other node.
            // The MessageFrame might not have the SourceNodeId in it, as its not always sent.
            nonceWriter.Write(BitConverter.GetBytes(DestinationNodeId));

            var nonce = memoryStream.ToArray();
            memoryStream = new MemoryStream();
            var additionalDataWriter = new BinaryWriter(memoryStream);

            additionalDataWriter.Write((byte)messageFrame.MessageFlags);
            additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.SessionID));
            additionalDataWriter.Write((byte)messageFrame.SecurityFlags);
            additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
            additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.DestinationNodeId));

            var additionalData = memoryStream.ToArray();
            byte[] decryptedPayload = new byte[parts.MessagePayload.Length - 16];

            var tag = parts.MessagePayload.AsSpan().Slice(parts.MessagePayload.Length - 16, 16);
            var encryptedPayload = parts.MessagePayload.AsSpan().Slice(0, parts.MessagePayload.Length - 16);

            var encryptor = new AesCcm(_decryptionKey);
            encryptor.Decrypt(nonce, encryptedPayload, tag, decryptedPayload, additionalData);

            messageFrame.MessagePayload = new MessagePayload(decryptedPayload);

            return messageFrame;
        }
    }
}
