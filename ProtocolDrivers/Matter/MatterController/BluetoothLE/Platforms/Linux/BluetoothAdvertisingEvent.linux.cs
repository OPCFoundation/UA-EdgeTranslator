//-----------------------------------------------------------------------
// <copyright file="BluetoothAdvertisingEvent.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if !WINDOWS

using Linux.Bluetooth;
using System;
using System.Collections.Generic;

namespace InTheHand.Bluetooth
{
    partial class BluetoothAdvertisingEventLinux : IBluetoothAdvertisingEvent
    {
        public IBluetoothDevice Device { get; set; }

        private readonly IDictionary<string, object> _advertisement;

        public BluetoothAdvertisingEventLinux(Device device, string name, IDictionary<string, object> serviceData)
        {
            Device = new BluetoothDeviceLinux {
                Id = name,
                GattServer = new RemoteGattServerLinux(),
                NativeDevice = device
            };

            Device.GattServer.Device = Device;
            ((BluetoothDeviceLinux)Device).NativeDevice.Disconnected += ((BluetoothDeviceLinux)Device).Device_Disconnected;

            _advertisement = serviceData;
        }

        public IReadOnlyDictionary<BluetoothUuid, byte[]> ServiceData()
        {
            var serviceData = new Dictionary<BluetoothUuid, byte[]>();

            foreach (KeyValuePair<string, object> data in _advertisement)
            {
                serviceData.Add(new Guid(data.Key), (byte[])data.Value);
            }

            return serviceData;
        }
    }
}

#endif
