//-----------------------------------------------------------------------
// <copyright file="BluetoothDevice.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace InTheHand.Bluetooth
{
    /// <summary>
    /// A BluetoothDevice instance represents a remote Bluetooth device
    /// </summary>
    public interface IBluetoothDevice
    {
        public string Id { get; set; }

        public IRemoteGattServer GattServer { get; set; }

        public void OnGattServerDisconnected();

        public event EventHandler GattServerDisconnected;
    }
}
