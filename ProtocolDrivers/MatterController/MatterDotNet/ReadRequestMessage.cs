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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MatterDotNet.Messages.InteractionModel
{
    public record ReadRequestMessage : TLVPayload
    {
        /// <inheritdoc />
        public ReadRequestMessage() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public ReadRequestMessage(Memory<byte> data) : this(new TLVReader(data)) {}

        public AttributePathIB[] AttributeRequests { get; set; }
        public EventPathIB[] EventRequests { get; set; }
        public EventFilterIB[] EventFilters { get; set; }
        public required bool FabricFiltered { get; set; }
        public DataVersionFilterIB[] DataVersionFilters { get; set; }
        public required byte InteractionModelRevision { get; set; }

        [SetsRequiredMembers]
        internal ReadRequestMessage(TLVReader reader, long structNumber = -1) {
            reader.StartStructure(structNumber);
            if (reader.IsTag(0))
            {
                reader.StartArray(0);
                List<AttributePathIB> items = new();
                while (!reader.IsEndContainer()) {
                    items.Add(new AttributePathIB(reader, -1));
                }
                reader.EndContainer();
                AttributeRequests = items.ToArray();
            }
            if (reader.IsTag(1))
            {
                reader.StartArray(1);
                List<EventPathIB> items = new();
                while (!reader.IsEndContainer()) {
                    items.Add(new EventPathIB(reader, -1));
                }
                reader.EndContainer();
                EventRequests = items.ToArray();
            }
            if (reader.IsTag(2))
            {
                reader.StartArray(2);
                List<EventFilterIB> items = new();
                while (!reader.IsEndContainer()) {
                    items.Add(new EventFilterIB(reader, -1));
                }
                reader.EndContainer();
                EventFilters = items.ToArray();
            }
            FabricFiltered = reader.GetBool(3)!.Value;
            if (reader.IsTag(4))
            {
                reader.StartArray(4);
                List<DataVersionFilterIB> items = new();
                while (!reader.IsEndContainer()) {
                    items.Add(new DataVersionFilterIB(reader, -1));
                }
                reader.EndContainer();
                DataVersionFilters = items.ToArray();
            }
            InteractionModelRevision = reader.GetByte(255)!.Value;
            reader.EndContainer();
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            writer.StartStructure(structNumber);
            if (AttributeRequests != null)
            {
                writer.StartArray(0);
                foreach (var item in AttributeRequests) {
                    item.Serialize(writer, -1);
                }
                writer.EndContainer();
            }
            if (EventRequests != null)
            {
                writer.StartArray(1);
                foreach (var item in EventRequests) {
                    item.Serialize(writer, -1);
                }
                writer.EndContainer();
            }
            if (EventFilters != null)
            {
                writer.StartArray(2);
                foreach (var item in EventFilters) {
                    item.Serialize(writer, -1);
                }
                writer.EndContainer();
            }
            writer.WriteBool(3, FabricFiltered);
            if (DataVersionFilters != null)
            {
                writer.StartArray(4);
                foreach (var item in DataVersionFilters) {
                    item.Serialize(writer, -1);
                }
                writer.EndContainer();
            }
            writer.WriteByte(255, InteractionModelRevision);
            writer.EndContainer();
        }
    }
}
