namespace Matter.Core
{
    using System;

    public class MessageFrame
    {
        public MessageFrame()
        {
        }

        public MessageFrame(MessagePayload messagePayload)
        {
            MessagePayload = messagePayload;
        }

        public MessageFlags MessageFlags { get; set; }

        public ushort SessionID { get; set; }

        public SecurityFlags SecurityFlags { get; set; }

        public uint MessageCounter { get; set; }

        public ulong SourceNodeID { get; set; }

        public ulong DestinationNodeId { get; set; }

        public MessagePayload MessagePayload { get; set; }

        public byte[] EncryptedMessagePayload { get; set; }

        internal static bool IsStandaloneAck(MessageFrame messageFrame)
        {
            return messageFrame.MessagePayload.ProtocolId == 0x00 &&
                   messageFrame.MessagePayload.ProtocolOpCode == 0x10;
        }

        internal static bool IsStatusReport(MessageFrame messageFrame)
        {
            return messageFrame.MessagePayload.ProtocolId == 0x00 &&
                   messageFrame.MessagePayload.ProtocolOpCode == 0x40;
        }
    }
}
