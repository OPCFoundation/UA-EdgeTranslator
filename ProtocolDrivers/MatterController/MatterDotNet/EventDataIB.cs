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
    public record EventDataIB : TLVPayload
    {
        /// <inheritdoc />
        public EventDataIB() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public EventDataIB(Memory<byte> data) : this(new TLVReader(data)) {}

        public required EventPathIB Path { get; set; }
        public required ulong EventNumber { get; set; }
        public required byte Priority { get; set; }
        public ulong? EpochTimestamp { get; set; }
        public ulong? SystemTimestamp { get; set; }
        public ulong? DeltaEpochTimestamp { get; set; }
        public ulong? DeltaSystemTimestamp { get; set; }
        public required object Data { get; set; }

        [SetsRequiredMembers]
        internal EventDataIB(TLVReader reader, long structNumber = -1) {
            reader.StartStructure(structNumber);
            Path = new EventPathIB(reader, 0);
            EventNumber = reader.GetULong(1)!.Value;
            Priority = reader.GetByte(2)!.Value;
            if (reader.IsTag(3))
                EpochTimestamp = reader.GetULong(3);
            if (reader.IsTag(4))
                SystemTimestamp = reader.GetULong(4);
            if (reader.IsTag(5))
                DeltaEpochTimestamp = reader.GetULong(5);
            if (reader.IsTag(6))
                DeltaSystemTimestamp = reader.GetULong(6);
            Data = reader.GetAny(7)!;
            reader.EndContainer();
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            writer.StartStructure(structNumber);
            Path.Serialize(writer, 0);
            writer.WriteULong(1, EventNumber);
            writer.WriteByte(2, Priority);
            if (EpochTimestamp != null)
                writer.WriteULong(3, EpochTimestamp);
            if (SystemTimestamp != null)
                writer.WriteULong(4, SystemTimestamp);
            if (DeltaEpochTimestamp != null)
                writer.WriteULong(5, DeltaEpochTimestamp);
            if (DeltaSystemTimestamp != null)
                writer.WriteULong(6, DeltaSystemTimestamp);
            writer.WriteAny(7, Data);
            writer.EndContainer();
        }
    }
}
