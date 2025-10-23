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
    public record AttributePathIB : TLVPayload
    {
        /// <inheritdoc />
        public AttributePathIB() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public AttributePathIB(Memory<byte> data) : this(new TLVReader(data)) {}

        public bool? EnableTagCompression { get; set; }
        public ulong? Node { get; set; }
        public ushort? Endpoint { get; set; }
        public uint? Cluster { get; set; }
        public uint? Attribute { get; set; }
        public ushort? ListIndex { get; set; }
        public bool HasListIndex { get; set; }
        public uint? WildcardPathFlags { get; set; }

        [SetsRequiredMembers]
        internal AttributePathIB(TLVReader reader, long structNumber = -1) {
            reader.StartList(structNumber);
            if (reader.IsTag(0))
                EnableTagCompression = reader.GetBool(0);
            if (reader.IsTag(1))
                Node = reader.GetULong(1);
            if (reader.IsTag(2))
                Endpoint = reader.GetUShort(2);
            if (reader.IsTag(3))
                Cluster = reader.GetUInt(3);
            if (reader.IsTag(4))
                Attribute = reader.GetUInt(4);
            if (reader.IsTag(5))
            {
                HasListIndex = true;
                ListIndex = reader.GetUShort(5, true);
            }
            if (reader.IsTag(6))
                WildcardPathFlags = reader.GetUInt(6);
            reader.EndContainer();
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            writer.StartList(structNumber);
            if (EnableTagCompression != null)
                writer.WriteBool(0, EnableTagCompression);
            if (Node != null)
                writer.WriteULong(1, Node);
            if (Endpoint != null)
                writer.WriteUShort(2, Endpoint);
            if (Cluster != null)
                writer.WriteUInt(3, Cluster);
            if (Attribute != null)
                writer.WriteUInt(4, Attribute);
            if (HasListIndex)
                writer.WriteUShort(5, ListIndex);
            if (WildcardPathFlags != null)
                writer.WriteUInt(6, WildcardPathFlags);
            writer.EndContainer();
        }
    }
}
