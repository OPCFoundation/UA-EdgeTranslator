
using System;
using System.IO;

namespace Matter.Core
{
    public class BTPFrame
    {
        public BTPFrame(byte[] payload)
        {
            int index = 0;
            ControlFlags = (BTPFlags)payload[index++];
            bool isHandshake = (ControlFlags & BTPFlags.Handshake) != 0;
            bool isManagement = (ControlFlags & BTPFlags.Management) != 0;
            bool isAcknowledgement = (ControlFlags & BTPFlags.Acknowledge) != 0;
            bool isBeginningSegment = (ControlFlags & BTPFlags.Beginning) != 0;

            if (isManagement)
            {
                index++; // skip opcode
                Version = (byte)(payload[index++] & 0xF);
            }

            if (isAcknowledgement)
            {
                AcknowledgeNumber = payload[index++];
            }

            if (isHandshake)
            {
                ATTSize = BitConverter.ToUInt16(payload, index);
                index += 2;
                WindowSize = payload[index++];
            }
            else
            {
                Sequence = payload[index++];

                if (isBeginningSegment)
                {
                    MessageLength = BitConverter.ToUInt16(payload, index);
                    index += 2;
                }

                Payload = new byte[payload.Length - index];
                Array.Copy(payload, index, Payload, 0, payload.Length - index);
            }
        }

        public BTPFlags ControlFlags { get; set; }

        public byte[] Payload { get; set; }

        public ushort MessageLength { get; set; }

        public byte AcknowledgeNumber { get; set; }

        public byte Sequence { get; set; }

        public ushort Version { get; }

        public ushort ATTSize { get; }

        public ushort WindowSize { get; }

        internal void Serialize(MemoryStream writer)
        {
            writer.WriteByte((byte)ControlFlags);

            // If this is an acknowledge message, send the number we're acknowldgeing.
            if ((ControlFlags & BTPFlags.Acknowledge) != 0)
            {
                writer.WriteByte(AcknowledgeNumber);
            }

            // If this isn't a handshake, include the sequence.
            if ((ControlFlags & BTPFlags.Handshake) == 0)
            {
                writer.WriteByte(Sequence);
            }

            // If this is a Beginning message, we need to include the MessageLength.
            if ((ControlFlags & BTPFlags.Beginning) != 0)
            {
                writer.Write(BitConverter.GetBytes(MessageLength));
            }

            writer.Write(Payload);
        }
    }
}
