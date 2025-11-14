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

    public async Task StartLEScanAsync(BluetoothLEScanOptions options)
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
            Device1Properties properties = await eventArgs.Device.GetAllAsync().ConfigureAwait(false);
            if (properties.UUIDs == null || properties.UUIDs.Length == 0)
            {
                return;
            }
            else
            {
                AdvertisementReceived?.Invoke(this, new BluetoothAdvertisingEventLinux(eventArgs.Device, properties.Name, properties.UUIDs));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing Linux BTLE advertisement: {ex}");
        }
    }
}

#endif
