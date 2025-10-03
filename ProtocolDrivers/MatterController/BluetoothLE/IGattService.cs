//-----------------------------------------------------------------------
// <copyright file="GattService.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    /// <summary>
    /// Represents a GATT Service, a collection of characteristics and relationships to other services that encapsulate the behavior of part of a device.
    /// </summary>
    public interface IGattService
    {
        public Task<IGattCharacteristic> GetCharacteristicAsync(BluetoothUuid characteristic);
    }
}
