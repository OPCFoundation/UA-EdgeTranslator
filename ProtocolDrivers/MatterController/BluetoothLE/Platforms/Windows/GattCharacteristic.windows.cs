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
using Uap = Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace InTheHand.Bluetooth
{
    public class GattCharacteristicWindows : IGattCharacteristic
    {
        private readonly Uap.GattCharacteristic _characteristic;

        public IGattService Service { get; set; }

        private EventHandler<GattCharacteristicValueChangedEventArgs> _characteristicValueChanged;

        public event EventHandler<GattCharacteristicValueChangedEventArgs> CharacteristicValueChanged
        {
            add
            {
                _characteristicValueChanged += value;
                AddCharacteristicValueChanged();
            }
            remove
            {
                _characteristicValueChanged -= value;
                RemoveCharacteristicValueChanged();
            }
        }

        public void OnCharacteristicValueChanged(GattCharacteristicValueChangedEventArgs args)
        {
            _characteristicValueChanged?.Invoke(this, args);
        }

        internal GattCharacteristicWindows(IGattService service, Uap.GattCharacteristic characteristic)
        {
            _characteristic = characteristic;
            Service = service;
        }

        public GattCharacteristicProperties Properties { get; set; }

        public async Task WriteValue(byte[] value, bool requireResponse)
        {
            await _characteristic.WriteValueAsync(value.AsBuffer(), requireResponse ? Uap.GattWriteOption.WriteWithResponse : Uap.GattWriteOption.WriteWithoutResponse);
        }

        private void AddCharacteristicValueChanged()
        {
            _characteristic.ValueChanged += Characteristic_ValueChanged;
        }

        private void Characteristic_ValueChanged(Uap.GattCharacteristic sender, Uap.GattValueChangedEventArgs args)
        {
            OnCharacteristicValueChanged(new GattCharacteristicValueChangedEventArgs(args.CharacteristicValue.Length == 0 ? [] : args.CharacteristicValue.ToArray()));
        }

        void RemoveCharacteristicValueChanged()
        {
            _characteristic.ValueChanged -= Characteristic_ValueChanged;
        }

        public async Task StartNotificationsAsync()
        {
            var value = Uap.GattClientCharacteristicConfigurationDescriptorValue.None;
            if (_characteristic.CharacteristicProperties.HasFlag(Uap.GattCharacteristicProperties.Notify))
                value = Uap.GattClientCharacteristicConfigurationDescriptorValue.Notify;
            else if (_characteristic.CharacteristicProperties.HasFlag(Uap.GattCharacteristicProperties.Indicate))
                value = Uap.GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            else
                return;

            try
            {
                var result = await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(value);
            }
            catch(UnauthorizedAccessException)
            {
                // not supported
            }
        }

        public async Task StopNotificationsAsync()
        {
            try
            {
                var result = await _characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(Uap.GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch
            {
                throw new NotSupportedException();
                // HRESULT 0x800704D6 means that a connection to the server could not be made because the limit on the number of concurrent connections for this account has been reached.
            }
        }
    }
}

#endif

