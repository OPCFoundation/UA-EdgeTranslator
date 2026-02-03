//-----------------------------------------------------------------------
// <copyright file="GattDescriptor.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-25 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;

namespace InTheHand.Bluetooth
{
    /// <summary>
    /// Represents a GATT Descriptor, which provides further information about a <see cref="IGattCharacteristic"/>’s value.
    /// </summary>
    [DebuggerDisplay("{Uuid} (Descriptor)")]
    public class GattDescriptor
    {
        internal GattDescriptor(IGattCharacteristic characteristic)
        {
            Characteristic = characteristic;
        }

        /// <summary>
        /// The GATT characteristic this descriptor belongs to.
        /// </summary>
        public IGattCharacteristic Characteristic { get; private set; }
    }
}
