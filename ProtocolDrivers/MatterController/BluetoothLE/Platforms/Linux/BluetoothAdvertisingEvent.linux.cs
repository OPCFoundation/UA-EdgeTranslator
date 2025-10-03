//-----------------------------------------------------------------------
// <copyright file="BluetoothAdvertisingEvent.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using Linux.Bluetooth;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace InTheHand.Bluetooth
{
    partial class BluetoothAdvertisingEventLinux : IBluetoothAdvertisingEvent
    {
        private ushort _appearance;

        internal BluetoothAdvertisingEventLinux(Device device, ushort appearance)
        {
            _appearance = appearance;
            Device.Id = device.GetNameAsync().GetAwaiter().GetResult();
        }

        public IBluetoothDevice Device { get; set; } = new BluetoothDeviceLinux();

        public IReadOnlyDictionary<ushort, byte[]> ManufacturerData()
        {
            var manufacturerData = new Dictionary<ushort, byte[]>();

            return new ReadOnlyDictionary<ushort, byte[]>(manufacturerData);
        }

        public IReadOnlyDictionary<BluetoothUuid, byte[]> ServiceData()
        {
            var serviceData = new Dictionary<BluetoothUuid, byte[]>();

            return new ReadOnlyDictionary<BluetoothUuid, byte[]>(serviceData);
        }
    }
}
