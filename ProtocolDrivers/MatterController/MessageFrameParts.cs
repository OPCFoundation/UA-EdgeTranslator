namespace Matter.Core
{
    using System;

    public class MessageFrameParts
    {
        public MessageFrameParts(MessageFrame messageFrame)
        {
            var headerWriter = new MatterMessageWriter();

            headerWriter.Write((byte)messageFrame.MessageFlags);
            headerWriter.Write(messageFrame.SessionID);
            headerWriter.Write((byte)messageFrame.SecurityFlags);
            headerWriter.Write(messageFrame.MessageCounter);

            if ((messageFrame.MessageFlags & MessageFlags.S) != 0)
            {
                headerWriter.Write(messageFrame.SourceNodeID);
            }

            if ((messageFrame.MessageFlags & MessageFlags.DSIZ1) != 0)
            {
                headerWriter.Write(messageFrame.DestinationNodeId);
            }

            if ((messageFrame.MessageFlags & MessageFlags.DSIZ2) != 0)
            {
                // TODO Don't know if this is needed?
                headerWriter.Write(messageFrame.DestinationNodeId);
            }

            Header = headerWriter.GetBytes();

            var payloadWriter = new MatterMessageWriter();
            messageFrame.MessagePayload.Serialize(payloadWriter);

            MessagePayload = payloadWriter.GetBytes();
        }

        public MessageFrameParts(byte[] messageFrameBytes)
        {
            Console.WriteLine("┌─────────────────────────────────── {0} ──────────────────────────────────────────\n│ {1}\n└──────────────────────────────────────────────────────────────────────────────", messageFrameBytes.Length, BitConverter.ToString(messageFrameBytes));

            var messageFlags = (MessageFlags)messageFrameBytes[0];
            var SessionID = BitConverter.ToUInt16(messageFrameBytes, 1);
            var SecurityFlags = (SecurityFlags)messageFrameBytes[3];
            var MessageCounter = BitConverter.ToUInt32(messageFrameBytes, 4);

            var headerLength = 8; // MessageFlags (1), SessionId (2), SecurityFlags(1), MessageCounter (4)

            if ((messageFlags & MessageFlags.S) != 0)
            {
                // Account for the SourceNodeId (8 bytes)
                headerLength += 8;
            }

            if ((messageFlags & MessageFlags.DSIZ0) != 0)
            {
                // Account for the DestinationNodeId (0 bytes)
                headerLength += 0;
            }

            if ((messageFlags & MessageFlags.DSIZ1) != 0)
            {
                // Account for the DestinationNodeId (64 bit)
                headerLength += 8;
            }

            if ((messageFlags & MessageFlags.DSIZ2) != 0)
            {
                // Account for the DestinationNodeId (2 bytes)
                // It's a GroupId
                headerLength += 2;
            }

            if ((SecurityFlags & SecurityFlags.MessageExtensions) != 0)
            {
                Console.WriteLine("Message Extensions present!!!!");
            }

            var messageHeader = new byte[headerLength];

            var messagePayloadLength = messageFrameBytes.Length - headerLength;
            var messagePayload = new byte[messagePayloadLength];

            Array.Copy(messageFrameBytes, 0, messageHeader, 0, headerLength);
            Array.Copy(messageFrameBytes, headerLength, messagePayload, 0, messagePayloadLength);

            Header = messageHeader;
            MessagePayload = messagePayload;
        }

        public byte[] Header { get; set; }

        public byte[] MessagePayload { get; set; }

        internal MessageFrame MessageFrameWithHeaders()
        {
            var messageFrame = new MessageFrame();

            messageFrame.MessageFlags = (MessageFlags)Header[0];
            messageFrame.SessionID = BitConverter.ToUInt16(Header, 1);
            messageFrame.SecurityFlags = (SecurityFlags)Header[3];
            messageFrame.MessageCounter = BitConverter.ToUInt32(Header, 4);

            var headerIndex = 8;

            if ((messageFrame.MessageFlags & MessageFlags.S) != 0)
            {
                // Process for the SourceNodeId (8 bytes)
                messageFrame.SourceNodeID = BitConverter.ToUInt64(Header, 8);
                headerIndex += 8;
            }

            if ((messageFrame.MessageFlags & MessageFlags.DSIZ1) != 0)
            {
                // Process the DestinationId
                messageFrame.DestinationNodeId = BitConverter.ToUInt64(Header, headerIndex);
                headerIndex += 8;
            }

            if ((messageFrame.MessageFlags & MessageFlags.DSIZ2) != 0)
            {
                // Process the DestinationId as a GroupId
                // TODO Handle Groups!
                //messageFrame.SourceNodeID = BitConverter.ToUInt16(Header, headerIndex);
                headerIndex += 2;
            }

            // Return an instance of the MessageFrame with just the headers populated from the parts.
            //
            return messageFrame;
        }
    }
}
