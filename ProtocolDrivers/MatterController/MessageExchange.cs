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

        private Channel<MessageFrame> _incomingMessageChannel = Channel.CreateBounded<MessageFrame>(10);

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

        public async Task AcknowledgeMessageAsync(uint messageCounter)
        {
            MessagePayload messagePayload = new(ExchangeFlags.Initiator | ExchangeFlags.Acknowledgement, 0x10, _exchangeId, 0, messageCounter, 0, null);

            _acknowledgedMessageCounter = messageCounter;

            MessageFrame frame = new(MessageFlags.SourceNodeID, _session.PeerSessionId, SecurityFlags.UnicastSession, _session.MessageCounter, 0, 0, 0, messagePayload);

            var bytes = _session.Encode(frame);

            //Console.WriteLine("SendAck: msg flags {0} exch flags {1} msg counter {2} ack counter {3} session {4} exch {5}.", frame.MessageFlags, frame.MessagePayload.ExchangeFlags, frame.MessageCounter, frame.MessagePayload.AcknowledgedMessageCounter, frame.SessionID, frame.MessagePayload.ExchangeId);

            await _session.SendAsync(bytes).ConfigureAwait(false);
        }

        public async Task<MessageFrame> SendAndReceiveMessageAsync(MatterTLV payload, byte protocolId, byte opCode)
        {
            MessagePayload messagePayload = new(ExchangeFlags.Initiator, opCode, _exchangeId, protocolId, 0, 0, payload);

            await SendAsync(messagePayload).ConfigureAwait(false);

            return await WaitForNextMessageAsync().ConfigureAwait(false);
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

            return await SendAndReceiveMessageAsync(payload, 1, opCode).ConfigureAwait(false);
        }

        private async Task SendAsync(MessagePayload message)
        {
            // add any unacknowledged acknowledgements to this outgoing message
            if (_acknowledgedMessageCounter != _receivedMessageCounter)
            {
                _acknowledgedMessageCounter = _receivedMessageCounter;

                message.ExchangeFlags |= ExchangeFlags.Acknowledgement;
                message.AcknowledgedMessageCounter = _acknowledgedMessageCounter;
            }

            if (_session.UseMRP)
            {
                message.ExchangeFlags |= ExchangeFlags.Reliability;
            }

            MessageFrame frame = new(
                MessageFlags.SourceNodeID,
                _session.PeerSessionId,
                SecurityFlags.UnicastSession,
                _session.MessageCounter++,
                _session.SourceNodeId,
                _session.DestinationNodeId,
                0,
                message);

            var bytes = _session.Encode(frame);

            //Console.WriteLine("Send: opcode 0x{0:X2} msg flags {1} exch flags {2} msg counter {3} ack counter {4} session {5} exch {6}.", frame.MessagePayload.ProtocolOpCode, frame.MessageFlags, frame.MessagePayload.ExchangeFlags, frame.MessageCounter, frame.MessagePayload.AcknowledgedMessageCounter, frame.SessionID, frame.MessagePayload.ExchangeId);

            await _session.SendAsync(bytes).ConfigureAwait(false);
        }

        private async Task<MessageFrame> WaitForNextMessageAsync()
        {
            try
            {
                return await _incomingMessageChannel.Reader.ReadAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("MessageExchange has been closed.");
                return null;
            }
        }

        private async Task ReceiveAsync()
        {
            do
            {
                byte[] bytes = Array.Empty<byte>();

                try
                {
                    bytes = await _session.ReadAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                    if (bytes == null)
                    {
                        continue;
                    }

                    MessageFrame frame = MessageFrame.Deserialize(bytes, false);

                    if (frame.SessionID != _session.SessionId)
                    {
                        Console.WriteLine("[E: {0}] Message {1} [SourceNodeID: {2}] is not for this session {3}. Ignoring...", _exchangeId, frame.MessageCounter, frame.SourceNodeID, _session.SessionId);
                        continue;
                    }

                    // Check if we have this message already.
                    if (_receivedMessageCounter >= frame.MessageCounter)
                    {
                        Console.WriteLine("Message {0} is a duplicate. Dropping...", frame.MessageCounter);
                        continue;
                    }

                    _receivedMessageCounter = frame.MessageCounter;

                    // Decode the full message now.
                    frame = _session.Decode(bytes);

                    // If this is a standalone acknowledgement, don't pass this up a level.
                    if (frame.MessagePayload.ProtocolId == 0x00 && frame.MessagePayload.ProtocolOpCode == 0x10)
                    {
                        //Console.WriteLine("RecvAck: msg flags {0} exch flags {1} msg counter {2} ack counter {3} session {4} exch {5}.", frame.MessageFlags, frame.MessagePayload.ExchangeFlags, frame.MessageCounter, frame.MessagePayload.AcknowledgedMessageCounter, frame.SessionID, frame.MessagePayload.ExchangeId);

                        // check if the Ack needs an ack back
                        if (frame.MessagePayload.ExchangeFlags.HasFlag(ExchangeFlags.Reliability))
                        {
                            AcknowledgeMessageAsync(frame.MessageCounter).GetAwaiter().GetResult();
                        }

                        continue;
                    }

                    //Console.WriteLine("Recv: opcode 0x{0:X2} msg flags {1} exch flags {2} msg counter {3} ack counter {4} session {5} exch {6}.", frame.MessagePayload.ProtocolOpCode, frame.MessageFlags, frame.MessagePayload.ExchangeFlags, frame.MessageCounter, frame.MessagePayload.AcknowledgedMessageCounter, frame.SessionID, frame.MessagePayload.ExchangeId);

                    // This message needs processing, so put it onto the queue.
                    await _incomingMessageChannel.Writer.WriteAsync(frame).ConfigureAwait(false);

                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Failed to read incoming message: {0}: [{1}]", ex.Message, BitConverter.ToString(bytes));
                    Console.ForegroundColor = ConsoleColor.White;
                }

            } while (!_cancellationTokenSource.Token.IsCancellationRequested);
        }
    }
}
