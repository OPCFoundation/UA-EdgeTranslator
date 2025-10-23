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
    public record WriteRequestMessage : TLVPayload
    {
        /// <inheritdoc />
        public WriteRequestMessage() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public WriteRequestMessage(Memory<byte> data) : this(new TLVReader(data)) {}

        public bool? SuppressResponse { get; set; }
        public required bool TimedRequest { get; set; }
        public required AttributeDataIB[] WriteRequests { get; set; }
        public bool? MoreChunkedMessages { get; set; }
        public required byte InteractionModelRevision { get; set; }

        [SetsRequiredMembers]
        internal WriteRequestMessage(TLVReader reader, long structNumber = -1) {
            reader.StartStructure(structNumber);
            if (reader.IsTag(0))
                SuppressResponse = reader.GetBool(0);
            TimedRequest = reader.GetBool(1)!.Value;
            {
                reader.StartArray(2);
                List<AttributeDataIB> items = new();
                while (!reader.IsEndContainer()) {
                    items.Add(new AttributeDataIB(reader, -1));
                }
                reader.EndContainer();
                WriteRequests = items.ToArray();
            }
            if (reader.IsTag(3))
                MoreChunkedMessages = reader.GetBool(3);
            InteractionModelRevision = reader.GetByte(255)!.Value;
            reader.EndContainer();
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            writer.StartStructure(structNumber);
            if (SuppressResponse != null)
                writer.WriteBool(0, SuppressResponse);
            writer.WriteBool(1, TimedRequest);
            {
                writer.StartArray(2);
                foreach (var item in WriteRequests) {
                    item.Serialize(writer, -1);
                }
                writer.EndContainer();
            }
            if (MoreChunkedMessages != null)
                writer.WriteBool(3, MoreChunkedMessages);
            writer.WriteByte(255, InteractionModelRevision);
            writer.EndContainer();
        }
    }
}
