//-----------------------------------------------------------------------
// <copyright file="GattService.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023-24 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    public class GattServiceLinux : IGattService
    {
        private IGattService1 _service;

        internal GattServiceLinux(IGattService1 service)
        {
            _service = service;
        }

        public async Task<IGattCharacteristic> GetCharacteristicAsync(BluetoothUuid characteristic)
        {
            var linuxCharacteristic = await _service.GetCharacteristicAsync(characteristic.Value.ToString());

            if (linuxCharacteristic != null)
            {
                var gattCharacteristic = new GattCharacteristicLinux(linuxCharacteristic, characteristic);
                await gattCharacteristic.Init().ConfigureAwait(false);

                return gattCharacteristic;
            }

            return null;
        }
    }
}
