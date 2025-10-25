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

namespace MatterDotNet.Messages.Certificates
{
    /// <summary>
    /// Extension Payload
    /// </summary>
    public record Extension : TLVPayload
    {
        /// <inheritdoc />
        public Extension() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public Extension(Memory<byte> data) : this(new TLVReader(data)) {}

        public BasicConstraints BasicCnstr { get; set; }
        public ushort KeyUsage { get; set; }
        public uint[] ExtendedKeyUsage { get; set; }
        public byte[] SubjectKeyId { get; set; }
        public byte[] AuthorityKeyId { get; set; }
        public byte[] FutureExtension { get; set; }

        [SetsRequiredMembers]
        internal Extension(TLVReader reader, long structNumber = -1) {
            if (reader.IsTag(1))
                BasicCnstr = new BasicConstraints(reader, 1);
            else if (reader.IsTag(2))
                KeyUsage = reader.GetUShort(2);
            else if (reader.IsTag(3))
            {
                reader.StartArray(3);
                List<uint> items = new();
                while (!reader.IsEndContainer()) {
                    items.Add(reader.GetUInt(-1)!);
                }
                reader.EndContainer();
                ExtendedKeyUsage = items.ToArray();
            }
            else if (reader.IsTag(4))
                SubjectKeyId = reader.GetBytes(4, false, 20, 20);
            else if (reader.IsTag(5))
                AuthorityKeyId = reader.GetBytes(5, false, 20, 20);
            else if (reader.IsTag(6))
                FutureExtension = reader.GetBytes(6);
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            if (BasicCnstr != null)
                BasicCnstr.Serialize(writer, 1);
            else if (KeyUsage != null)
                writer.WriteUShort(2, KeyUsage);
            else if (ExtendedKeyUsage != null)
            {
                Constrain(ExtendedKeyUsage, 1);
                writer.StartArray(3);
                foreach (var item in ExtendedKeyUsage) {
                    writer.WriteUInt(-1, item);
                }
                writer.EndContainer();
            }
            else if (SubjectKeyId != null)
                writer.WriteBytes(4, SubjectKeyId, 20, 20);
            else if (AuthorityKeyId != null)
                writer.WriteBytes(5, AuthorityKeyId, 20, 20);
            else if (FutureExtension != null)
                writer.WriteBytes(6, FutureExtension);
        }
    }
}
