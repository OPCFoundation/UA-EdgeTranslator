namespace Matter.Core
{
    using Matter.Core.Fabrics;
    using Matter.Core.Sessions;
    using Matter.Core.TLV;
    using System;
    using System.Threading.Tasks;

    public class CommandSender
    {
        public static async Task<MessageExchange> SendCommandOff(MatterAsset asset, Fabric fabric, ISession caseSession)
        {
            var caseExchange = caseSession.CreateExchange();

            var offCommandPayload = new MatterTLV();
            offCommandPayload.AddStructure();
            offCommandPayload.AddBool(0, false);
            offCommandPayload.AddBool(1, false);
            offCommandPayload.AddArray(tagNumber: 2); // InvokeRequests
            offCommandPayload.AddStructure();
            offCommandPayload.AddList(tagNumber: 0); // CommandPath
            offCommandPayload.AddUInt16(tagNumber: 0, 0x01); // Endpoint 0x01
            offCommandPayload.AddUInt32(tagNumber: 1, 0x06); // ClusterId 0x06 - OnOff
            offCommandPayload.AddUInt16(tagNumber: 2, 0x00); // 1.5.7 Command Off
            offCommandPayload.EndContainer();
            offCommandPayload.AddStructure(1); // CommandFields
            offCommandPayload.EndContainer(); // Close the CommandFields
            offCommandPayload.EndContainer(); // Close the structure
            offCommandPayload.EndContainer(); // Close the array
            offCommandPayload.AddUInt8(255, 12); // interactionModelRevision
            offCommandPayload.EndContainer(); // Close the structure

            var offCommandPayloadMessagePayload = new MessagePayload(offCommandPayload);
            offCommandPayloadMessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
            offCommandPayloadMessagePayload.ProtocolId = 0x01; // IM Protocol Messages
            offCommandPayloadMessagePayload.ProtocolOpCode = 0x08; // InvokeRequest

            var offCommandMessageFrame = new MessageFrame(offCommandPayloadMessagePayload);
            offCommandMessageFrame.MessageFlags |= MessageFlags.S;
            offCommandMessageFrame.SecurityFlags = 0x00;
            offCommandMessageFrame.SourceNodeID = BitConverter.ToUInt64(fabric.RootNodeId.ToByteArrayUnsigned());
            offCommandMessageFrame.DestinationNodeId = BitConverter.ToUInt64(asset.Node.NodeId.ToByteArrayUnsigned());

            await caseExchange.SendAsync(offCommandMessageFrame);
            var offCommandResultFrame = await caseExchange.WaitForNextMessageAsync();
            await caseExchange.AcknowledgeMessageAsync(offCommandResultFrame.MessageCounter);

            return caseExchange;
        }

        public static async Task<MessageExchange> SendCommandOn(MatterAsset asset, Fabric fabric, ISession caseSession)
        {
            var caseExchange = caseSession.CreateExchange();

            var onCommandPayload = new MatterTLV();
            onCommandPayload.AddStructure();
            onCommandPayload.AddBool(0, false);
            onCommandPayload.AddBool(1, false);
            onCommandPayload.AddArray(tagNumber: 2); // InvokeRequests
            onCommandPayload.AddStructure();
            onCommandPayload.AddList(tagNumber: 0); // CommandPath
            onCommandPayload.AddUInt16(tagNumber: 0, 0x01); // Endpoint 0x01
            onCommandPayload.AddUInt32(tagNumber: 1, 0x06); // ClusterId 0x06 - OnOff
            onCommandPayload.AddUInt16(tagNumber: 2, 0x01); // 1.5.7 Command On
            onCommandPayload.EndContainer();
            onCommandPayload.AddStructure(1); // CommandFields
            onCommandPayload.EndContainer(); // Close the CommandFields
            onCommandPayload.EndContainer(); // Close the structure
            onCommandPayload.EndContainer(); // Close the array
            onCommandPayload.AddUInt8(255, 12); // interactionModelRevision
            onCommandPayload.EndContainer(); // Close the structure

            var onCommandPayloadMessagePayload = new MessagePayload(onCommandPayload);
            onCommandPayloadMessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
            onCommandPayloadMessagePayload.ProtocolId = 0x01; // IM Protocol Messages
            onCommandPayloadMessagePayload.ProtocolOpCode = 0x08; // InvokeRequest

            var onCommandMessageFrame = new MessageFrame(onCommandPayloadMessagePayload);
            onCommandMessageFrame.MessageFlags |= MessageFlags.S;
            onCommandMessageFrame.SecurityFlags = 0x00;
            onCommandMessageFrame.SourceNodeID = BitConverter.ToUInt64(fabric.RootNodeId.ToByteArrayUnsigned());
            onCommandMessageFrame.DestinationNodeId = BitConverter.ToUInt64(asset.Node.NodeId.ToByteArrayUnsigned());

            await caseExchange.SendAsync(onCommandMessageFrame);
            var onCommandResultFrame = await caseExchange.WaitForNextMessageAsync();
            await caseExchange.AcknowledgeMessageAsync(onCommandResultFrame.MessageCounter);

            return caseExchange;
        }
    }
}
