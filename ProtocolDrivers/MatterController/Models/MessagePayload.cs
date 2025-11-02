using System;
using System.IO;

namespace Matter.Core
{
    public class MessagePayload
    {
        public ExchangeFlags ExchangeFlags { get; set; }

        public byte ProtocolOpCode { get; set; }

        public ushort ExchangeId { get; set; }

        public ushort ProtocolId { get; set; }

        public uint AcknowledgedMessageCounter { get; set; }

        public MatterTLV ApplicationPayload { get; set; }

        public int HeaderLength { get; set; }

        public MessagePayload(ExchangeFlags exchangeFlags, byte opCode, ushort exchangeId, ushort protocolId, uint ackCounter, ushort securedExtensionsLength, MatterTLV payload)
        {
            ExchangeFlags = exchangeFlags;
            ProtocolOpCode = opCode;
            ExchangeId = exchangeId;

            HeaderLength = 1 + 1 + 2; // ExchangeFlags + ProtocolOpCode + ExchangeId

            if ((ExchangeFlags & ExchangeFlags.VendorPresent) != 0)
            {
                HeaderLength += 2;
            }

            ProtocolId = protocolId;
            HeaderLength += 2; // ProtocolId

            if ((ExchangeFlags & ExchangeFlags.Acknowledgement) != 0)
            {
                AcknowledgedMessageCounter = ackCounter;
                HeaderLength += 4;
            }

            if ((exchangeFlags & ExchangeFlags.SecuredExtensions) != 0)
            {
                HeaderLength += 2; // Length ushort
                HeaderLength += securedExtensionsLength;
            }

            ApplicationPayload = payload;
        }

        public static MessagePayload Deserialize(byte[] messagePayload)
        {
            var index = 0;

            var exchangeFlags = (ExchangeFlags)messagePayload[index];
            index++;

            var protocolOpCode = messagePayload[index];
            index++;

            var exchangeId = BitConverter.ToUInt16(messagePayload, index);
            index += 2;

            if ((exchangeFlags & ExchangeFlags.VendorPresent) != 0)
            {
                index += 2;
            }

            var protocolId = BitConverter.ToUInt16(messagePayload, index);
            index += 2;

            uint acknowledgedMessageCounter = 0;
            if ((exchangeFlags & ExchangeFlags.Acknowledgement) != 0)
            {
                acknowledgedMessageCounter = BitConverter.ToUInt32(messagePayload, index);
                index += 4;
            }

            ushort securedExtensionsLength = 0;
            if ((exchangeFlags & ExchangeFlags.SecuredExtensions) != 0)
            {
                securedExtensionsLength = BitConverter.ToUInt16(messagePayload, index);
                index += 2; // Length ushort
                index += securedExtensionsLength;
            }

            var applicationPayload = new MatterTLV(messagePayload.AsSpan().Slice(index).ToArray());

            return new MessagePayload(exchangeFlags, protocolOpCode, exchangeId, protocolId, acknowledgedMessageCounter, securedExtensionsLength, applicationPayload);
        }

        public byte[] Serialize()
        {
            using MemoryStream writer = new();

            writer.WriteByte((byte)ExchangeFlags);
            writer.WriteByte(ProtocolOpCode);
            writer.Write(BitConverter.GetBytes(ExchangeId));
            writer.Write(BitConverter.GetBytes(ProtocolId));

            if ((ExchangeFlags & ExchangeFlags.Acknowledgement) != 0)
            {
                writer.Write(BitConverter.GetBytes(AcknowledgedMessageCounter));
            }

            if (ApplicationPayload != null)
            {
                writer.Write(ApplicationPayload.Serialize());
            }

            return writer.ToArray();
        }
    }
}
