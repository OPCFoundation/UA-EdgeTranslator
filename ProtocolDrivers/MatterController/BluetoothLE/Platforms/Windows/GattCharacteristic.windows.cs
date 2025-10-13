//-----------------------------------------------------------------------
// <copyright file="GattCharacteristic.windows.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if WINDOWS

using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace InTheHand.Bluetooth
{
    public class GattCharacteristicWindows : IGattCharacteristic
    {
        private readonly GattCharacteristic _characteristicWindows;

        private event EventHandler<GattCharacteristicValueChangedEventArgs> _characteristicValueChanged;

        public event EventHandler<GattCharacteristicValueChangedEventArgs> CharacteristicValueChanged
        {
            add
            {
                _characteristicValueChanged += value;
                _characteristicWindows.ValueChanged += OnCharacteristicValueChangedInternal;
            }

            remove
            {
                _characteristicValueChanged -= value;
                _characteristicWindows.ValueChanged -= OnCharacteristicValueChangedInternal;
            }
        }

        private void OnCharacteristicValueChangedInternal(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            _characteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs(args.CharacteristicValue.Length == 0 ? [] : args.CharacteristicValue.ToArray()));
        }

        public GattCharacteristicWindows(GattCharacteristic characteristic)
        {
            _characteristicWindows = characteristic;
        }

        public async Task WriteAsync(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length > 512)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Value cannot be larger than 512 bytes.");
            }

            await _characteristicWindows.WriteValueAsync(value.AsBuffer(), GattWriteOption.WriteWithResponse);
        }

        public async Task StartNotificationsAsync()
        {
            var value = GattClientCharacteristicConfigurationDescriptorValue.None;

            if (_characteristicWindows.CharacteristicProperties.HasFlag(Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicProperties.Notify))
            {
                value |= GattClientCharacteristicConfigurationDescriptorValue.Notify;
            }

            if (_characteristicWindows.CharacteristicProperties.HasFlag(Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicProperties.Indicate))
            {
                value |= GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            }

            try
            {
                await _characteristicWindows.WriteClientCharacteristicConfigurationDescriptorAsync(value);
            }
            catch (Exception)
            {
                throw new NotSupportedException("Notifications not supported for this GATT characteristic!");
            }
        }

        public async Task StopNotificationsAsync()
        {
            try
            {
                await _characteristicWindows.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch (Exception)
            {
                throw new NotSupportedException("Notifications not supported for this GATT characteristic!");
            }
        }
    }
}

#endif

