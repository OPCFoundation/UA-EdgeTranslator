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
using System.Collections.ObjectModel;

namespace InTheHand.Bluetooth
{
    partial class BluetoothAdvertisingEventLinux : IBluetoothAdvertisingEvent
    {
        public IBluetoothDevice Device { get; set; } = new BluetoothDeviceLinux();

        private readonly IDictionary<string, object> _advertisement;

        public BluetoothAdvertisingEventLinux(Device device, IDictionary<string, object> serviceData)
        {
            Device.Id = device.GetNameAsync().GetAwaiter().GetResult();
            Device.GattServer = new RemoteGattServerLinux();
            Device.GattServer.Device = Device;

            _advertisement = serviceData;
        }

        public IReadOnlyDictionary<BluetoothUuid, byte[]> ServiceData()
        {
            var serviceData = new Dictionary<BluetoothUuid, byte[]>();

            foreach (KeyValuePair<string, object> data in _advertisement)
            {
                var uuidBytes = new byte[16];

                //if (data.DataType == BluetoothLEAdvertisementDataTypes.ServiceData128BitUuids)
                //{
                //    // read uuid
                //    data.Data.CopyTo(0, uuidBytes, 0, 16);

                //    // read data
                //    byte[] dataBytes = new byte[data.Data.Length - 16];
                //    data.Data.CopyTo(16, dataBytes, 0, dataBytes.Length);
                //    serviceData.Add(new Guid(uuidBytes), dataBytes);
                //}

                //if (data.DataType == BluetoothLEAdvertisementDataTypes.ServiceData32BitUuids)
                //{
                //    // read uuid
                //    data.Data.CopyTo(0, uuidBytes, 0, 4);

                //    // read data
                //    byte[] dataBytes = new byte[data.Data.Length - 4];
                //    data.Data.CopyTo(4, dataBytes, 0, dataBytes.Length);
                //    serviceData.Add(BluetoothUuid.FromShortId(BitConverter.ToUInt16(uuidBytes, 0)), dataBytes);
                //}

                //if (data.DataType == BluetoothLEAdvertisementDataTypes.ServiceData16BitUuids)
                //{
                //    // read uuid
                //    data.Data.CopyTo(0, uuidBytes, 0, 2);

                //    // read data
                //    byte[] dataBytes = new byte[data.Data.Length - 2];
                //    data.Data.CopyTo(2, dataBytes, 0, dataBytes.Length);
                //    serviceData.Add(BluetoothUuid.FromShortId(BitConverter.ToUInt16(uuidBytes, 0)), dataBytes);
                //}
            }

            return new ReadOnlyDictionary<BluetoothUuid, byte[]>(serviceData);
        }
    }
}

#endif
