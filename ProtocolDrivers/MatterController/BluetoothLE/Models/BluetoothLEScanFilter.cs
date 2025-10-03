//-----------------------------------------------------------------------
// <copyright file="BluetoothLEScanFilter.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-24 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace InTheHand.Bluetooth
{
    /// <summary>
    /// Defines an individual filter to apply when requested devices.
    /// </summary>
    public sealed partial class BluetoothLEScanFilter
    {
        public IList<BluetoothUuid> Services { get; } = new List<BluetoothUuid>();
    }
}
