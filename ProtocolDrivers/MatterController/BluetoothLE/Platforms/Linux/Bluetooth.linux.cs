//-----------------------------------------------------------------------
// <copyright file="Bluetooth.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if !WINDOWS

using Linux.Bluetooth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth;

public class BluetoothLinux : IBluetooth
{
    internal Adapter Adapter;

    public event EventHandler<IBluetoothAdvertisingEvent> AdvertisementReceived;

    public async Task StartLEScanAsync()
    {
        if (Adapter == null)
        {
            Adapter = (await BlueZManager.GetAdaptersAsync().ConfigureAwait(false)).FirstOrDefault();
            if (Adapter == null)
            {
                throw new PlatformNotSupportedException("No IBluetooth adapter present.");
            }

            Console.WriteLine($"Using BT adapter {Adapter.Name}");

            Adapter.DeviceFound += Adapter_DeviceFound;
        }

        await Adapter.StartDiscoveryAsync().ConfigureAwait(false);
    }

    public Task StopLEScanAsync()
    {
        Adapter.StopDiscoveryAsync();

        return Task.CompletedTask;
    }

    private async Task Adapter_DeviceFound(Adapter sender, DeviceFoundEventArgs eventArgs)
    {
        try
        {
            string btAddress = await eventArgs.Device.GetAddressAsync().ConfigureAwait(false);
            Console.WriteLine($"BT Device found: {btAddress}");

            IDictionary<string, object> serviceData = await eventArgs.Device.GetServiceDataAsync().ConfigureAwait(false);
            if ((serviceData == null) && (serviceData.Count == 0))
            {
                Console.WriteLine("No BT service data!");
            }
            else
            {
                AdvertisementReceived?.Invoke(this, new BluetoothAdvertisingEventLinux(eventArgs.Device, btAddress, serviceData));
            }
        }
        catch (Exception)
        {
            return; // do nothing
        }
    }
}

#endif
