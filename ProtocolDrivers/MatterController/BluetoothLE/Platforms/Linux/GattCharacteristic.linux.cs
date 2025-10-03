//-----------------------------------------------------------------------
// <copyright file="GattCharacteristic.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023-24 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using Linux.Bluetooth;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    public class GattCharacteristicLinux : IGattCharacteristic
    {
        private readonly IGattCharacteristic1 _characteristic;

        public event EventHandler<GattCharacteristicValueChangedEventArgs> CharacteristicValueChanged;

        public void OnCharacteristicValueChanged(GattCharacteristicValueChangedEventArgs args)
        {
            CharacteristicValueChanged?.Invoke(this, args);
        }

        public GattCharacteristicProperties Properties { get; set; }

        internal GattCharacteristicLinux(IGattCharacteristic1 characteristic, BluetoothUuid uuid)
        {
            _characteristic = characteristic;
        }

        internal async Task Init()
        {
            await UpdateCharacteristicProperties();
        }

        private async Task UpdateCharacteristicProperties()
        {
            string[] flags = await GattCharacteristic1Extensions.GetFlagsAsync(_characteristic);

            var characteristicProperties = GattCharacteristicProperties.None;
            foreach (var flag in flags)
            {
                switch (flag)
                {
                    case "broadcast":
                        characteristicProperties |= GattCharacteristicProperties.Broadcast;
                        break;

                    case "read":
                        characteristicProperties |= GattCharacteristicProperties.Read;
                        break;

                    case "write-without-response":
                        characteristicProperties |= GattCharacteristicProperties.WriteWithoutResponse;
                        break;

                    case "write":
                        characteristicProperties |= GattCharacteristicProperties.Write;
                        break;

                    case "notify":
                        characteristicProperties |= GattCharacteristicProperties.Notify;
                        break;

                    case "indicate":
                        characteristicProperties |= GattCharacteristicProperties.Indicate;
                        break;

                    case "authenticated-signed-writes":
                        characteristicProperties |= GattCharacteristicProperties.AuthenticatedSignedWrites;
                        break;

                    case "extended-properties":
                        characteristicProperties |= GattCharacteristicProperties.ExtendedProperties;
                        break;

                    case "reliable-write":
                        characteristicProperties |= GattCharacteristicProperties.ReliableWrites;
                        break;

                    case "writable-auxiliaries":
                        characteristicProperties |= GattCharacteristicProperties.WriteableAuxiliaries;
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

            Properties = characteristicProperties;
        }

        public async Task WriteValue(byte[] value, bool requireResponse)
        {
            var options = new Dictionary<string, object> {
                { "type", requireResponse ? "request" : "command" }
            };

            await _characteristic.WriteValueAsync(value, options).ConfigureAwait(false);
        }

        public Task StartNotificationsAsync()
        {
            if (Properties.HasFlag(GattCharacteristicProperties.Notify) | Properties.HasFlag(GattCharacteristicProperties.Indicate))
            {
                return _characteristic.StartNotifyAsync();
            }

            return Task.CompletedTask;
        }

        public Task StopNotificationsAsync()
        {
            return _characteristic.StopNotifyAsync();
        }
    }
}
