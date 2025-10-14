
using System;

namespace Matter.Core
{
    public class BTPFrame
    {
        public BTPFrame(byte[] readData)
        {
            // Check the ControlFlags.
            ControlFlags = (BTPFlags)readData[0];
            bool isHandshake = ((byte)ControlFlags & 0x20) != 0;
            bool isManagement = ((byte)ControlFlags & 0x10) != 0;
            bool isAcknowledgement = ((byte)ControlFlags & 0x8) != 0;
            bool isEndingSegment = ((byte)ControlFlags & 0x4) != 0;
            bool isContinuingSegment = ((byte)ControlFlags & 0x2) != 0;
            bool isBeginningSegment = ((byte)ControlFlags & 0x1) != 0;

            if (isHandshake)
            {
                Version = readData[2];
                ATTSize = BitConverter.ToUInt16(readData, 3);
                WindowSize = readData[5];
                return;
            }

            int offset = 1;

            if (isManagement)
            {
                // TODO Grab the Management OpCode.
                offset++;
            }

            if (isBeginningSegment)
            {
                MessageLength = BitConverter.ToUInt16(readData, offset);
                offset += 2;
            }

            if (isAcknowledgement)
            {
                AcknowledgeNumber = readData[offset];
                offset++;
            }

            if (isBeginningSegment || isContinuingSegment || isEndingSegment)
            {
                Sequence = readData[offset];

                offset++;

                Payload = new byte[readData.Length - offset];

                Array.Copy(readData, offset, Payload, 0, readData.Length - offset);
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

        internal void Serialize(MatterMessageWriter writer)
        {
            writer.Write((byte)ControlFlags);

            // If this is an acknowledge message, send the number we're acknowldgeing.
            if ((ControlFlags & BTPFlags.Acknowledge) != 0)
            {
                writer.Write(AcknowledgeNumber);
            }

            // If this isn't a handshake, include the sequence.
            if ((ControlFlags & BTPFlags.Handshake) == 0)
            {
                writer.Write(Sequence);
            }

            // If this is a Beginning message, we need to include the MessageLength.
            if ((ControlFlags & BTPFlags.Beginning) != 0)
            {
                writer.Write(MessageLength);
            }

            writer.Write(Payload);
        }
    }
}
