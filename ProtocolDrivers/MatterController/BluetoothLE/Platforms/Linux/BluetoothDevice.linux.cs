//-----------------------------------------------------------------------
// <copyright file="BluetoothDevice.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using Linux.Bluetooth;
using System;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    public class BluetoothDeviceLinux : IBluetoothDevice
    {
        public event EventHandler GattServerDisconnected;

        public string Id { get; set; }

        public Device NativeDevice { get; set; }

        public void OnGattServerDisconnected()
        {
            GattServerDisconnected?.Invoke(this, EventArgs.Empty);
        }

        public IRemoteGattServer GattServer { get; set; }

        public BluetoothDeviceLinux()
        {
            NativeDevice = new Device();
            NativeDevice.Disconnected += _device_Disconnected;
            Id = NativeDevice.GetNameAsync().GetAwaiter().GetResult();
        }

        private Task _device_Disconnected(Device sender, BlueZEventArgs eventArgs)
        {
            if(eventArgs.IsStateChange)
            {
                GattServerDisconnected?.Invoke(this, EventArgs.Empty);
            }

            return Task.CompletedTask;
        }
    }
}
