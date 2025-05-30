// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan
{
    using System;
    using System.Globalization;

    /// <summary>
    /// A MAC header (MHDR) that specifies the message type (MType) and according to which major
    /// version (Major) of the frame format of the LoRaWAN layer specification the frame has been
    /// encoded.
    /// </summary>
    public readonly record struct MacHeader
    {
        public const int Size = sizeof(byte);

        private readonly byte value;

        public MacHeader(byte value) => this.value = value;

        public MacHeader(MacMessageType messageType, DataMessageVersion major = DataMessageVersion.R1)
#pragma warning disable format
            : this((unchecked((uint)messageType), unchecked((uint)major)) switch
                   {
                       (> 7, _) => throw new ArgumentException(null, nameof(messageType)),
                       (_, > 3) => throw new ArgumentException(null, nameof(major)),
                       _ => unchecked((byte)((((byte)messageType) << 5) | (byte)major))
                   }) { }
#pragma warning restore format

        /// <summary>
        /// Gets the message type (MType).
        /// </summary>
        public MacMessageType MessageType => (MacMessageType)(this.value >> 5);

        /// <summary>
        /// Gets the major version (Major) of the frame format of the LoRaWAN layer specification.
        /// </summary>
        public DataMessageVersion Major => (DataMessageVersion)(this.value & 0b11);

        public override string ToString() => this.value.ToString("X2", CultureInfo.InvariantCulture);

        public Span<byte> Write(Span<byte> buffer)
        {
            buffer[0] = this.value;
            return buffer[Size..];
        }

        public static explicit operator byte(MacHeader header) => header.value;
    }
}
