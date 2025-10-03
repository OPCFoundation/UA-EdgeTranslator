//-----------------------------------------------------------------------
// <copyright file="RemoteGattServer.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using System;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    public class RemoteGattServerLinux : IRemoteGattServer
    {
        public IBluetoothDevice Device { get; set; } = new BluetoothDeviceLinux();

        public bool IsConnected { get; set; }

        public int Mtu { get; set; }

        public async Task ConnectAsync()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(8);

            if (await ((BluetoothDeviceLinux)Device).NativeDevice.GetPairedAsync().ConfigureAwait(false) == false)
            {
                await ((BluetoothDeviceLinux)Device).NativeDevice.PairAsync().ConfigureAwait(false);
            }

            await ((BluetoothDeviceLinux)Device).NativeDevice.ConnectAsync().ConfigureAwait(false);
            await ((BluetoothDeviceLinux)Device).NativeDevice.WaitForPropertyValueAsync("Connected", value: true, timeout: timeout).ConfigureAwait(false);
            await ((BluetoothDeviceLinux)Device).NativeDevice.WaitForPropertyValueAsync("ServicesResolved", value: true, timeout: timeout).ConfigureAwait(false);
        }

        public async Task<IGattService> GetPrimaryServiceAsync(BluetoothUuid service)
        {
            string uuid = service.Value.ToString();
            var gattService = await ((BluetoothDeviceLinux)Device).NativeDevice.GetServiceAsync(uuid).ConfigureAwait(false);
            if(gattService != null)
            {
                return new GattServiceLinux(gattService);
            }

            return null;
        }
    }
}
