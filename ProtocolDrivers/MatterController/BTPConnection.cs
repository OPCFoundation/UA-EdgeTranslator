
using InTheHand.Bluetooth;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Matter.Core.BTP
{
    internal class BTPConnection : IConnection
    {
        public static readonly BluetoothUuid MATTER_UUID = BluetoothUuid.FromShortId(0xFFF6);
        private static readonly BluetoothUuid C1_UUID = BluetoothUuid.FromGuid(Guid.Parse("18EE2EF5-263D-4559-959F-4F9C429F9D11"));
        private static readonly BluetoothUuid C2_UUID = BluetoothUuid.FromGuid(Guid.Parse("18EE2EF5-263D-4559-959F-4F9C429F9D12"));

        private IGattCharacteristic _read;
        private IGattCharacteristic _write;

        private Channel<BTPFrame> _instream = Channel.CreateBounded<BTPFrame>(10);
        private Timer _acknowledgementTimer;
        private ushort _mtu = 0;
        private ushort _serverWindow = 0;
        private byte _txCounter = 0; // First is 0
        private byte _rxCounter = 0;
        private byte _rxAcknowledged = 255; //Ensures we acknowledge the handshake
        private byte _txAcknowledged = 0;

        private SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private bool _connected = false;
        private IBluetoothDevice _device;

        public BTPConnection(IBluetoothDevice device)
        {
            _device = device;
        }

        public IConnection OpenConnection()
        {
            if (!_device.GattServer.IsConnected)
            {
                _device.GattServer.ConnectAsync().GetAwaiter().GetResult();
            }

            _device.GattServerDisconnected += Device_GattServerDisconnected;

            _mtu = (ushort)Math.Min(_device.GattServer.Mtu, 244);

            _acknowledgementTimer = new Timer(SendStandaloneAcknowledgement, null, 2000, 5000);

            IGattService service = _device.GattServer.GetPrimaryServiceAsync(MATTER_UUID).GetAwaiter().GetResult();

            _write = service.GetCharacteristicAsync(C1_UUID).GetAwaiter().GetResult();
            _read = service.GetCharacteristicAsync(C2_UUID).GetAwaiter().GetResult();
            _read.CharacteristicValueChanged += ReadCharacteristicValueChanged;

            SendHandshakeAsync().GetAwaiter().GetResult();

            return this;
        }

        public void Close()
        {
            _acknowledgementTimer.Dispose();
            _read.StopNotificationsAsync().GetAwaiter().GetResult();
        }

        private void Device_GattServerDisconnected(object sender, EventArgs e)
        {
            _connected = false;

            _rxAcknowledged = 255;

            _acknowledgementTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            Console.WriteLine(DateTime.Now + "** GATT Disconnected **");
        }

        private void ReadCharacteristicValueChanged(object sender, GattCharacteristicValueChangedEventArgs e)
        {
            if (e.Value != null)
            {
                _acknowledgementTimer.Change(2000, 5000);

                BTPFrame frame = new BTPFrame(e.Value);
                if ((frame.ControlFlags & BTPFlags.Acknowledge) != 0)
                {
                    _txAcknowledged = frame.AcknowledgeNumber;
                }

                if ((frame.ControlFlags & BTPFlags.Handshake) == 0)
                {
                    _rxCounter = frame.Sequence;
                }

                if ((frame.ControlFlags & BTPFlags.Beginning) != 0 || (frame.ControlFlags & BTPFlags.Continuing) != 0)
                {
                    _instream.Writer.WriteAsync(frame).GetAwaiter().GetResult();
                }
            }
        }

        public async Task SendAsync(byte[] message)
        {
            if (!_connected)
            {
                OpenConnection();
            }

            await WaitForWindow(CancellationToken.None).ConfigureAwait(false);

            await _writeLock.WaitAsync().ConfigureAwait(false);

            try
            {
                if (_rxAcknowledged != _rxCounter)
                {
                    _rxAcknowledged = _rxCounter;
                    _acknowledgementTimer.Change(2000, 5000);
                }

                await WaitForWindow(CancellationToken.None).ConfigureAwait(false);

                BTPFrame[] segments = GetSegments(message);

                Console.WriteLine("BTP Message has been split into {0} BTP frame segments", segments.Length);

                foreach (var btpFrame in segments)
                {
                    btpFrame.Sequence = _txCounter++;

                    Console.WriteLine("Sending BTP frame segment [{0}] [{1}]...", btpFrame.Sequence, Convert.ToString((byte)btpFrame.ControlFlags, 2).PadLeft(8, '0'));

                    var btpWriter = new MatterMessageWriter();

                    btpFrame.Serialize(btpWriter);

                    await _write.WriteAsync(btpWriter.GetBytes()).ConfigureAwait(false);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task WaitForWindow(CancellationToken token)
        {
            while ((_txCounter - _txAcknowledged) > _serverWindow)
            {
                await _instream.Reader.WaitToReadAsync(token).ConfigureAwait(false);
            }
        }

        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            BTPFrame segment = await _instream.Reader.ReadAsync().ConfigureAwait(false);

            return segment.Payload;
        }

        private async void SendStandaloneAcknowledgement(object state)
        {
            if (!_connected)
            {
                return;
            }

            await _writeLock.WaitAsync().ConfigureAwait(false);

            try
            {
                Console.WriteLine($"Sending Standalone Acknowledgement for {_rxCounter}");

                BTPFrame acknowledgementFrame = new(new byte[10]);
                acknowledgementFrame.Sequence = _txCounter++;
                acknowledgementFrame.ControlFlags = BTPFlags.Acknowledge;

                if (_rxAcknowledged != _rxCounter)
                {
                    _rxAcknowledged = _rxCounter;
                    acknowledgementFrame.AcknowledgeNumber = _rxAcknowledged;
                }

                var writer = new MatterMessageWriter();
                acknowledgementFrame.Serialize(writer);

                await _write.WriteAsync(writer.GetBytes()).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<bool> SendHandshakeAsync()
        {
            byte[] handshakePayload = new byte[9];
            handshakePayload[0] = 0x65; // Handshake flag
            handshakePayload[1] = 0x6C;
            handshakePayload[2] = 0x04; // Version
            handshakePayload[3] = 0x00;
            handshakePayload[4] = 0x00;
            handshakePayload[5] = 0x00;
            handshakePayload[6] = 0x00;
            handshakePayload[7] = 0x00;
            handshakePayload[8] = 0x02; // Only accept two frames at a time!

            await _write.WriteAsync(handshakePayload).ConfigureAwait(false);
            await _read.StartNotificationsAsync().ConfigureAwait(false);

            var handshakeResponseFrame = await _instream.Reader.ReadAsync().ConfigureAwait(false);

            _mtu = handshakeResponseFrame.ATTSize;
            _serverWindow = handshakeResponseFrame.WindowSize;

            // If we have matching versions from the handshake, we're good to go!
            _connected = handshakeResponseFrame.Version == 0x04;

            return _connected;
        }

        private BTPFrame[] GetSegments(byte[] messageBytes)
        {
            // We might need multiple frames to transport this message.
            var segments = new List<BTPFrame>();
            var messageBytesAddedToSegments = 0;

            do
            {
                BTPFrame segment = new(messageBytes);

                // If we have not created the first segment, this one will
                // have the Beginning control flag. It will also include the MessageLength.
                //
                // If we already have segments, set Continuing flag
                //
                // Depending on the type of message, we have different header lengths. E.g. for Beginning
                // we must inlude the MessageLength in the payload. For Continuing, we don't!
                // We start with the ControlFlags and the sequence number.
                var headerLength = 2;

                if (segments.Count == 0)
                {
                    segment.ControlFlags = BTPFlags.Beginning;
                    segment.MessageLength = (ushort)messageBytes.Length;
                    headerLength += 2; // Add two bytes to the header length to indicate we have the MessageLength.

                    // If we have any outstanding messages to acknowledges, add it here!
                    if (_rxAcknowledged != _rxCounter)
                    {
                        _rxAcknowledged = _rxCounter;
                        segment.AcknowledgeNumber = _rxAcknowledged;
                        segment.ControlFlags |= BTPFlags.Acknowledge;
                        headerLength += 1;
                    }
                }
                else
                {
                    segment.ControlFlags = BTPFlags.Continuing;
                }

                // Work out how much of the messageBytes we're putting into the slice.
                var howManyBytesLeftToSend = messageBytes.Length - messageBytesAddedToSegments;
                var howMuchSpaceAvailableInBTPFrame = _mtu - headerLength;

                ushort segmentSize = (ushort)Math.Min(howManyBytesLeftToSend, howMuchSpaceAvailableInBTPFrame);

                var segmentBytes = new byte[segmentSize];

                // Copy from our messageBytes into segmentBytes
                Buffer.BlockCopy(messageBytes, messageBytesAddedToSegments, segmentBytes, 0, segmentBytes.Length);

                // If the current segmentSize + all the bytes already added equals the total,
                // we send the Ending flag.
                if (segmentSize + messageBytesAddedToSegments == messageBytes.Length)
                {
                    segment.ControlFlags |= BTPFlags.Ending;
                }

                segment.Payload = segmentBytes;
                segments.Add(segment);

                messageBytesAddedToSegments += segmentSize;
            }
            while (messageBytesAddedToSegments < messageBytes.Length);

            return segments.ToArray();
        }
    }
}
