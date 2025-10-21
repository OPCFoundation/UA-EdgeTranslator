using Matter.Core.Sessions;
using Matter.Core.TLV;
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
        private Task _readingThread;

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

            _readingThread = Task.Run(ReceiveAsync);
        }

        public void Close()
        {
            _cancellationTokenSource.Cancel();
            _readingThread.Wait();
        }

        public async Task SendAsync(MessageFrame message)
        {
            // Set the common data on the MessageFrame.
            message.SessionID = _session.PeerSessionId;
            message.SourceNodeID = _session.SourceNodeId;
            message.DestinationNodeId = _session.DestinationNodeId;
            message.MessagePayload.ExchangeID = _exchangeId;
            message.MessageCounter = _session.MessageCounter;

            // Do we have any unacknowledged messages?
            // If yes, add the acknowledgement to this outgoing message.
            if (_acknowledgedMessageCounter != _receivedMessageCounter)
            {
                _acknowledgedMessageCounter = _receivedMessageCounter;

                message.MessagePayload.ExchangeFlags |= ExchangeFlags.Acknowledgement;
                message.MessagePayload.AcknowledgedMessageCounter = _acknowledgedMessageCounter;
            }

            if (_session.UseMRP)
            {
                message.MessagePayload.ExchangeFlags |= ExchangeFlags.Reliability;
            }

            Console.WriteLine("Sending Message {0}", message.DebugInfo());

            var bytes = _session.Encode(message);

            await _session.SendAsync(bytes).ConfigureAwait(false);
        }

        public async Task<MessageFrame> WaitForNextMessageAsync()
        {
            try
            {
                return await _incomingMessageChannel.Reader.ReadAsync(_cancellationTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("MessageExchange has been closed.");
                return null;
            }
        }

        /// <summary>
        /// This is a message pump. It waits for data to be available and passes it to
        /// the _incomingMessageChannel
        /// </summary>
        private async Task ReceiveAsync()
        {
            do
            {
                byte[] bytes = Array.Empty<byte>();

                try
                {
                    bytes = await _session.ReadAsync(_cancellationTokenSource.Token);
                    if (bytes == null)
                    {
                        continue;
                    }

                    var messageFrameParts = new MessageFrameParts(bytes);

                    var messageFrameWithHeader = messageFrameParts.MessageFrameWithHeaders();

                    if (messageFrameWithHeader.SessionID != _session.SessionId)
                    {
                        Console.WriteLine("[E: {0}] Message {1} [S: {2}] is not for this session {3}. Ignoring...", _exchangeId, messageFrameWithHeader.MessageCounter, messageFrameWithHeader.SessionID, _session.SessionId);
                        continue;
                    }

                    var messageFrame = _session.Decode(messageFrameParts);

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
                        continue;
                    }

                    // This message needs processing, so put it onto the queue.
                    //
                    await _incomingMessageChannel.Writer.WriteAsync(messageFrame).ConfigureAwait(false);

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

        public async Task<MessageFrame> SendAndReceiveMessageAsync(MatterTLV payload, byte protocolId, byte opCode)
        {
            MessagePayload messagePayload = new(payload);
            messagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
            messagePayload.ExchangeID = _exchangeId;
            messagePayload.ProtocolId = protocolId;
            messagePayload.ProtocolOpCode = opCode;

            MessageFrame messageFrame = new(messagePayload);
            messageFrame.MessageFlags |= MessageFlags.S;
            messageFrame.SecurityFlags = 0;
            messageFrame.SessionID = _session.SessionId;
            messageFrame.SourceNodeID = _session.SourceNodeId;
            messageFrame.DestinationNodeId = _session.DestinationNodeId;
            messageFrame.MessageCounter = _session.MessageCounter;

            await SendAsync(messageFrame).ConfigureAwait(false);
            return await WaitForNextMessageAsync().ConfigureAwait(false);
        }

        public async Task AcknowledgeMessageAsync(uint messageCounter)
        {
            MessagePayload messagePayload = new MessagePayload();
            messagePayload.ExchangeFlags |= ExchangeFlags.Acknowledgement;
            messagePayload.ExchangeFlags |= ExchangeFlags.Initiator;
            messagePayload.ExchangeID = _exchangeId;
            messagePayload.AcknowledgedMessageCounter = messageCounter;
            messagePayload.ProtocolId = 0; // Secure Channel
            messagePayload.ProtocolOpCode = 0x10; // MRP Standalone Acknowledgement

            MessageFrame messageFrame = new MessageFrame(messagePayload);
            messageFrame.MessageFlags |= MessageFlags.S;
            messageFrame.SecurityFlags = 0;
            messageFrame.SessionID = _session.SessionId;
            messageFrame.SourceNodeID = _session.SourceNodeId;
            messageFrame.DestinationNodeId = _session.DestinationNodeId;
            messageFrame.MessageCounter = _session.MessageCounter;

            await SendAsync(messageFrame).ConfigureAwait(false);
        }

        public async Task<MessageFrame> SendCommand(byte endpoint, byte cluster, byte command, byte opCode, object[] parameters = null)
        {
            var payload = new MatterTLV();
            payload.AddStructure();
            payload.AddBool(0, false);
            payload.AddBool(1, false);
            payload.AddArray(2); // InvokeRequests
            payload.AddStructure();
            payload.AddList(0); // CommandPath
            payload.AddUInt16(0, endpoint);
            payload.AddUInt32(1, cluster);
            payload.AddUInt16(2, command);
            payload.EndContainer();
            payload.AddStructure(1); // CommandFields

            if (parameters != null)
            {
                for (byte i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i] == null)
                    {
                        // skip null paramters
                        continue;
                    }

                    object param = parameters[i];
                    switch (param)
                    {
                        case byte b:
                            payload.AddUInt8(i, b);
                            break;
                        case short s:
                            payload.AddInt16(i, s);
                            break;
                        case ushort us:
                            payload.AddUInt16(i, us);
                            break;
                        case int it:
                            payload.AddInt32(i, it);
                            break;
                        case uint ui:
                            payload.AddUInt32(i, ui);
                            break;
                        case long l:
                                payload.AddInt64(i, l);
                                break;
                        case ulong ul:
                            payload.AddUInt64(i, ul);
                            break;
                        case Org.BouncyCastle.Math.BigInteger bi:
                            payload.AddUInt64(i, bi.ToByteArrayUnsigned());
                            break;
                        case string s:
                            payload.AddUTF8String(i, s);
                            break;
                        case byte[] ba:
                            payload.AddOctetString(i, ba);
                            break;
                        case bool bo:
                            payload.AddBool(i, bo);
                            break;
                        default:
                            throw new NotSupportedException($"Parameter type {param.GetType()} is not supported.");
                    }
                }
            }

            payload.EndContainer(); // Close the CommandFields
            payload.EndContainer(); // Close the structure
            payload.EndContainer(); // Close the array
            payload.AddUInt8(255, 12); // interactionModelRevision
            payload.EndContainer(); // Close the structure

            MessageFrame response = await SendAndReceiveMessageAsync(payload, 1, opCode).ConfigureAwait(false);
            await AcknowledgeMessageAsync(response.MessageCounter).ConfigureAwait(false);

            return response;
        }
    }
}
