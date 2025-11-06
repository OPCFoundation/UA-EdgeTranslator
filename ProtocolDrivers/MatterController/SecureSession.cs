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

        public SecureSession(IConnection connection, ushort sessionId, ushort peerSessionId, byte[] encryptionKey, byte[] decryptionKey)
        {
            _connection = connection;

            SessionId = sessionId;
            PeerSessionId = peerSessionId;

            _encryptionKey = encryptionKey;
            _decryptionKey = decryptionKey;
        }

        public IConnection Connection => _connection;

        public ulong SourceNodeId { get; set; } = 0x00;

        public ulong DestinationNodeId { get; set; } = 0x00;

        public ushort SessionId { get; }

        public ushort PeerSessionId { get; }

        public bool UseMRP => true;

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
            byte[] messageFrameUnencrypted = messageFrame.Serialize();

            byte[] nonce;
            using (var memoryStream = new MemoryStream())
            {
                using (var nonceWriter = new BinaryWriter(memoryStream))
                {
                    nonceWriter.Write((byte)messageFrame.SecurityFlags);
                    nonceWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
                    nonceWriter.Write(BitConverter.GetBytes(SourceNodeId));
                    nonce = memoryStream.ToArray();
                }
            }

            byte[] associatedData;
            using (var memoryStream = new MemoryStream())
            {
                using (var associatedDataWriter = new BinaryWriter(memoryStream))
                {
                    associatedDataWriter.Write((byte)messageFrame.MessageFlags);
                    associatedDataWriter.Write(BitConverter.GetBytes(messageFrame.SessionID));
                    associatedDataWriter.Write((byte)messageFrame.SecurityFlags);
                    associatedDataWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
                    associatedDataWriter.Write(BitConverter.GetBytes(SourceNodeId));
                    associatedData = memoryStream.ToArray();
                }
            }

            byte[] unencryptedPayload = messageFrame.MessagePayload.Serialize();
            byte[] encryptedPayload = new byte[unencryptedPayload.Length];
            byte[] tag = new byte[16];
            var encryptor = new AesCcm(_encryptionKey);
            encryptor.Encrypt(nonce, unencryptedPayload, encryptedPayload, tag, associatedData);

            // concat the header and the encrypted payload with tag and return
            return messageFrameUnencrypted.Take(messageFrame.HeaderLength).Concat(encryptedPayload.Concat(tag).ToArray()).ToArray();
        }

        public MessageFrame Decode(byte[] messageFrameBytes)
        {
            MessageFrame messageFrame = MessageFrame.Deserialize(messageFrameBytes, false);

            byte[] nonce;
            using (var memoryStream = new MemoryStream())
            {
                using (var nonceWriter = new BinaryWriter(memoryStream))
                {
                    nonceWriter.Write((byte)messageFrame.SecurityFlags);
                    nonceWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
                    nonceWriter.Write(BitConverter.GetBytes(DestinationNodeId));
                    nonce = memoryStream.ToArray();
                }
            }

            byte[] associatedData;
            using (var memoryStream = new MemoryStream())
            {
                using (var associatedDataWriter = new BinaryWriter(memoryStream))
                {
                    associatedDataWriter.Write((byte)messageFrame.MessageFlags);
                    associatedDataWriter.Write(BitConverter.GetBytes(messageFrame.SessionID));
                    associatedDataWriter.Write((byte)messageFrame.SecurityFlags);
                    associatedDataWriter.Write(BitConverter.GetBytes(messageFrame.MessageCounter));
                    associatedData = memoryStream.ToArray();
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
                encryptor.Decrypt(nonce, encryptedPayload, tag, decryptedPayload, associatedData);

                messageFrame.MessagePayload = MessagePayload.Deserialize(decryptedPayload);

                return messageFrame;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Decryption failed - {0}", ex.Message);
                throw;
            }
        }
    }
}
