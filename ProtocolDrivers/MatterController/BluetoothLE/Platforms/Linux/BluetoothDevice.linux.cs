//-----------------------------------------------------------------------
// <copyright file="BluetoothDevice.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if !WINDOWS

using Linux.Bluetooth;
using System;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    public class BluetoothDeviceLinux : IBluetoothDevice
    {
        public Device NativeDevice { get; set; }

        public string Id { get; set; }

        public IRemoteGattServer GattServer { get; set; }

        public event EventHandler GattServerDisconnected;

        public void OnGattServerDisconnected()
        {
            GattServerDisconnected?.Invoke(this, EventArgs.Empty);
        }

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

#endif
