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
    public record CommandStatusIB : TLVPayload
    {
        /// <inheritdoc />
        public CommandStatusIB() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public CommandStatusIB(Memory<byte> data) : this(new TLVReader(data)) {}

        public required CommandPathIB CommandPath { get; set; }
        public required StatusIB Status { get; set; }
        public ushort? CommandRef { get; set; }

        [SetsRequiredMembers]
        internal CommandStatusIB(TLVReader reader, long structNumber = -1) {
            reader.StartStructure(structNumber);
            CommandPath = new CommandPathIB(reader, 0);
            Status = new StatusIB(reader, 1);
            if (reader.IsTag(2))
                CommandRef = reader.GetUShort(2);
            reader.EndContainer();
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            writer.StartStructure(structNumber);
            CommandPath.Serialize(writer, 0);
            Status.Serialize(writer, 1);
            if (CommandRef != null)
                writer.WriteUShort(2, CommandRef);
            writer.EndContainer();
        }
    }
}
