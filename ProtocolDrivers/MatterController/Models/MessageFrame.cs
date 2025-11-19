namespace Matter.Core
{
    using System;
    using System.IO;

    public class MessageFrame
    {
        public MessageFlags MessageFlags { get; set; }

        public ushort SessionID { get; set; }

        public SecurityFlags SecurityFlags { get; set; }

        public uint MessageCounter { get; set; }

        public ulong SourceNodeID { get; set; }

        public ulong DestinationNodeId { get; set; }

        public ushort DestinationGroupId { get; set; }

        public MessagePayload MessagePayload { get; set; }

        public int HeaderLength { get; set; }

        public MessageFrame(MessageFlags messageFlags, ushort sessionId, SecurityFlags securityFlags, uint messageCounter, ulong sourceNodeId, ulong destinationNodeId, ushort destinationGroupId, MessagePayload messagePayload)
        {
            MessageFlags = messageFlags;
            SessionID = sessionId;
            SecurityFlags = securityFlags;
            MessageCounter = messageCounter;

            HeaderLength = 8; // MessageFlags (1), SessionId (2), SecurityFlags(1), MessageCounter (4)

            if ((MessageFlags & MessageFlags.SourceNodeID) != 0)
            {
                SourceNodeID = sourceNodeId;
                HeaderLength += 8;
            }
            if ((MessageFlags & MessageFlags.DestinationNodeID) != 0)
            {
                DestinationNodeId = destinationNodeId;
                HeaderLength += 8;
            }
            if ((MessageFlags & MessageFlags.DestinationGroupID) != 0)
            {
                DestinationGroupId = destinationGroupId;
                HeaderLength += 2;
            }

            MessagePayload = messagePayload;
        }

        public byte[] Serialize()
        {
            using var writer = new MemoryStream();

            writer.WriteByte((byte)MessageFlags);
            writer.Write(BitConverter.GetBytes(SessionID));
            writer.WriteByte((byte)SecurityFlags);
            writer.Write(BitConverter.GetBytes(MessageCounter));

            if ((MessageFlags & MessageFlags.SourceNodeID) != 0)
            {
                writer.Write(BitConverter.GetBytes(SourceNodeID));
            }

            if ((MessageFlags & MessageFlags.DestinationNodeID) != 0)
            {
                writer.Write(BitConverter.GetBytes(DestinationNodeId));
            }

            if ((MessageFlags & MessageFlags.DestinationGroupID) != 0)
            {
                writer.Write(BitConverter.GetBytes(DestinationGroupId));
            }

            if (MessagePayload != null)
            {
                writer.Write(MessagePayload.Serialize());
            }

            return writer.ToArray();
        }

        public static MessageFrame Deserialize(byte[] messageFrameBytes, bool deserializePayload)
        {
            var messageFlags = (MessageFlags)messageFrameBytes[0];
            var sessionID = BitConverter.ToUInt16(messageFrameBytes, 1);
            var securityFlags = (SecurityFlags)messageFrameBytes[3];
            var messageCounter = BitConverter.ToUInt32(messageFrameBytes, 4);

            int headerLength = 8; // MessageFlags (1), SessionId (2), SecurityFlags(1), MessageCounter (4)

            ulong sourceNodeId = 0;
            if ((messageFlags & MessageFlags.SourceNodeID) != 0)
            {
                // Account for the SourceNodeId (8 bytes)
                sourceNodeId = BitConverter.ToUInt64(messageFrameBytes, headerLength);
                headerLength += 8;
            }

            ulong destinationNodeId = 0;
            if ((messageFlags & MessageFlags.DestinationNodeID) != 0)
            {
                // Account for the DestinationNodeId (64 bit)
                destinationNodeId = BitConverter.ToUInt64(messageFrameBytes, headerLength);
                headerLength += 8;
            }

            ushort destinationGroupId = 0;
            if ((messageFlags & MessageFlags.DestinationGroupID) != 0)
            {
                // Account for the DestinationNodeId (2 bytes)
                destinationGroupId = BitConverter.ToUInt16(messageFrameBytes, headerLength);
                headerLength += 2;
            }

            if ((securityFlags & SecurityFlags.MessageExtensions) != 0)
            {
                Console.WriteLine("Message Extensions present!");
            }

            MessagePayload payload = null;
            if (deserializePayload)
            {
                payload = MessagePayload.Deserialize(messageFrameBytes.AsSpan().Slice(headerLength, messageFrameBytes.Length - headerLength).ToArray());
            }

            return new MessageFrame(messageFlags, sessionID, securityFlags, messageCounter, sourceNodeId, destinationNodeId, destinationGroupId, payload);
        }

        public static bool IsStandaloneAck(MessageFrame messageFrame)
        {
            return (messageFrame.MessagePayload.ProtocolId == 0) && (messageFrame.MessagePayload.OpCode == ProtocolOpCode.Acknowledgement);
        }

        public static bool IsError(MessageFrame messageFrame)
        {
            if (messageFrame == null)
            {
                return true;
            }
            else
            {
                if ((messageFrame.MessagePayload.ProtocolId == 0) && (messageFrame.MessagePayload.OpCode == ProtocolOpCode.StatusReport))
                {
                    // print the status report details
                    Console.WriteLine("Matter Status Report: " + Convert.ToHexString(messageFrame.MessagePayload.ApplicationPayload.Serialize()));
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
    }
}
