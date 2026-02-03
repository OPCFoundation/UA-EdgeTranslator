//-----------------------------------------------------------------------
// <copyright file="GattCharacteristic.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023-24 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if !WINDOWS

using Linux.Bluetooth;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    public class GattCharacteristicLinux : IGattCharacteristic
    {
        private readonly IGattCharacteristic1 _characteristicLinux;
        private IDisposable _eventHandler;
        private GattCharacteristicProperties _properties;

        private event EventHandler<GattCharacteristicValueChangedEventArgs> _characteristicValueChanged;

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

        private void AddCharacteristicValueChanged()
        {
            _ = Task.Run(async () =>
            {
                _eventHandler = await _characteristicLinux.WatchPropertiesAsync(OnCharacteristicValueChangedInternal).ConfigureAwait(false);
            });
        }

        private void OnCharacteristicValueChangedInternal(Tmds.DBus.PropertyChanges changes)
        {
            foreach (var change in changes.Changed)
            {
                if (change.Key == "Value")
                {
                    _characteristicValueChanged?.Invoke(this, new GattCharacteristicValueChangedEventArgs((byte[])change.Value));
                }
            }
        }

        private void RemoveCharacteristicValueChanged()
        {
            if (_eventHandler != null)
            {
                _eventHandler.Dispose();
                _eventHandler = null;
            }
        }

        internal GattCharacteristicLinux(IGattCharacteristic1 characteristic, BluetoothUuid uuid)
        {
            _characteristicLinux = characteristic;
        }

        internal async Task Init()
        {
            await UpdateCharacteristicProperties().ConfigureAwait(false);
        }

        private async Task UpdateCharacteristicProperties()
        {
            string[] flags = await GattCharacteristic1Extensions.GetFlagsAsync(_characteristicLinux).ConfigureAwait(false);

            _properties = GattCharacteristicProperties.None;
            foreach (var flag in flags)
            {
                switch (flag)
                {
                    case "broadcast":
                        _properties |= GattCharacteristicProperties.Broadcast;
                        break;

                    case "read":
                        _properties |= GattCharacteristicProperties.Read;
                        break;

                    case "write-without-response":
                        _properties |= GattCharacteristicProperties.WriteWithoutResponse;
                        break;

                    case "write":
                        _properties |= GattCharacteristicProperties.Write;
                        break;

                    case "notify":
                        _properties |= GattCharacteristicProperties.Notify;
                        break;

                    case "indicate":
                        _properties |= GattCharacteristicProperties.Indicate;
                        break;

                    case "authenticated-signed-writes":
                        _properties |= GattCharacteristicProperties.AuthenticatedSignedWrites;
                        break;

                    case "extended-properties":
                        _properties |= GattCharacteristicProperties.ExtendedProperties;
                        break;

                    case "reliable-write":
                        _properties |= GattCharacteristicProperties.ReliableWrites;
                        break;

                    case "writable-auxiliaries":
                        _properties |= GattCharacteristicProperties.WriteableAuxiliaries;
                        break;

                    /* Not handled values:
                       "encrypt-read"
                       "encrypt-write"
                       "encrypt-notify" (Server only)
                       "encrypt-indicate" (Server only)
                       "encrypt-authenticated-read"
                       "encrypt-authenticated-write"
                       "encrypt-authenticated-notify" (Server only)
                       "encrypt-authenticated-indicate" (Server only)
                       "secure-read" (Server only)
                       "secure-write" (Server only)
                       "secure-notify" (Server only)
                       "secure-indicate" (Server only)
                       "authorize"
                     */
                }
            }
        }

        public async Task WriteAsync(byte[] value)
        {
            var options = new Dictionary<string, object> {
                { "type", "request" }
            };

            await _characteristicLinux.WriteValueAsync(value, options).ConfigureAwait(false);
        }

        public Task StartNotificationsAsync()
        {
            if (_properties.HasFlag(GattCharacteristicProperties.Notify) || _properties.HasFlag(GattCharacteristicProperties.Indicate))
            {
                return _characteristicLinux.StartNotifyAsync();
            }

            return Task.CompletedTask;
        }

        public Task StopNotificationsAsync()
        {
            return _characteristicLinux.StopNotifyAsync();
        }
    }
}

#endif
