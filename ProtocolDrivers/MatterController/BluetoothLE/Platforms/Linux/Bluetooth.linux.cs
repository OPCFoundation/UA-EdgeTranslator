//-----------------------------------------------------------------------
// <copyright file="Bluetooth.linux.cs" company="In The Hand Ltd">
//   Copyright (c) 2023-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if !WINDOWS

using Linux.Bluetooth;
using System;
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
            Adapter = (await BlueZManager.GetAdaptersAsync()).FirstOrDefault();

            if (Adapter == null)
                throw new PlatformNotSupportedException("No IBluetooth adapter present.");

            Console.WriteLine($"Using BT adapter {Adapter.Name}");

            Adapter.DeviceFound += Adapter_DeviceFound;
        }

        Console.WriteLine("Starting BLE Scan...");
        await Adapter.StartDiscoveryAsync().ConfigureAwait(false);
    }

    public Task StopLEScanAsync()
    {
        Adapter.StopDiscoveryAsync();

        return Task.CompletedTask;
    }

    private async Task Adapter_DeviceFound(Adapter sender, DeviceFoundEventArgs eventArgs)
    {
        Device device = eventArgs.Device;
        ushort appearance = await eventArgs.Device.GetAppearanceAsync().ConfigureAwait(false);
        var eventInfo = new BluetoothAdvertisingEventLinux(device, appearance);

        AdvertisementReceived?.Invoke(this, eventInfo);
    }
}

#endif
