﻿//-----------------------------------------------------------------------
// <copyright file="BluetoothUuid.cs" company="In The Hand Ltd">
//   Copyright (c) 2020 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;

namespace InTheHand.Bluetooth
{
    /// <summary>
    /// Represents a Bluetooth UUID. Can be expressed as a Guid or a short ID for documented Bluetooth SIG definitions.
    /// </summary>
    [DebuggerDisplay("{ToString()}")]
    public partial struct BluetoothUuid
    {
        internal static readonly Guid BluetoothBase = new Guid(0x00000000, 0x0000, 0x1000, 0x80, 0x00, 0x00, 0x80, 0x5F, 0x9B, 0x34, 0xFB);

        private Guid _uuid;

        private BluetoothUuid(Guid uuid)
        {
            _uuid = uuid;
        }

        public static implicit operator Guid(BluetoothUuid uuid)
        {
            return uuid._uuid;
        }

        public static implicit operator BluetoothUuid(Guid uuid)
        {
            return new BluetoothUuid(uuid);
        }

        public static implicit operator BluetoothUuid(ushort uuid)
        {
            return FromShortId(uuid);
        }

        public static explicit operator BluetoothUuid(string uuid)
        {
            return FromId(uuid);
        }

        public static explicit operator ushort(BluetoothUuid uuid)
        {
            var val = TryGetShortId(uuid);
            if (val.HasValue)
                return val.Value;

            return default;
        }

        /// <summary>
        /// Returns true if both Uuids are equal.
        /// </summary>
        /// <param name="uuid1"></param>
        /// <param name="uuid2"></param>
        /// <returns></returns>
        public static bool operator ==(BluetoothUuid uuid1, BluetoothUuid uuid2)
        {
            return uuid1.Value == uuid2.Value;
        }

        /// <summary>
        /// Returns the bluetooth device UUID from a Guid.
        /// </summary>
        /// <param name="uuid"></param>
        /// <returns></returns>
        public static BluetoothUuid FromGuid(Guid uuid)
        {
            return new BluetoothUuid(uuid);
        }

        /// <summary>
        /// Returns true if both Uuids are unequal.
        /// </summary>
        /// <param name="uuid1"></param>
        /// <param name="uuid2"></param>
        /// <returns></returns>
        public static bool operator !=(BluetoothUuid uuid1, BluetoothUuid uuid2)
        {
            return uuid1.Value != uuid2.Value;
        }

        /// <summary>
        /// The full underlying value as a <see cref="Guid"/>.
        /// </summary>
        [DebuggerDisplay("{0}")]
        public Guid Value
        {
            get
            {
                return _uuid;
            }
        }

        /// <summary>
        /// A default Uuid containing a zero Guid.
        /// </summary>
        /// <value>This is not a valid value so be careful passing this to any native function.</value>
        public static BluetoothUuid Empty => default;

        /// <summary>
        /// Returns the bluetooth device UUID from a short ID.
        /// </summary>
        /// <param name="shortId">The short ID.</param>
        /// <returns>Returns the UUID.</returns>
        public static BluetoothUuid FromShortId(ushort shortId)
        {
            var guidBytes = BluetoothBase.ToByteArray();
            BitConverter.GetBytes(shortId).CopyTo(guidBytes, 0);
            return new Guid(guidBytes);
        }

        /// <summary>
        /// Attempts to get the short Bluetooth ID from a UUID.
        /// </summary>
        /// <param name="uuid">The UUID.</param>
        /// <returns>Returns the short ID.</returns>
        public static ushort? TryGetShortId(Guid uuid)
        {
            var bytes = uuid.ToByteArray();
            var baseBytes = BluetoothBase.ToByteArray();
            var match = true;
            for (var i = 4; i < 16; i++)
            {
                if (bytes[i] != baseBytes[i])
                {
                    match = false;
                    break;
                }
            }

            return match ? BitConverter.ToUInt16(bytes, 0) : null;
        }

        /// <summary>
        /// Returns the Uuid for a given Bluetooth SIG name.
        /// Names must begin with the "org.bluetooth" prefix.
        /// </summary>
        /// <param name="uuidName"></param>
        /// <returns></returns>
        public static BluetoothUuid FromId(string uuidName)
        {
            if(uuidName.StartsWith(GattServiceUuids.Namespace))
            {
                return GattServiceUuids.FromBluetoothUti(uuidName);
            }
            else if(uuidName.StartsWith(GattCharacteristicUuids.Namespace))
            {
                return GattCharacteristicUuids.FromBluetoothUti(uuidName);
            }
            else if(uuidName.StartsWith(GattDescriptorUuids.Namespace))
            {
                return GattDescriptorUuids.FromBluetoothUti(uuidName);
            }

            // no namespace provided - try all
            var uuid = GattServiceUuids.FromBluetoothUti(uuidName);
            if(uuid == Empty)
            {
                uuid = GattCharacteristicUuids.FromBluetoothUti(uuidName);

                if(uuid == Empty)
                {
                    uuid = GattDescriptorUuids.FromBluetoothUti(uuidName);
                }
            }

            return uuid;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            if (Value == ((BluetoothUuid)obj).Value)
            {
                return true;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return _uuid.GetHashCode();
        }
    }
}
