//-----------------------------------------------------------------------
// <copyright file="GattService.windows.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-22 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if WINDOWS

using System;
using System.Threading.Tasks;
using WBluetooth = Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace InTheHand.Bluetooth
{
    partial class GattServiceWindows : IGattService
    {
        private readonly WBluetooth.GattDeviceService _service;
        private readonly WBluetooth.GattSession _session;
        private readonly bool _isPrimary;

        internal GattServiceWindows(IBluetoothDevice device, WBluetooth.GattDeviceService service, bool isPrimary)
        {
            _service = service;
            _session = _service.Session;
            _isPrimary = isPrimary;
        }

        public async Task<bool> OpenAsync()
        {
            return IsOpenSuccess(await _service.OpenAsync(WBluetooth.GattSharingMode.SharedReadAndWrite));
        }

        private bool IsOpenSuccess(WBluetooth.GattOpenStatus status)
        {
            switch (status)
            {
                case WBluetooth.GattOpenStatus.Success:
                case WBluetooth.GattOpenStatus.AlreadyOpened:
                    return true;

                default:
                    return false;
            }
        }

        public async Task<IGattCharacteristic> GetCharacteristicAsync(BluetoothUuid characteristic)
        {
            if (_service.Session.SessionStatus != WBluetooth.GattSessionStatus.Active)
            {
                if (!await OpenAsync())
                {
                    return null;
                }
            }

            var result = await _service.GetCharacteristicsForUuidAsync(characteristic);

            if (result.Status == WBluetooth.GattCommunicationStatus.Success && result.Characteristics.Count > 0)
                return new GattCharacteristicWindows(this, result.Characteristics[0]);

            return null;
        }
    }
}

#endif

