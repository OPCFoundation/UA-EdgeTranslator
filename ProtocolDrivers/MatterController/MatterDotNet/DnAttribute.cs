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
    public record DnAttribute : TLVPayload
    {
        /// <inheritdoc />
        public DnAttribute() {}

        /// <inheritdoc />
        [SetsRequiredMembers]
        public DnAttribute(Memory<byte> data) : this(new TLVReader(data)) {}

        public string CommonName { get; set; }
        public string Surname { get; set; }
        public string SerialNum { get; set; }
        public string CountryName { get; set; }
        public string LocalityName { get; set; }
        public string StateOrProvinceName { get; set; }
        public string OrgName { get; set; }
        public string OrgUnitName { get; set; }
        public string Title { get; set; }
        public string Name { get; set; }
        public string GivenName { get; set; }
        public string Initials { get; set; }
        public string GenQualifier { get; set; }
        public string DnQualifier { get; set; }
        public string Pseudonym { get; set; }
        public string DomainComponent { get; set; }
        public ulong MatterNodeId { get; set; }
        public ulong MatterFirmwareSigningId { get; set; }
        public ulong MatterIcacId { get; set; }
        public ulong MatterRcacId { get; set; }
        public ulong MatterFabricId { get; set; }
        public ulong? MatterNocCat { get; set; }
        public string CommonNamePs { get; set; }
        public string SurnamePs { get; set; }
        public string SerialNumPs { get; set; }
        public string CountryNamePs { get; set; }
        public string LocalityNamePs { get; set; }
        public string StateOrProvinceNamePs { get; set; }
        public string OrgNamePs { get; set; }
        public string OrgUnitNamePs { get; set; }
        public string TitlePs { get; set; }
        public string NamePs { get; set; }
        public string GivenNamePs { get; set; }
        public string InitialsPs { get; set; }
        public string GenQualifierPs { get; set; }
        public string DnQualifierPs { get; set; }
        public string PseudonymPs { get; set; }

        [SetsRequiredMembers]
        internal DnAttribute(TLVReader reader, long structNumber = -1) {
            if (reader.IsTag(1))
                CommonName = reader.GetString(1);
            else if (reader.IsTag(2))
                Surname = reader.GetString(2);
            else if (reader.IsTag(3))
                SerialNum = reader.GetString(3);
            else if (reader.IsTag(4))
                CountryName = reader.GetString(4);
            else if (reader.IsTag(5))
                LocalityName = reader.GetString(5);
            else if (reader.IsTag(6))
                StateOrProvinceName = reader.GetString(6);
            else if (reader.IsTag(7))
                OrgName = reader.GetString(7);
            else if (reader.IsTag(8))
                OrgUnitName = reader.GetString(8);
            else if (reader.IsTag(9))
                Title = reader.GetString(9);
            else if (reader.IsTag(10))
                Name = reader.GetString(10);
            else if (reader.IsTag(11))
                GivenName = reader.GetString(11);
            else if (reader.IsTag(12))
                Initials = reader.GetString(12);
            else if (reader.IsTag(13))
                GenQualifier = reader.GetString(13);
            else if (reader.IsTag(14))
                DnQualifier = reader.GetString(14);
            else if (reader.IsTag(15))
                Pseudonym = reader.GetString(15);
            else if (reader.IsTag(16))
                DomainComponent = reader.GetString(16);
            else if (reader.IsTag(17))
                MatterNodeId = reader.GetULong(17);
            else if (reader.IsTag(18))
                MatterFirmwareSigningId = reader.GetULong(18);
            else if (reader.IsTag(19))
                MatterIcacId = reader.GetULong(19);
            else if (reader.IsTag(20))
                MatterRcacId = reader.GetULong(20);
            else if (reader.IsTag(21))
                MatterFabricId = reader.GetULong(21);
            else if (reader.IsTag(22))
                MatterNocCat = reader.GetULong(22);
            else if (reader.IsTag(129))
                CommonNamePs = reader.GetString(129);
            else if (reader.IsTag(130))
                SurnamePs = reader.GetString(130);
            else if (reader.IsTag(131))
                SerialNumPs = reader.GetString(131);
            else if (reader.IsTag(132))
                CountryNamePs = reader.GetString(132);
            else if (reader.IsTag(133))
                LocalityNamePs = reader.GetString(133);
            else if (reader.IsTag(134))
                StateOrProvinceNamePs = reader.GetString(134);
            else if (reader.IsTag(135))
                OrgNamePs = reader.GetString(135);
            else if (reader.IsTag(136))
                OrgUnitNamePs = reader.GetString(136);
            else if (reader.IsTag(137))
                TitlePs = reader.GetString(137);
            else if (reader.IsTag(138))
                NamePs = reader.GetString(138);
            else if (reader.IsTag(139))
                GivenNamePs = reader.GetString(139);
            else if (reader.IsTag(140))
                InitialsPs = reader.GetString(140);
            else if (reader.IsTag(141))
                GenQualifierPs = reader.GetString(141);
            else if (reader.IsTag(142))
                DnQualifierPs = reader.GetString(142);
            else if (reader.IsTag(143))
                PseudonymPs = reader.GetString(143);
        }

        internal override void Serialize(TLVWriter writer, long structNumber = -1) {
            if (CommonName != null)
                writer.WriteString(1, CommonName);
            else if (Surname != null)
                writer.WriteString(2, Surname);
            else if (SerialNum != null)
                writer.WriteString(3, SerialNum);
            else if (CountryName != null)
                writer.WriteString(4, CountryName);
            else if (LocalityName != null)
                writer.WriteString(5, LocalityName);
            else if (StateOrProvinceName != null)
                writer.WriteString(6, StateOrProvinceName);
            else if (OrgName != null)
                writer.WriteString(7, OrgName);
            else if (OrgUnitName != null)
                writer.WriteString(8, OrgUnitName);
            else if (Title != null)
                writer.WriteString(9, Title);
            else if (Name != null)
                writer.WriteString(10, Name);
            else if (GivenName != null)
                writer.WriteString(11, GivenName);
            else if (Initials != null)
                writer.WriteString(12, Initials);
            else if (GenQualifier != null)
                writer.WriteString(13, GenQualifier);
            else if (DnQualifier != null)
                writer.WriteString(14, DnQualifier);
            else if (Pseudonym != null)
                writer.WriteString(15, Pseudonym);
            else if (DomainComponent != null)
                writer.WriteString(16, DomainComponent);
            else if (MatterNodeId != 0)
                writer.WriteULong(17, MatterNodeId);
            else if (MatterFirmwareSigningId != 0)
                writer.WriteULong(18, MatterFirmwareSigningId);
            else if (MatterIcacId != 0)
                writer.WriteULong(19, MatterIcacId);
            else if (MatterRcacId != 0)
                writer.WriteULong(20, MatterRcacId);
            else if (MatterFabricId != 0)
                writer.WriteULong(21, MatterFabricId);
            else if (MatterNocCat != null)
                writer.WriteULong(22, MatterNocCat);
            else if (CommonNamePs != null)
                writer.WriteString(129, CommonNamePs);
            else if (SurnamePs != null)
                writer.WriteString(130, SurnamePs);
            else if (SerialNumPs != null)
                writer.WriteString(131, SerialNumPs);
            else if (CountryNamePs != null)
                writer.WriteString(132, CountryNamePs);
            else if (LocalityNamePs != null)
                writer.WriteString(133, LocalityNamePs);
            else if (StateOrProvinceNamePs != null)
                writer.WriteString(134, StateOrProvinceNamePs);
            else if (OrgNamePs != null)
                writer.WriteString(135, OrgNamePs);
            else if (OrgUnitNamePs != null)
                writer.WriteString(136, OrgUnitNamePs);
            else if (TitlePs != null)
                writer.WriteString(137, TitlePs);
            else if (NamePs != null)
                writer.WriteString(138, NamePs);
            else if (GivenNamePs != null)
                writer.WriteString(139, GivenNamePs);
            else if (InitialsPs != null)
                writer.WriteString(140, InitialsPs);
            else if (GenQualifierPs != null)
                writer.WriteString(141, GenQualifierPs);
            else if (DnQualifierPs != null)
                writer.WriteString(142, DnQualifierPs);
            else if (PseudonymPs != null)
                writer.WriteString(143, PseudonymPs);
        }
    }
}
