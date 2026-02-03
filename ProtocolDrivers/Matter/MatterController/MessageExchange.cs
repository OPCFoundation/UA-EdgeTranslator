using Org.BouncyCastle.Math;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Matter.Core
{
    /// <summary>
    /// Note: This class is not thread safe. We ensure that there is only one instance of this at a time!
    /// </summary>
    public class MessageExchange
    {
        private ushort _exchangeId;
        private ISession _session;

        private static int _instanceCount = 0;

        private uint _receivedMessageCounter = 255;
        private uint _acknowledgedMessageCounter = 255;

        private Timer _timer;
        private Task _readingThread;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private void Callback(object state)
        {
            lock (this)
            {
                Debug.WriteLine("Aborting and closing Message Exchange with ExchangeId {0} for Session {1} due to inactivity!", _exchangeId, _session.SessionId);

                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                _cancellationTokenSource.Cancel();
                _readingThread.Wait();

                _instanceCount--;
            }
        }

        private Channel<MessageFrame> _incomingMessageChannel = Channel.CreateBounded<MessageFrame>(10);

        public MessageExchange(ushort exchangeId, ISession session)
        {
            lock (this)
            {
                if (_instanceCount > 0)
                {
                    throw new InvalidOperationException("Only one MessageExchange instance is allowed at a time. This is a limitation with many Matter devices!");
                }

                _instanceCount++;

                Debug.WriteLine("Creating new MessageExchange with ExchangeId {0} for Session {1}.", exchangeId, session.SessionId);

                _exchangeId = exchangeId;
                _session = session;

                _timer = new Timer(Callback, null, Timeout.Infinite, Timeout.Infinite);
                _readingThread = Task.Run(ReceiveAsync);
            }
        }

        public void Close()
        {
            Task.Delay(250).GetAwaiter().GetResult(); // Grace period before close to allow the other side to cleanup

            lock (this)
            {
                Debug.WriteLine("Closing Message Exchange with ExchangeId {0} for Session {1}.", _exchangeId, _session.SessionId);

                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                _cancellationTokenSource.Cancel();
                _readingThread.Wait();

                _instanceCount--;
            }
        }

        public async Task AcknowledgeMessageAsync(uint messageCounter, ushort exchangeId = 0)
        {
            if (exchangeId == 0)
            {
                exchangeId = _exchangeId;
            }

            MessagePayload messagePayload = new(ExchangeFlags.Initiator | ExchangeFlags.Acknowledgement, ProtocolOpCode.Acknowledgement, exchangeId, 0, messageCounter, 0, null);

            _acknowledgedMessageCounter = messageCounter;

            MessageFrame frame = new(MessageFlags.Version1, _session.PeerSessionId, SecurityFlags.UnicastSession, _session.MessageCounter++, 0, 0, 0, messagePayload);

            var bytes = _session.Encode(frame);

            Debug.WriteLine("SendAck: msg flags {0} exch flags {1} msg counter {2} ack counter {3} session {4} exch {5}.", frame.MessageFlags, frame.MessagePayload.ExchangeFlags, frame.MessageCounter, frame.MessagePayload.AcknowledgedMessageCounter, frame.SessionID, frame.MessagePayload.ExchangeId);

            await _session.SendAsync(bytes).ConfigureAwait(false);
        }

        public async Task<MessageFrame> SendAndReceiveMessageAsync(MatterTLV payload, byte protocolId, ProtocolOpCode opCode)
        {
            MessagePayload messagePayload = new(ExchangeFlags.Initiator, opCode, _exchangeId, protocolId, 0, 0, payload);

            await SendAsync(messagePayload).ConfigureAwait(false);

            return await WaitForNextMessageAsync().ConfigureAwait(false);
        }

        public async Task<MessageFrame> SendTimedCommandAsync(ushort timeoutMs, ushort endpoint, uint cluster, ushort command, object[] parameters = null)
        {
            var payload = new MatterTLV();
            payload.AddStructure();
            payload.AddUInt16(0, timeoutMs);
            payload.AddUInt8(255, 12);        // InteractionModelRevision
            payload.EndContainer();           // Close the structure

            MessageFrame timedResponse = await SendAndReceiveMessageAsync(payload, 1, ProtocolOpCode.TimedRequest).ConfigureAwait(false);

            var status = StatusResponseResult.Parse(timedResponse.MessagePayload.ApplicationPayload);
            if (!status.IsSuccess)
            {
                string diag = $"TimedRequest denied: {status.ImStatus}"
                            + (status.ClusterStatus.HasValue ? $", ClusterStatus=0x{status.ClusterStatus.Value:X4}" : string.Empty);
                Console.WriteLine(diag);
                return null;
            }

            return await SendCommandAsync(endpoint, cluster, command, parameters);
        }

        public class AccessControlTarget
        {
            public ulong Cluster { get; set; }
        }

        public async Task<MessageFrame> SendCommandAsync(ushort endpoint, uint cluster, ushort command, object[] parameters = null, bool timed = false)
        {
            var payload = new MatterTLV();
            payload.AddStructure();
            payload.AddBool(0, false);  // Suppress Response
            payload.AddBool(1, timed);  // Is Timed Invoke
            payload.AddArray(2);        // Invoke Requests
            payload.AddStructure();
            payload.AddList(0);         // Command Path
            payload.AddUInt16(0, endpoint);
            payload.AddUInt32(1, cluster);
            payload.AddUInt16(2, command);
            payload.EndContainer();

            if ((parameters != null) && (parameters.Length > 0))
            {
                payload.AddStructure(1);    // Command Fields

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
                        case byte b:        payload.AddUInt8(i, b); break;
                        case short s:       payload.AddInt16(i, s); break;
                        case ushort us:     payload.AddUInt16(i, us); break;
                        case int it:        payload.AddInt32(i, it); break;
                        case uint ui:       payload.AddUInt32(i, ui); break;
                        case long l:        payload.AddInt64(i, l); break;
                        case ulong ul:      payload.AddUInt64(i, ul); break;
                        case BigInteger bi: payload.AddUInt64(i, bi.ToByteArrayUnsigned()); break;
                        case string s:      payload.AddUTF8String(i, s); break;
                        case byte[] ba:     payload.AddOctetString(i, ba); break;
                        case ulong[] ula:   payload.AddArray(i); for (byte j = 0; j < ula.Length; j++) { payload.AddUInt64(j, ula[j]); } payload.EndContainer(); break;
                        case AccessControlTarget[] acta:
                            payload.AddArray(i);
                            for (byte j = 0; j < acta.Length; j++)
                            {
                                payload.AddStructure();
                                payload.AddUInt64(0, acta[j].Cluster);
                                payload.EndContainer();
                            }
                            payload.EndContainer();
                            break;
                        case bool bo:       payload.AddBool(i, bo); break;
                        case double d:      payload.AddUInt64(i, ulong.Parse(d.ToString())); break; // not a bug: We convert WoT numbers to OPC UA doubles to Matter unsigned integers!
                        default:            throw new NotSupportedException($"Parameter type {param.GetType()} is not supported.");
                    }
                }

                payload.EndContainer(); // Close the CommandFields
            }

            payload.EndContainer();     // Close the structure
            payload.EndContainer();     // Close the array
            payload.AddUInt8(255, 12);  // interactionModelRevision
            payload.EndContainer();     // Close the structure

            return await SendAndReceiveMessageAsync(payload, 1, ProtocolOpCode.InvokeRequest).ConfigureAwait(false);
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

            Debug.WriteLine("Send: opcode {0} msg flags {1} exch flags {2} msg counter {3} ack counter {4} session {5} exch {6}.", frame.MessagePayload.OpCode, frame.MessageFlags, frame.MessagePayload.ExchangeFlags, frame.MessageCounter, frame.MessagePayload.AcknowledgedMessageCounter, frame.SessionID, frame.MessagePayload.ExchangeId);

            await _session.SendAsync(bytes).ConfigureAwait(false);
        }

        public async Task<MessageFrame> WaitForNextMessageAsync()
        {
            try
            {
                // wait up to 10 seconds for a message (e.g. Thread network scanning can take this long...)
                _timer.Change(10000, 10000);
                MessageFrame result = await _incomingMessageChannel.Reader.ReadAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                _timer.Change(Timeout.Infinite, Timeout.Infinite);

                return result;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("MessageExchange has been closed.");
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
                        Debug.WriteLine("Exchange {0}, Rcvd Message Counter {1}, Rcvd SessionID {2}, is not for this session {3}, peer session {4}. Ignoring...", _exchangeId, frame.MessageCounter, frame.SessionID, _session.SessionId, _session.PeerSessionId);
                        continue;
                    }

                    // Decode the full message now.
                    frame = _session.Decode(bytes);

                    // Check if we have this message already.
                    if (_receivedMessageCounter >= frame.MessageCounter)
                    {
                        Debug.WriteLine("Message {0} is a duplicate. Dropping...", frame.MessageCounter);

                        // check if we should better ack it
                        if (frame.MessagePayload.ExchangeFlags.HasFlag(ExchangeFlags.Reliability))
                        {
                            Debug.WriteLine("Acking duplicate message {0}.", frame.MessageCounter);
                            AcknowledgeMessageAsync(frame.MessageCounter, frame.MessagePayload.ExchangeId).GetAwaiter().GetResult();
                        }

                        continue;
                    }

                    _receivedMessageCounter = frame.MessageCounter;

                    // If this is a standalone acknowledgement, don't pass this up a level.
                    if (frame.MessagePayload.ProtocolId == 0x00 && frame.MessagePayload.OpCode == ProtocolOpCode.Acknowledgement)
                    {
                        Debug.WriteLine("RecvAck: msg flags {0}, exch flags {1}, msg counter {2}, ack counter {3}, rcvd session {4}, local session {5}, peer session {6}, exch {7}.", frame.MessageFlags, frame.MessagePayload.ExchangeFlags, frame.MessageCounter, frame.MessagePayload.AcknowledgedMessageCounter, frame.SessionID, _session.SessionId, _session.PeerSessionId, frame.MessagePayload.ExchangeId);

                        // check if the Ack needs an ack back
                        if (frame.MessagePayload.ExchangeFlags.HasFlag(ExchangeFlags.Reliability))
                        {
                            AcknowledgeMessageAsync(frame.MessageCounter, frame.MessagePayload.ExchangeId).GetAwaiter().GetResult();
                        }

                        continue;
                    }

                    Debug.WriteLine("Recv: opcode {0}, msg flags {1}, exch flags {2}, msg counter {3}, ack counter {4}, rcvd session {5}, local session {6}, peer session {7}, exch {8}.", frame.MessagePayload.OpCode, frame.MessageFlags, frame.MessagePayload.ExchangeFlags, frame.MessageCounter, frame.MessagePayload.AcknowledgedMessageCounter, frame.SessionID, _session.SessionId, _session.PeerSessionId, frame.MessagePayload.ExchangeId);

                    // This message needs processing, so put it onto the queue.
                    await _incomingMessageChannel.Writer.WriteAsync(frame).ConfigureAwait(false);

                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to read incoming message: {0}: [{1}]", ex.Message, BitConverter.ToString(bytes));
                }

            } while (!_cancellationTokenSource.Token.IsCancellationRequested);
        }
    }
}
