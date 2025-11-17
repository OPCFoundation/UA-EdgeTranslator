//-----------------------------------------------------------------------
// <copyright file="RemoteGattServer.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if !WINDOWS

using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using System;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    public class RemoteGattServerLinux : IRemoteGattServer
    {
        public IBluetoothDevice Device { get; set; }

        public bool IsConnected { get; set; }

        public int Mtu { get; set; }

        public async Task ConnectAsync()
        {
            TimeSpan timeout = TimeSpan.FromSeconds(15);

            if (await ((BluetoothDeviceLinux)Device).NativeDevice.GetPairedAsync().ConfigureAwait(false) == false)
            {
                await ((BluetoothDeviceLinux)Device).NativeDevice.PairAsync().ConfigureAwait(false);
            }

            await ((BluetoothDeviceLinux)Device).NativeDevice.ConnectAsync().ConfigureAwait(false);
            await ((BluetoothDeviceLinux)Device).NativeDevice.WaitForPropertyValueAsync("Connected", true, timeout).ConfigureAwait(false);
            await ((BluetoothDeviceLinux)Device).NativeDevice.WaitForPropertyValueAsync("ServicesResolved", true, timeout).ConfigureAwait(false);
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

#endif
