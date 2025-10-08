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
        public GattCharacteristicProperties Properties { get; set; }

        public Task WriteValueWithResponseAsync(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length > 512)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Value cannot be larger than 512 bytes.");
            }

            return WriteValue(value, true);
        }

        public Task WriteValue(byte[] value, bool requireResponse);

        public event EventHandler<GattCharacteristicValueChangedEventArgs> CharacteristicValueChanged;

        public void OnCharacteristicValueChanged(GattCharacteristicValueChangedEventArgs args);

        public Task StartNotificationsAsync();

        public Task StopNotificationsAsync();
    }
}
