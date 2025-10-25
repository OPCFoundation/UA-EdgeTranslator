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
    public record ReportDataMessage : TLVPayload
    {
        /// <inheritdoc />
        public ReportDataMessage() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public ReportDataMessage(Memory<byte> data) : this(new TLVReader(data)) {}

        public ulong? SubscriptionID { get; set; }
        public AttributeReportIB[] AttributeReports { get; set; }
        public EventReportIB[] EventReports { get; set; }
        public bool? MoreChunkedMessages { get; set; }
        public bool? SuppressResponse { get; set; }
        public required byte InteractionModelRevision { get; set; }

        [SetsRequiredMembers]
        internal ReportDataMessage(TLVReader reader, long structNumber = -1) {
            reader.StartStructure(structNumber);
            if (reader.IsTag(0))
                SubscriptionID = reader.GetULong(0);
            if (reader.IsTag(1))
            {
                reader.StartArray(1);
                List<AttributeReportIB> items = new();
                while (!reader.IsEndContainer()) {
                    items.Add(new AttributeReportIB(reader, -1));
                }
                reader.EndContainer();
                AttributeReports = items.ToArray();
            }
            if (reader.IsTag(2))
            {
                reader.StartArray(2);
                List<EventReportIB> items = new();
                while (!reader.IsEndContainer()) {
                    items.Add(new EventReportIB(reader, -1));
                }
                reader.EndContainer();
                EventReports = items.ToArray();
            }
            if (reader.IsTag(3))
                MoreChunkedMessages = reader.GetBool(3);
            if (reader.IsTag(4))
                SuppressResponse = reader.GetBool(4);
            InteractionModelRevision = reader.GetByte(255)!;
            reader.EndContainer();
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            writer.StartStructure(structNumber);
            if (SubscriptionID != null)
                writer.WriteULong(0, SubscriptionID);
            if (AttributeReports != null)
            {
                writer.StartArray(1);
                foreach (var item in AttributeReports) {
                    item.Serialize(writer, -1);
                }
                writer.EndContainer();
            }
            if (EventReports != null)
            {
                writer.StartArray(2);
                foreach (var item in EventReports) {
                    item.Serialize(writer, -1);
                }
                writer.EndContainer();
            }
            if (MoreChunkedMessages != null)
                writer.WriteBool(3, MoreChunkedMessages);
            if (SuppressResponse != null)
                writer.WriteBool(4, SuppressResponse);
            writer.WriteByte(255, InteractionModelRevision);
            writer.EndContainer();
        }
    }
}
