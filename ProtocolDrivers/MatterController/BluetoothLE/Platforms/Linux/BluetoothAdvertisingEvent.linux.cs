//-----------------------------------------------------------------------
// <copyright file="BluetoothAdvertisingEvent.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if !WINDOWS

using Linux.Bluetooth;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace InTheHand.Bluetooth
{
    partial class BluetoothAdvertisingEventLinux : IBluetoothAdvertisingEvent
    {
        public IBluetoothDevice Device { get; set; } = new BluetoothDeviceLinux();

        public BluetoothAdvertisingEventLinux(Device device, ushort appearance)
        {
            Device.Id = device.GetNameAsync().GetAwaiter().GetResult();
            Device.GattServer = new RemoteGattServerLinux();
            Device.GattServer.Device = Device;
        }

        public IReadOnlyDictionary<BluetoothUuid, byte[]> ServiceData()
        {
            var serviceData = new Dictionary<BluetoothUuid, byte[]>();

            return new ReadOnlyDictionary<BluetoothUuid, byte[]>(serviceData);
        }
    }
}

#endif
