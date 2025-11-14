//-----------------------------------------------------------------------
// <copyright file="BluetoothAdvertisingEvent.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if !WINDOWS

using libplctag.DataTypes;
using Linux.Bluetooth;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace InTheHand.Bluetooth
{
    partial class BluetoothAdvertisingEventLinux : IBluetoothAdvertisingEvent
    {
        public IBluetoothDevice Device { get; set; }

        private readonly string[] _advertisement;

        public BluetoothAdvertisingEventLinux(Device device, string[] data)
        {
            Device = new BluetoothDeviceLinux
            {
                Id = device.GetNameAsync().GetAwaiter().GetResult(),
                GattServer = new RemoteGattServerLinux(),
                NativeDevice = device
            };

            Device.GattServer.Device = Device;

            _advertisement = data;
        }

        public IReadOnlyDictionary<BluetoothUuid, byte[]> ServiceData()
        {
            var serviceData = new Dictionary<BluetoothUuid, byte[]>();

            try
            {
                foreach (string data in _advertisement)
                {
                    serviceData.Add(new Guid(data), Array.Empty<byte>());
                }
            }
            catch
            {
                Console.WriteLine("Error parsing service data");
            }

            return serviceData;
        }
    }
}

#endif
