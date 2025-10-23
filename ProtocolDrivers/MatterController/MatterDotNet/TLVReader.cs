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

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MatterDotNet.Protocol.Parsers
{
    public class TLVReader
    {
        Memory<byte> data;
        TLVControl control;
        ElementType type;
        int offset;
        ushort vendorID;
        ushort profileNumber;
        uint tagNumber;
        int length = 0;

        public TLVReader(Memory<byte> data)
        {
            this.data = data;
            if (!ReadTag())
                throw new EndOfStreamException("Payload is empty");
        }

        public bool IsTag(long tagNumber)
        {
            if (tagNumber < 0)
                return this.control == TLVControl.Anonymous;
            return this.tagNumber == tagNumber;
        }

        public bool IsTag(long tagNumber, ushort vendorID, ushort profileNumber)
        {
            return this.tagNumber == tagNumber && this.vendorID == vendorID && this.profileNumber == profileNumber;
        }

        public bool IsEndContainer()
        {
            return this.type == ElementType.EndOfContainer;
        }

        public void StartStructure(long structureNumber = -1)
        {
            if (type != ElementType.Structure)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type structure but received {type}");
            if (!IsTag(structureNumber))
                throw new InvalidDataException("Tag " + structureNumber + " not present");
            ReadTag();
        }

        public void StartArray(long arrayNumber = -1)
        {
            if (type != ElementType.Array)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type array but received {type}");
            if (!IsTag(arrayNumber))
                throw new InvalidDataException("Tag " + arrayNumber + " not present");
            ReadTag();
        }

        public void StartList(long listNumber = -1)
        {
            if (type != ElementType.List)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type list but received {type}");
            if (!IsTag(listNumber))
                throw new InvalidDataException("Tag " + listNumber + " not present");
            ReadTag();
        }

        public void EndContainer()
        {
            while (type != ElementType.EndOfContainer && type != ElementType.None)
                GetAny(tagNumber, true);
            if (type != ElementType.EndOfContainer)
                throw new InvalidDataException("End structure was not found");
            ReadTag();
        }

        public byte? GetByte(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (byte?)GetNull(tagNumber);
            if (type != ElementType.Byte)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type byte but received {type}");
            byte val = data.Span[offset++];
            ReadTag();
            return val;
        }
        public sbyte? GetSByte(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (sbyte?)GetNull(tagNumber);
            if (type != ElementType.SByte)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type sbyte but received {type}");
            sbyte val = (sbyte)data.Span[offset++];
            ReadTag();
            return val;
        }
        public bool? GetBool(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (bool?)GetNull(tagNumber);
            if (type != ElementType.True && type != ElementType.False)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type bool but received {type}");
            bool val = (type == ElementType.True);
            ReadTag();
            return val;
        }
        public short? GetShort(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (short?)GetNull(tagNumber);
            if (type == ElementType.SByte)
                return GetSByte(tagNumber, nullable);
            if (type != ElementType.Short)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type short but received {type}");
            short val = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2).Span);
            offset += 2;
            ReadTag();
            return val;
        }

        public ushort? GetUShort(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (ushort?)GetNull(tagNumber);
            if (type == ElementType.Byte)
                return GetByte(tagNumber, nullable);
            if (type != ElementType.UShort)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type ushort but received {type}");
            ushort val = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2).Span);
            offset += 2;
            ReadTag();
            return val;
        }

        public decimal? GetUDecimal(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (decimal?)GetNull(tagNumber);
            ushort val = GetUShort(tagNumber, false)!.Value;
            decimal ret = Math.Abs(val / 100);
            ret += (val % 100) / 100M;
            return ret;
        }

        public decimal? GetDecimal(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (decimal?)GetNull(tagNumber);
            short val = GetShort(tagNumber, false)!.Value;
            decimal ret = (int)(val / 100);
            ret += (val % 100) / 100M;
            return ret;
        }

        public int? GetInt(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (int?)GetNull(tagNumber);
            if (type == ElementType.SByte)
                return GetSByte(tagNumber, nullable);
            if (type == ElementType.Short)
                return GetShort(tagNumber, nullable);
            if (type != ElementType.Int)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type int but received {type}");
            int val = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4).Span);
            offset += 4;
            ReadTag();
            return val;
        }

        public uint? GetUInt(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (uint?)GetNull(tagNumber);
            if (type == ElementType.Byte)
                return GetByte(tagNumber, nullable);
            if (type == ElementType.UShort)
                return GetUShort(tagNumber, nullable);
            if (type != ElementType.UInt)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type uint but received {type}");
            uint val = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4).Span);
            offset += 4;
            ReadTag();
            return val;
        }

        public long? GetLong(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (long?)GetNull(tagNumber);
            if (type == ElementType.SByte)
                return GetSByte(tagNumber, nullable);
            if (type == ElementType.Short)
                return GetShort(tagNumber, nullable);
            if (type == ElementType.Int)
                return GetInt(tagNumber, nullable);
            if (type != ElementType.Long)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type long but received {type}");
            long val = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8).Span);
            offset += 8;
            ReadTag();
            return val;
        }

        public ulong? GetULong(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (ulong?)GetNull(tagNumber);
            if (type == ElementType.Byte)
                return GetByte(tagNumber, nullable);
            if (type == ElementType.UShort)
                return GetUShort(tagNumber, nullable);
            if (type == ElementType.UInt)
                return GetUInt(tagNumber, nullable);
            if (type != ElementType.ULong)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type ulong but received {type}");
            ulong val = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8).Span);
            offset += 8;
            ReadTag();
            return val;
        }

        public float? GetFloat(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (float?)GetNull(tagNumber);
            if (type != ElementType.Float)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type float but received {type}");
            float val = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset, 4).Span);
            offset += 4;
            ReadTag();
            return val;
        }

        public double? GetDouble(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (double?)GetNull(tagNumber);
            if (type != ElementType.Double)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type double but received {type}");
            double val = BinaryPrimitives.ReadDoubleLittleEndian(data.Slice(offset, 4).Span);
            offset += 4;
            ReadTag();
            return val;
        }

        public string GetString(long tagNumber, bool nullable = false, int max = int.MaxValue, int min = 0)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null && nullable)
                return (string)GetNull(tagNumber);
            if (type != ElementType.String8 && type != ElementType.String16 && type != ElementType.String32 && type != ElementType.String64)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type string but received {type}");
            if (length > max)
                throw new InvalidDataException($"Constraint Violation! Max length is {max} but received {length}");
            if (length < min)
                throw new InvalidDataException($"Constraint Violation! Min length is {min} but received {length}");
            string val = Encoding.UTF8.GetString(data.Slice(offset, length).Span);
            offset += length;
            ReadTag();
            return val;
        }

        public byte[] GetBytes(long tagNumber, bool nullable = false, int max = int.MaxValue, int min = 0)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present. Current tag is " + this.tagNumber);
            if (type == ElementType.Null && nullable)
                return (byte[])GetNull(tagNumber);
            if (type != ElementType.Bytes8 && type != ElementType.Bytes16 && type != ElementType.Bytes32 && type != ElementType.Bytes64)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type bytes but received {type}");
            if (length > max)
                throw new InvalidDataException($"Constraint Violation! Max length is {max} but received {length}");
            if (length < min)
                throw new InvalidDataException($"Constraint Violation! Min length is {min} but received {length}");
            byte[] val = data.Slice(offset, length).ToArray();
            offset += length;
            ReadTag();
            return val;
        }

        public object GetNull(long tagNumber)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            ReadTag();
            return null;
        }

        public object GetAny(long tagNumber, bool nullable = false)
        {
            if (!IsTag(tagNumber))
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (type == ElementType.Null)
            {
                if (nullable)
                    return GetNull(tagNumber);
                else
                    throw new InvalidDataException($"Tag {tagNumber}: Expected not null but received {type}");
            }
            switch (type)
            {
                case ElementType.SByte:
                    return GetSByte(tagNumber, nullable);
                case ElementType.Short:
                    return GetShort(tagNumber, nullable);
                case ElementType.Int:
                    return GetInt(tagNumber, nullable);
                case ElementType.Long:
                    return GetLong(tagNumber, nullable);
                case ElementType.Byte:
                    return GetByte(tagNumber, nullable);
                case ElementType.UShort:
                    return GetUShort(tagNumber, nullable);
                case ElementType.UInt:
                    return GetUInt(tagNumber, nullable);
                case ElementType.ULong:
                    return GetULong(tagNumber, nullable);
                case ElementType.Float:
                    return GetFloat(tagNumber, nullable);
                case ElementType.Double:
                    return GetDouble(tagNumber, nullable);
                case ElementType.Bytes8:
                case ElementType.Bytes16:
                case ElementType.Bytes32:
                case ElementType.Bytes64:
                    return GetBytes(tagNumber, nullable);
                case ElementType.String8:
                case ElementType.String16:
                case ElementType.String32:
                case ElementType.String64:
                    return GetString(tagNumber, nullable);
                case ElementType.Array:
                    List<object> array = new List<object>();
                    StartArray(tagNumber);
                    while (!IsEndContainer())
                        array.Add(GetAny(-1)!);
                    EndContainer();
                    return array.ToArray();
                case ElementType.List:
                    List<object> list = new List<object>();
                    StartArray(tagNumber);
                    while (!IsEndContainer())
                        list.Add(GetAny(control == TLVControl.Anonymous ? -1 : this.tagNumber)!);
                    EndContainer();
                    return list;
                case ElementType.Structure:
                    List<object> structure = new List<object>();
                    StartStructure(tagNumber);
                    while (!IsEndContainer())
                    {
                        if (control == TLVControl.Anonymous)
                            structure.Add(GetAny(-1, true)!);
                        else
                        {
                            while (this.tagNumber > structure.Count)
                                structure.Add(null!);
                            structure.Add(GetAny(this.tagNumber, true)!);
                        }
                    }
                    EndContainer();
                    return structure.ToArray();
                default:
                    return GetBytes(tagNumber, nullable);
            }
        }

        private bool ReadTag()
        {
            if (offset == data.Length)
            {
                tagNumber = 0;
                type = ElementType.None;
                control = TLVControl.Anonymous;
                return false;
            }
            control = (TLVControl)(data.Span[offset] >> 5);
            type = (ElementType)(0x1F & data.Span[offset++]);
            switch (control)
            {
                case TLVControl.ContextSpecific:
                    tagNumber = data.Span[offset++];
                    break;
                case TLVControl.CommonProfileShort:
                case TLVControl.ImplicitProfileShort:
                    tagNumber = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2).Span);
                    offset += 2;
                    break;
                case TLVControl.CommonProfileInt:
                case TLVControl.ImplicitProfileInt:
                    tagNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4).Span);
                    offset += 4;
                    break;
                case TLVControl.FullyQualifiedInt:
                case TLVControl.FullyQualifiedShort:
                    vendorID = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2).Span);
                    offset += 2;
                    profileNumber = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2).Span);
                    offset += 2;
                    if (control == TLVControl.FullyQualifiedShort)
                    {
                        tagNumber = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2).Span);
                        offset += 2;
                    }
                    else
                    {
                        tagNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4).Span);
                        offset += 4;
                    }
                    break;
                case TLVControl.Anonymous:
                    tagNumber = 0;
                    break;
            }
            switch (type)
            {
                case ElementType.Byte:
                case ElementType.SByte:
                    length = 1;
                    break;
                case ElementType.Short:
                case ElementType.UShort:
                    length = 2;
                    break;
                case ElementType.Int:
                case ElementType.UInt:
                case ElementType.Float:
                    length = 4;
                    break;
                case ElementType.Long:
                case ElementType.ULong:
                case ElementType.Double:
                    length = 8;
                    break;
                case ElementType.String8:
                case ElementType.String16:
                case ElementType.String32:
                case ElementType.String64:
                case ElementType.Bytes8:
                case ElementType.Bytes16:
                case ElementType.Bytes32:
                case ElementType.Bytes64:
                    byte offsetSize = (byte)(1 << ((byte)type & 0x3));
                    if (offsetSize == 1)
                        length = data.Span[offset++];
                    else if (offsetSize == 2)
                    {
                        length = BinaryPrimitives.ReadUInt16LittleEndian(data.Span.Slice(offset, 2));
                        offset += 2;
                    }
                    else if (offsetSize == 4)
                    {
                        length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Span.Slice(offset, 4));
                        offset += 4;
                    }
                    else
                        throw new InvalidDataException("Long strings are not supported");
                    break;
            }
            return true;
        }
    }
}
