
using InTheHand.Bluetooth;
using MatterDotNet.Protocol.Payloads;
using MatterDotNet.Protocol.Payloads.OpCodes;
using System;
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

        private static readonly TimeSpan CONN_RSP_TIMEOUT = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ACK_TIME = TimeSpan.FromSeconds(6);

        private IGattCharacteristic Read;
        private IGattCharacteristic Write;
        Channel<BTPFrame> instream = Channel.CreateBounded<BTPFrame>(10);
        ushort MTU = 0;
        byte ServerWindow = 0;
        byte txCounter = 0; // First is 0
        byte rxCounter = 0;
        byte rxAcknowledged = 255; //Ensures we acknowledge the handshake
        byte txAcknowledged = 0;
        //Timer AckTimer;
        SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);
        bool connected;
        IBluetoothDevice device;

        public BTPConnection(IBluetoothDevice device)
        {
            this.device = device;
        }

        public IConnection OpenConnection()
        {
            //AckTimer = new Timer(SendAck, null, ACK_TIME, ACK_TIME);

            if (!device.GattServer.IsConnected)
            {
                device.GattServer.ConnectAsync().GetAwaiter().GetResult();
            }

            device.GattServerDisconnected += Device_GattServerDisconnected;

            MTU = (ushort)Math.Min(device.GattServer.Mtu, 244);

            IGattService service = device.GattServer.GetPrimaryServiceAsync(MATTER_UUID).GetAwaiter().GetResult();

            Write = service.GetCharacteristicAsync(C1_UUID).GetAwaiter().GetResult();
            Read = service.GetCharacteristicAsync(C2_UUID).GetAwaiter().GetResult();
            Read.CharacteristicValueChanged += Read_CharacteristicValueChanged;

            connected = true;

            SendHandshake().GetAwaiter().GetResult();

            return this;
        }

        public void Close()
        {
            Read.StopNotificationsAsync().GetAwaiter().GetResult();
        }

        private void Device_GattServerDisconnected(object sender, EventArgs e)
        {
            connected = false;
            //AckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            rxAcknowledged = 255;
            Console.WriteLine(DateTime.Now + "** GATT Disconnected **");
        }

        private async Task SendHandshake()
        {
            Console.WriteLine("Send Handshake Request");
            BTPFrame handshake = new BTPFrame(BTPFlags.Handshake | BTPFlags.Management | BTPFlags.Beginning | BTPFlags.Ending);
            handshake.OpCode = BTPManagementOpcode.Handshake;
            handshake.WindowSize = 8;
            handshake.ATT_MTU = MTU;

            await Write.WriteAsync(handshake.Serialize(9)).ConfigureAwait(false);
            await Read.StartNotificationsAsync().ConfigureAwait(false);

            BTPFrame frame = await instream.Reader.ReadAsync();
            if (frame.Version != BTPFrame.MATTER_BT_VERSION1)
            {
                throw new NotSupportedException($"Version {frame.Version} not supported");
            }

            Console.WriteLine($"MTU: {frame.ATT_MTU}, Window: {frame.WindowSize}");
        }

        private async void SendAck(object state)
        {
            await WriteLock.WaitAsync();
            if (!connected)
                return;
            try
            {
                BTPFrame segment = new BTPFrame(BTPFlags.Acknowledge);
                segment.Sequence = txCounter++;

                if (rxCounter != rxAcknowledged)
                {
                    segment.Acknowledge = rxCounter;
                    rxAcknowledged = rxCounter;
                }

                Console.WriteLine("[StandaloneAck] Wrote Segment: " + segment);

                await Write.WriteAsync(segment.Serialize(MTU));
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                WriteLock.Release();
            }
        }

        private void Read_CharacteristicValueChanged(object sender, GattCharacteristicValueChangedEventArgs e)
        {
            if (e.Value != null)
            {
                BTPFrame frame = new BTPFrame(e.Value!);
                Console.WriteLine("BTP Received: " + frame);

                //AckTimer?.Change(ACK_TIME, ACK_TIME);

                if ((frame.Flags & BTPFlags.Acknowledge) != 0)
                {
                    txAcknowledged = frame.Acknowledge;
                }

                if ((frame.Flags & BTPFlags.Handshake) == 0)
                {
                    rxCounter = frame.Sequence;
                }

                if ((frame.Flags & BTPFlags.Continuing) != 0 || (frame.Flags & BTPFlags.Beginning) != 0)
                {
                    instream.Writer.TryWrite(frame);
                }
            }
        }

        public async Task SendAsync(byte[] message)
        {
            if (!connected)
            {
                OpenConnection();
            }

            await WaitForWindow(CancellationToken.None);

            await WriteLock.WaitAsync();

            try
            {
                if (rxCounter != rxAcknowledged)
                {
                    rxAcknowledged = rxCounter;
                    //AckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                }

                await WaitForWindow(CancellationToken.None).ConfigureAwait(false);
                await Write.WriteAsync(message).ConfigureAwait(false);
            }
            finally
            {
                WriteLock.Release();
            }
        }

        private async Task WaitForWindow(CancellationToken token)
        {
            while (txCounter - txAcknowledged > ServerWindow)
            {
                await instream.Reader.WaitToReadAsync(token);
            }
        }

        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            BTPFrame segment = await instream.Reader.ReadAsync().ConfigureAwait(false);

            if ((segment.Flags & BTPFlags.Ending) == 0x0)
            {
                return null;
            }
            else
            {
                return segment.Payload.ToArray();
            }
        }
    }
}
