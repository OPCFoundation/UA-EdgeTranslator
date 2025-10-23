// MatterDotNet Copyright (C) 2025 
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or any later version.
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Affero General Public License for more details.
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
// WARNING: This file was auto-generated. Do not edit.

using MatterDotNet.Protocol.Parsers;
using MatterDotNet.Protocol.Payloads;
using System;
using System.Diagnostics.CodeAnalysis;

namespace MatterDotNet.Messages.Certificates
{
    public record NocsrElements : TLVPayload
    {
        /// <inheritdoc />
        public NocsrElements() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public NocsrElements(Memory<byte> data) : this(new TLVReader(data)) {}

        public required byte[] Csr { get; set; }
        public required byte[] CSRNonce { get; set; }
        public byte[] Vendor_reserved1 { get; set; }
        public byte[] Vendor_reserved2 { get; set; }
        public byte[] Vendor_reserved3 { get; set; }

        [SetsRequiredMembers]
        internal NocsrElements(TLVReader reader, long structNumber = -1) {
            reader.StartStructure(structNumber);
            Csr = reader.GetBytes(1)!;
            CSRNonce = reader.GetBytes(2, false, 32, 32)!;
            if (reader.IsTag(3))
                Vendor_reserved1 = reader.GetBytes(3);
            if (reader.IsTag(4))
                Vendor_reserved2 = reader.GetBytes(4);
            if (reader.IsTag(5))
                Vendor_reserved3 = reader.GetBytes(5);
            reader.EndContainer();
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            writer.StartStructure(structNumber);
            writer.WriteBytes(1, Csr);
            writer.WriteBytes(2, CSRNonce, 32, 32);
            if (Vendor_reserved1 != null)
                writer.WriteBytes(3, Vendor_reserved1);
            if (Vendor_reserved2 != null)
                writer.WriteBytes(4, Vendor_reserved2);
            if (Vendor_reserved3 != null)
                writer.WriteBytes(5, Vendor_reserved3);
            writer.EndContainer();
        }
    }
}
