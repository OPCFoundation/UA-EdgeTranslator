//-----------------------------------------------------------------------
// <copyright file="BluetoothAdvertisingEvent.windows.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-24 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if WINDOWS

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace InTheHand.Bluetooth
{
    public class BluetoothAdvertisingEventWindows : IBluetoothAdvertisingEvent
    {
        private readonly BluetoothLEAdvertisement _advertisement;

        public IBluetoothDevice Device { get; set; } = new BluetoothDeviceWindows();

        public BluetoothAdvertisingEventWindows(BluetoothLEAdvertisementReceivedEventArgs args)
        {
            ((BluetoothDeviceWindows)Device).NativeDevice = BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress, args.BluetoothAddressType).GetAwaiter().GetResult();
            
            Device.Id = ((BluetoothDeviceWindows)Device).NativeDevice.BluetoothDeviceId.Id;

            Device.GattServer = new RemoteGattServerWindows();
            Device.GattServer.Device = Device;

            _advertisement = args.Advertisement;
        }

        public IReadOnlyDictionary<ushort,byte[]> ManufacturerData()
        {
            var manufacturerData = new Dictionary<ushort, byte[]>();

            foreach(BluetoothLEManufacturerData data in _advertisement.ManufacturerData)
            {
                manufacturerData.Add(data.CompanyId, data.Data.ToArray());
            }

            return new ReadOnlyDictionary<ushort,byte[]>(manufacturerData);
        }

        public IReadOnlyDictionary<BluetoothUuid, byte[]> ServiceData()
        {
            var serviceData = new Dictionary<BluetoothUuid, byte[]>();

            foreach (BluetoothLEAdvertisementDataSection data in _advertisement.DataSections)
            {
                var uuidBytes = new byte[16];

                if (data.DataType == BluetoothLEAdvertisementDataTypes.ServiceData128BitUuids)
                {
                    // read uuid
                    data.Data.CopyTo(0, uuidBytes, 0, 16);

                    // read data
                    byte[] dataBytes = new byte[data.Data.Length - 16];
                    data.Data.CopyTo(16, dataBytes, 0, dataBytes.Length);
                    serviceData.Add(new Guid(uuidBytes), dataBytes);
                }

                if (data.DataType == BluetoothLEAdvertisementDataTypes.ServiceData32BitUuids)
                {
                    // read uuid
                    data.Data.CopyTo(0, uuidBytes, 0, 4);

                    // read data
                    byte[] dataBytes = new byte[data.Data.Length - 4];
                    data.Data.CopyTo(4, dataBytes, 0, dataBytes.Length);
                    serviceData.Add(BluetoothUuid.FromShortId(BitConverter.ToUInt16(uuidBytes, 0)), dataBytes);
                }

                if (data.DataType == BluetoothLEAdvertisementDataTypes.ServiceData16BitUuids)
                {
                    // read uuid
                    data.Data.CopyTo(0, uuidBytes, 0, 2);

                    // read data
                    byte[] dataBytes = new byte[data.Data.Length - 2];
                    data.Data.CopyTo(2, dataBytes, 0, dataBytes.Length);
                    serviceData.Add(BluetoothUuid.FromShortId(BitConverter.ToUInt16(uuidBytes, 0)), dataBytes);
                }
            }

            return new ReadOnlyDictionary<BluetoothUuid, byte[]>(serviceData);
        }
    }
}

#endif
