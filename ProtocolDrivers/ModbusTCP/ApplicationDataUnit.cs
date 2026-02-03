namespace Opc.Ua.Edge.Translator.Models
{
    using System;

    public class ApplicationDataUnit
    {
        public const uint maxADU = 260;
        public const int headerLength = 8;

        public ushort TransactionID;

        // protocol is always 0 for Modbus
        public ushort ProtocolID = 0;

        public ushort Length;

        public byte UnitID;

        public byte FunctionCode;

        public byte[] Payload = new byte[maxADU - headerLength];

        public void CopyADUToNetworkBuffer(byte[] buffer)
        {
            if (buffer.Length < maxADU)
            {
                throw new ArgumentException("buffer must be at least " + maxADU.ToString() + " bytes long");
            }

            buffer[0] = (byte)(TransactionID >> 8);
            buffer[1] = (byte)(TransactionID & 0x00FF);

            buffer[2] = (byte)(ProtocolID >> 8);
            buffer[3] = (byte)(ProtocolID & 0x00FF);

            buffer[4] = (byte)(Length >> 8);
            buffer[5] = (byte)(Length & 0x00FF);

            buffer[6] = UnitID;

            buffer[7] = FunctionCode;

            Payload.CopyTo(buffer, 8);
        }

        public void CopyHeaderFromNetworkBuffer(byte[] buffer)
        {
            if (buffer.Length < headerLength)
            {
                throw new ArgumentException("buffer must be at least " + headerLength.ToString() + " bytes long");
            }

            TransactionID |= (ushort)(buffer[0] << 8);
            TransactionID = buffer[1];

            ProtocolID |= (ushort)(buffer[2] << 8);
            ProtocolID = buffer[3];

            Length = (ushort)(buffer[4] << 8);
            Length = buffer[5];

            UnitID = buffer[6];

            FunctionCode = buffer[7];
        }
    }
}
