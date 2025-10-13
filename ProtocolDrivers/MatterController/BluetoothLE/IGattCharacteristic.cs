//-----------------------------------------------------------------------
// <copyright file="GattCharacteristic.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    /// <summary>
    /// Represents a GATT Characteristic, which is a basic data element that provides further information about a peripheral’s service.
    /// </summary>
    public interface IGattCharacteristic
    {
        public Task WriteAsync(byte[] value);

        public event EventHandler<GattCharacteristicValueChangedEventArgs> CharacteristicValueChanged;

        public Task StartNotificationsAsync();

        public Task StopNotificationsAsync();
    }
}
