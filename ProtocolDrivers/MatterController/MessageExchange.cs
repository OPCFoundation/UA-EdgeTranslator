using Matter.Core.Sessions;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Matter.Core
{
    public class MessageExchange
    {
        private ushort _exchangeId;
        private ISession _session;
        private Thread _readingThread;

        private uint _receivedMessageCounter = 255;
        private uint _acknowledgedMessageCounter = 255;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private Channel<MessageFrame> _incomingMessageChannel = Channel.CreateBounded<MessageFrame>(1);

        // For this, the role will always be Initiator.
        //
        public MessageExchange(ushort exchangeId, ISession session)
        {
            _exchangeId = exchangeId;
            _session = session;

            _readingThread = new Thread(new ThreadStart(ReceiveAsync));
            _readingThread.Start();
        }

        public void Close()
        {
            _cancellationTokenSource.Cancel();
            _readingThread.Join();

            Console.WriteLine("Closed MessageExchange [E: {0}]", _exchangeId);
        }

        public async Task SendAsync(MessageFrame message)
        {
            // Set the common data on the MessageFrame.
            //
            message.SessionID = _session.PeerSessionId;
            message.SourceNodeID = _session.SourceNodeId;
            message.DestinationNodeId = _session.DestinationNodeId;
            message.MessagePayload.ExchangeID = _exchangeId;
            message.MessageCounter = _session.MessageCounter;

            uint? messageToAck = null;

            // Do we have any unacknowledged messages?
            // If yes, add the acknowledgement to this outgoing message.
            //
            if (_acknowledgedMessageCounter != _receivedMessageCounter)
            {
                _acknowledgedMessageCounter = _receivedMessageCounter;

                message.MessagePayload.ExchangeFlags |= ExchangeFlags.Acknowledgement;
                message.MessagePayload.AcknowledgedMessageCounter = _acknowledgedMessageCounter;
                messageToAck = _acknowledgedMessageCounter;
            }

            if (_session.UseMRP)
            {
                message.MessagePayload.ExchangeFlags |= ExchangeFlags.Reliability;
            }

            Console.WriteLine("\n>>> Sending Message {0}", message.DebugInfo());

            var bytes = _session.Encode(message);

            await _session.SendAsync(bytes);
        }

        public async Task<MessageFrame> WaitForNextMessageAsync()
        {
            Console.WriteLine("Waiting for incoming message...");
            return await _incomingMessageChannel.Reader.ReadAsync(_cancellationTokenSource.Token);
        }

        /// <summary>
        /// This is a message pump. It waits for data to be available and passes it to
        /// the _incomingMessageChannel
        /// </summary>
        private async void ReceiveAsync()
        {
            do
            {
                byte[] bytes = Array.Empty<byte>();

                try
                {
                    bytes = await _session.ReadAsync(_cancellationTokenSource.Token);

                    var messageFrameParts = new MessageFrameParts(bytes);

                    var messageFrameWithHeader = messageFrameParts.MessageFrameWithHeaders();

                    if (messageFrameWithHeader.SessionID != _session.SessionId)
                    {
                        Console.WriteLine("[E: {0}] Message {1} [S: {2}] is not for this session {3}. Ignoring...", _exchangeId, messageFrameWithHeader.MessageCounter, messageFrameWithHeader.SessionID, _session.SessionId);
                        continue;
                    }

                    var messageFrame = _session.Decode(messageFrameParts);

                    Console.WriteLine("\n<<< Received Message {0}", messageFrame.DebugInfo());

                    // Check if we have this message already.
                    if (_receivedMessageCounter >= messageFrame.MessageCounter)
                    {
                        Console.WriteLine("Message {0} is a duplicate. Dropping...", messageFrame.MessageCounter);
                        continue;
                    }

                    _receivedMessageCounter = messageFrame.MessageCounter;

                    // If this is a standalone acknowledgement, don't pass this up a level.
                    //
                    if (messageFrame.MessagePayload.ProtocolId == 0x00 && messageFrame.MessagePayload.ProtocolOpCode == 0x10)
                    {
                        Console.WriteLine("Received Message is a standalone ack for {0}", messageFrame.MessagePayload.AcknowledgedMessageCounter);
                        continue;
                    }

                    // This message needs processing, so put it onto the queue.
                    //
                    await _incomingMessageChannel.Writer.WriteAsync(messageFrame);

                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Failed to read incoming message: {0}: [{1}]", ex.Message, BitConverter.ToString(bytes));
                    Console.ForegroundColor = ConsoleColor.White;
                }

            } while (!_cancellationTokenSource.Token.IsCancellationRequested);

            Console.WriteLine("Exiting ReceiveAsync loop...");
        }

        public async Task AcknowledgeMessageAsync(uint messageCounter)
        {
            MessagePayload payload = new MessagePayload();
            payload.ExchangeFlags |= ExchangeFlags.Acknowledgement;
            payload.ExchangeFlags |= ExchangeFlags.Initiator;
            payload.ExchangeID = _exchangeId;
            payload.AcknowledgedMessageCounter = messageCounter;
            payload.ProtocolId = 0x00; // Secure Channel
            payload.ProtocolOpCode = 0x10; // MRP Standalone Acknowledgement

            MessageFrame messageFrame = new MessageFrame(payload);
            messageFrame.MessageFlags |= MessageFlags.S;
            messageFrame.SecurityFlags = 0x00;
            messageFrame.SessionID = _session.SessionId;
            messageFrame.SourceNodeID = _session.SourceNodeId;
            messageFrame.MessageCounter = _session.MessageCounter;

            await SendAsync(messageFrame);
        }
    }
}
