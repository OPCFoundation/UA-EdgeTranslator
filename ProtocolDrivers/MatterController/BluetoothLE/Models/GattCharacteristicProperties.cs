﻿//-----------------------------------------------------------------------
// <copyright file="GattCharacteristicProperties.cs" company="In The Hand Ltd">
//   Copyright (c) 2015-20 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace InTheHand.Bluetooth
{
    /// <summary>
    /// Specifies the values for the GATT characteristic properties as well as the GATT Extended Characteristic Properties Descriptor.
    /// </summary>
    [Flags]
    public enum GattCharacteristicProperties
    {
        /// <summary>
        /// The characteristic doesn’t have any properties that apply.
        /// </summary>
        None = 0,

        /// <summary>
        /// The characteristic supports broadcasting
        /// </summary>
        Broadcast = 1,

        /// <summary>
        /// The characteristic is readable
        /// </summary>
        Read = 2,

        /// <summary>
        /// The characteristic supports Write Without Response
        /// </summary>
        WriteWithoutResponse = 4,

        /// <summary>
        /// The characteristic is writable
        /// </summary>
        Write = 8,

        /// <summary>
        /// The characteristic is notifiable
        /// </summary>
        Notify = 16,

        /// <summary>
        /// The characteristic supports indicate notifications
        /// </summary>
        Indicate = 32,

        /// <summary>
        /// The characteristic supports signed writes
        /// </summary>
        AuthenticatedSignedWrites = 64,

        /// <summary>
        /// The ExtendedProperties Descriptor is present
        /// </summary>
        ExtendedProperties = 128,

        /// <summary>
        /// The characteristic supports reliable writes
        /// </summary>
        ReliableWrites = 256,

        /// <summary>
        /// The characteristic has writable auxiliaries
        /// </summary>
        WriteableAuxiliaries = 512,
    }
}
