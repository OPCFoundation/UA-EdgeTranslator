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

namespace MatterDotNet.Messages.InteractionModel
{
    public record AttributeStatusIB : TLVPayload
    {
        /// <inheritdoc />
        public AttributeStatusIB() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public AttributeStatusIB(Memory<byte> data) : this(new TLVReader(data)) {}

        public required AttributePathIB Path { get; set; }
        public required StatusIB Status { get; set; }

        [SetsRequiredMembers]
        internal AttributeStatusIB(TLVReader reader, long structNumber = -1) {
            reader.StartStructure(structNumber);
            Path = new AttributePathIB(reader, 0);
            Status = new StatusIB(reader, 1);
            reader.EndContainer();
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            writer.StartStructure(structNumber);
            Path.Serialize(writer, 0);
            Status.Serialize(writer, 1);
            writer.EndContainer();
        }
    }
}
