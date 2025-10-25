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
    public record BasicConstraints : TLVPayload
    {
        /// <inheritdoc />
        public BasicConstraints() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public BasicConstraints(Memory<byte> data) : this(new TLVReader(data)) {}

        public required bool IsCa { get; set; }
        public byte? PathLenConstraint { get; set; }

        [SetsRequiredMembers]
        internal BasicConstraints(TLVReader reader, long structNumber = -1) {
            reader.StartStructure(structNumber);
            IsCa = reader.GetBool(1)!.Value;
            if (reader.IsTag(2))
                PathLenConstraint = reader.GetByte(2);
            reader.EndContainer();
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            writer.StartStructure(structNumber);
            writer.WriteBool(1, IsCa);
            if (PathLenConstraint != null)
                writer.WriteByte(2, PathLenConstraint);
            writer.EndContainer();
        }
    }
}
