//-----------------------------------------------------------------------
// <copyright file="Bluetooth.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-24 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    public interface IBluetooth
    {
        public event EventHandler<IBluetoothAdvertisingEvent> AdvertisementReceived;

        public Task StartLEScanAsync();

        public Task StopLEScanAsync();
    }
}
