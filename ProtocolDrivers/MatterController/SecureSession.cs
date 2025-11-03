using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Matter.Core
{
    public class SecureSession : ISession
    {
        private readonly IConnection _connection;
        private readonly byte[] _encryptionKey;
        private readonly byte[] _decryptionKey;
        private uint _messageCounter = 0;

        public SecureSession(IConnection connection, ushort sessionId, ushort peerSessionId, byte[] encryptionKey, byte[] decryptionKey)
        {
            _connection = connection;
            _messageCounter = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));

            _encryptionKey = encryptionKey;
            _decryptionKey = decryptionKey;

            SessionId = sessionId;
            PeerSessionId = peerSessionId;
        }

        public IConnection Connection => _connection;

        public ulong SourceNodeId { get; } = 0x00;

        public ulong DestinationNodeId { get; } = 0x00;

        public ushort SessionId { get; }

        public ushort PeerSessionId { get; }

        public bool UseMRP => true;

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
            byte[] messageFrameUnencrypted = messageFrame.Serialize();

            byte[] nonce;
            using (var memoryStream = new MemoryStream())
            {
                using (var nonceWriter = new BinaryWriter(memoryStream))
                {
                    nonceWriter.Write((byte)messageFrame.SecurityFlags);
                    nonceWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
                    nonceWriter.Write(BitConverter.GetBytes(messageFrame.SourceNodeID));
                    nonce = memoryStream.ToArray();
                }
            }

            byte[] additionalData;
            using (var memoryStream = new MemoryStream())
            {
                using (var additionalDataWriter = new BinaryWriter(memoryStream))
                {
                    additionalDataWriter.Write((byte)messageFrame.MessageFlags);
                    additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.SessionID));
                    additionalDataWriter.Write((byte)messageFrame.SecurityFlags);
                    additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
                    additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.SourceNodeID));
                    additionalData = memoryStream.ToArray();
                }
            }

            byte[] unencryptedPayload = messageFrame.MessagePayload.Serialize();
            byte[] encryptedPayload = new byte[unencryptedPayload.Length];
            byte[] tag = new byte[16];
            var encryptor = new AesCcm(_encryptionKey);
            encryptor.Encrypt(nonce, unencryptedPayload, encryptedPayload, tag, additionalData);

            // concat the header and the encrypted payload with tag and return
            return messageFrameUnencrypted.Take(messageFrame.HeaderLength).Concat(encryptedPayload.Concat(tag).ToArray()).ToArray();
        }

        public MessageFrame Decode(byte[] messageFrameBytes)
        {
            MessageFrame messageFrame = MessageFrame.Deserialize(messageFrameBytes, false);

            byte[] nonce;
            byte[] additionalData;

            using (var memoryStream = new MemoryStream())
            {
                using (var nonceWriter = new BinaryWriter(memoryStream))
                {
                    nonceWriter.Write((byte)messageFrame.SecurityFlags);
                    nonceWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
                    nonceWriter.Write(BitConverter.GetBytes(messageFrame.SourceNodeID));
                    nonce = memoryStream.ToArray();
                }
            }

            using (var memoryStream = new MemoryStream())
            {
                using (var additionalDataWriter = new BinaryWriter(memoryStream))
                {
                    additionalDataWriter.Write((byte)messageFrame.MessageFlags);
                    additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.SessionID));
                    additionalDataWriter.Write((byte)messageFrame.SecurityFlags);
                    additionalDataWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
                    additionalData = memoryStream.ToArray();
                }
            }

            // check if we have something to decode
            if (messageFrameBytes.Length <= messageFrame.HeaderLength + 16)
            {
                // return the message with the payload as is
                return MessageFrame.Deserialize(messageFrameBytes, true);
            }

            var encryptedPayload = messageFrameBytes.AsSpan().Slice(messageFrame.HeaderLength, messageFrameBytes.Length - messageFrame.HeaderLength - 16);
            var tag = messageFrameBytes.AsSpan().Slice(messageFrame.HeaderLength + encryptedPayload.Length, 16);

            try
            {
                byte[] decryptedPayload = new byte[encryptedPayload.Length];
                var encryptor = new AesCcm(_decryptionKey);
                encryptor.Decrypt(nonce, encryptedPayload, tag, decryptedPayload, additionalData);

                messageFrame.MessagePayload = MessagePayload.Deserialize(decryptedPayload);

                return messageFrame;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Decryption failed - {0}", ex.Message);

                // return the message with the payload as is
                return MessageFrame.Deserialize(messageFrameBytes, true);
            }
        }
    }
}
