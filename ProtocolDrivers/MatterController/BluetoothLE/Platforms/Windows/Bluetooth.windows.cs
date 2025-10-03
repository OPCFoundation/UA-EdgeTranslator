//-----------------------------------------------------------------------
// <copyright file="Bluetooth.windows.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-24 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

#if WINDOWS

using System;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Advertisement;

namespace InTheHand.Bluetooth
{
    public class BluetoothWindows : IBluetooth
    {
        public event EventHandler<IBluetoothAdvertisingEvent> AdvertisementReceived;

        private BluetoothLEAdvertisementWatcher _watcher;

        public Task StartLEScanAsync(BluetoothLEScanOptions options = null)
        {
            _watcher = new BluetoothLEAdvertisementWatcher();
            _watcher.Received += Watcher_Received;

            _watcher.ScanningMode = BluetoothLEScanningMode.Active;

            _watcher.Start();

            return Task.CompletedTask;
        }

        public Task StopLEScanAsync()
        {
            _watcher.Stop();

            return Task.CompletedTask;
        }

        public void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            if (args.IsConnectable)
            {
                AdvertisementReceived?.Invoke(this, new BluetoothAdvertisingEventWindows(args));
            }
        }
    }
}

#endif
