using Matter.Core.TLV;
using System;

namespace Matter.Core
{
    public class MessagePayload
    {
        public MessagePayload()
        {
            ApplicationPayload = null;
        }

        public MessagePayload(MatterTLV payload)
        {
            ApplicationPayload = payload;
        }

        public MessagePayload(byte[] messagePayload)
        {
            var index = 0;

            ExchangeFlags = (ExchangeFlags)messagePayload[index];
            index++;

            ProtocolOpCode = messagePayload[index];
            index++;

            ExchangeID = BitConverter.ToUInt16(messagePayload, index);
            index += 2;

            if ((ExchangeFlags & ExchangeFlags.VendorPresent) != 0)
            {
                // TODO Store the Protocol Vendor ID value if present.
                index += 2;
            }

            ProtocolId = BitConverter.ToUInt16(messagePayload, index);
            index += 2;

            if ((ExchangeFlags & ExchangeFlags.Acknowledgement) != 0)
            {
                AcknowledgedMessageCounter = BitConverter.ToUInt32(messagePayload, index);
                index += 4;
            }

            if ((ExchangeFlags & ExchangeFlags.SecuredExtensions) != 0)
            {
                var securedExtensionsLength = BitConverter.ToUInt16(messagePayload, index);
                index += 2; // Length ushort
                index += securedExtensionsLength;
            }

            try
            {
                ApplicationPayload = new MatterTLV(messagePayload.AsSpan().Slice(index).ToArray());
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("Error extracting ApplicationPayload from MessagePayload: {0}", ExchangeFlags);
            }
        }

        public ExchangeFlags ExchangeFlags { get; set; }

        public byte ProtocolOpCode { get; set; }

        public ushort ExchangeID { get; set; }

        public ushort ProtocolId { get; set; }

        public uint AcknowledgedMessageCounter { get; set; }

        public MatterTLV ApplicationPayload { get; set; }

        internal void Serialize(MatterMessageWriter writer)
        {
            writer.Write((byte)ExchangeFlags);
            writer.Write(ProtocolOpCode);
            writer.Write(ExchangeID);
            writer.Write(ProtocolId);

            if ((ExchangeFlags & ExchangeFlags.Acknowledgement) != 0)
            {
                writer.Write(AcknowledgedMessageCounter);
            }

            // Write the bytes of the payload!
            //
            if (ApplicationPayload is not null)
            {
                ApplicationPayload.Serialize(writer);
            }
        }
    }
}
