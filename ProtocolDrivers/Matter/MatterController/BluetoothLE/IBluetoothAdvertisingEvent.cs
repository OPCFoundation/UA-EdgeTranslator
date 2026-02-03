//-----------------------------------------------------------------------
// <copyright file="BluetoothAdvertisingEvent.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-24 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace InTheHand.Bluetooth
{
    public interface IBluetoothAdvertisingEvent
    {
        public IBluetoothDevice Device { get; set; }

        public IReadOnlyDictionary<BluetoothUuid, byte[]> ServiceData();
    }
}
