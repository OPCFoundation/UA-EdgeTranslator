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

using MatterDotNet.Protocol.Payloads;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("Test")]
namespace MatterDotNet.Protocol.Parsers
{
    internal class TLVWriter
    {
        PayloadWriter writer;

        public TLVWriter(PayloadWriter writer)
        {
            this.writer = writer;
        }

        private void WriteTag(TLVControl control, ElementType type)
        {
            writer.Write((byte)(((byte)control << 5) | (byte)type));
        }

        private void WriteTag(long tagNumber, ElementType type)
        {
            if (tagNumber < 0)
                WriteTag(TLVControl.Anonymous, type);
            else
            {
                writer.Write((byte)(((byte)TLVControl.ContextSpecific << 5) | (byte)type));
                writer.Write((byte)tagNumber);
            }
        }

        public void StartStructure(long tagNumber)
        {
            if (tagNumber < 0)
                WriteTag(TLVControl.Anonymous, ElementType.Structure);
            else
                WriteTag(tagNumber, ElementType.Structure);
        }

        public void StartArray(long tagNumber = -1)
        {
            if (tagNumber < 0)
                WriteTag(TLVControl.Anonymous, ElementType.Array);
            else
                WriteTag(tagNumber, ElementType.Array);
        }

        public void StartList(long tagNumber = -1)
        {
            if (tagNumber < 0)
                WriteTag(TLVControl.Anonymous, ElementType.List);
            else
                WriteTag(tagNumber, ElementType.List);
        }

        public void EndContainer()
        {
            WriteTag(TLVControl.Anonymous, ElementType.EndOfContainer);
        }

        public void WriteNull(long tagNumber)
        {
            WriteTag(tagNumber, ElementType.Null);
        }

        public void WriteByte(long tagNumber, byte? value, byte max = byte.MaxValue, byte min = byte.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value > max)
                    throw new InvalidDataException("Constraint Violated! Maximum value: " + max + ", Actual: " + value);
                if (value < min)
                    throw new InvalidDataException("Constraint Violated! Minimum value: " + min + ", Actual: " + value);
                WriteTag(tagNumber, ElementType.Byte);
                writer.Write(value.Value);
            }
        }
        public void WriteSByte(long tagNumber, sbyte? value, sbyte max = sbyte.MaxValue, sbyte min = sbyte.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value > max)
                    throw new InvalidDataException("Constraint Violated! Maximum value: " + max + ", Actual: " + value);
                if (value < min)
                    throw new InvalidDataException("Constraint Violated! Minimum value: " + min + ", Actual: " + value);
                WriteTag(tagNumber, ElementType.SByte);
                writer.Write(value.Value);
            }
        }
        public void WriteShort(long tagNumber, short? value, short max = short.MaxValue, short min = short.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value > max)
                    throw new InvalidDataException("Constraint Violated! Maximum value: " + max + ", Actual: " + value);
                if (value < min)
                    throw new InvalidDataException("Constraint Violated! Minimum value: " + min + ", Actual: " + value);
                if (value.Value < sbyte.MaxValue && value.Value > sbyte.MinValue)
                {
                    WriteSByte(tagNumber, (sbyte)value.Value);
                    return;
                }
                WriteTag(tagNumber, ElementType.Short);
                writer.Write(value.Value);
            }
        }
        public void WriteUShort(long tagNumber, ushort? value, ushort max = ushort.MaxValue, ushort min = ushort.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value > max)
                    throw new InvalidDataException("Constraint Violated! Maximum value: " + max + ", Actual: " + value);
                if (value < min)
                    throw new InvalidDataException("Constraint Violated! Minimum value: " + min + ", Actual: " + value);
                if (value.Value < byte.MaxValue)
                {
                    WriteByte(tagNumber, (byte)value.Value);
                    return;
                }
                WriteTag(tagNumber, ElementType.UShort);
                writer.Write(value.Value);
            }
        }
        public void WriteUDecimal(long tagNumber, decimal? value, ushort max = ushort.MaxValue, ushort min = ushort.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                ushort val = (ushort)Math.Truncate(value.Value);
                val *= 100;
                val += (ushort)Math.Round((value.Value - Math.Truncate(value.Value)) * 100M);
                WriteUShort(tagNumber, val);
            }
        }
        public void WriteDecimal(long tagNumber, decimal? value, ushort max = ushort.MaxValue, ushort min = ushort.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                short val = (short)Math.Truncate(value.Value);
                val *= 100;
                val += (short)Math.Round((value.Value - Math.Truncate(value.Value)) * 100M);
                WriteShort(tagNumber, val);
            }
        }
        public void WriteInt(long tagNumber, int? value, int max = int.MaxValue, int min = int.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value > max)
                    throw new InvalidDataException("Constraint Violated! Maximum value: " + max + ", Actual: " + value);
                if (value < min)
                    throw new InvalidDataException("Constraint Violated! Minimum value: " + min + ", Actual: " + value);
                if (value.Value < short.MaxValue && value.Value > short.MinValue)
                {
                    WriteShort(tagNumber, (short)value.Value);
                    return;
                }
                WriteTag(tagNumber, ElementType.Int);
                writer.Write(value.Value);
            }
        }
        public void WriteUInt(long tagNumber, uint? value, uint max = uint.MaxValue, uint min = uint.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value > max)
                    throw new InvalidDataException("Constraint Violated! Maximum value: " + max + ", Actual: " + value);
                if (value < min)
                    throw new InvalidDataException("Constraint Violated! Minimum value: " + min + ", Actual: " + value);
                if (value.Value < ushort.MaxValue)
                {
                    WriteUShort(tagNumber, (ushort)value.Value);
                    return;
                }
                WriteTag(tagNumber, ElementType.UInt);
                writer.Write(value.Value);
            }
        }
        public void WriteLong(long tagNumber, long? value, long max = long.MaxValue, long min = long.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value > max)
                    throw new InvalidDataException("Constraint Violated! Maximum value: " + max + ", Actual: " + value);
                if (value < min)
                    throw new InvalidDataException("Constraint Violated! Minimum value: " + min + ", Actual: " + value);
                if (value.Value < int.MaxValue && value.Value > int.MinValue)
                {
                    WriteInt(tagNumber, (int)value.Value);
                    return;
                }
                WriteTag(tagNumber, ElementType.Long);
                writer.Write(value.Value);
            }
        }
        public void WriteULong(long tagNumber, ulong? value, ulong max = ulong.MaxValue, ulong min = ulong.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value > max)
                    throw new InvalidDataException("Constraint Violated! Maximum value: " + max + ", Actual: " + value);
                if (value < min)
                    throw new InvalidDataException("Constraint Violated! Minimum value: " + min + ", Actual: " + value);
                if (value.Value < uint.MaxValue)
                {
                    WriteUInt(tagNumber, (uint)value.Value);
                    return;
                }
                WriteTag(tagNumber, ElementType.ULong);
                writer.Write(value.Value);
            }
        }
        public void WriteFloat(long tagNumber, float? value, float max = float.MaxValue, float min = float.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value > max)
                    throw new InvalidDataException("Constraint Violated! Maximum value: " + max + ", Actual: " + value);
                if (value < min)
                    throw new InvalidDataException("Constraint Violated! Minimum value: " + min + ", Actual: " + value);
                WriteTag(tagNumber, ElementType.Float);
                writer.Write(value.Value);
            }
        }
        public void WriteDouble(long tagNumber, double? value, double max = double.MaxValue, double min = double.MinValue)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value > max)
                    throw new InvalidDataException("Constraint Violated! Maximum value: " + max + ", Actual: " + value);
                if (value < min)
                    throw new InvalidDataException("Constraint Violated! Minimum value: " + min + ", Actual: " + value);
                WriteTag(tagNumber, ElementType.Double);
                writer.Write(value.Value);
            }
        }

        public void WriteBool(long tagNumber, bool? value)
        {
            if (!value.HasValue)
                WriteTag(tagNumber, ElementType.Null);
            else if (value.Value == true)
                WriteTag(tagNumber, ElementType.True);
            else
                WriteTag(tagNumber, ElementType.False);
        }

        public void WriteString(long tagNumber, string value, int maxLen = int.MaxValue, int minLen = 0)
        {
            if (value == null)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value.Length > maxLen)
                    throw new InvalidDataException("Constraint Violated! Maximum length: " + maxLen + ", Actual: " + value.Length);
                if (value.Length < minLen)
                    throw new InvalidDataException("Constraint Violated! Minimum length: " + minLen + ", Actual: " + value.Length);
                if (value.Length <= byte.MaxValue)
                {
                    WriteTag(tagNumber, ElementType.String8);
                    writer.Write((byte)Encoding.UTF8.GetByteCount(value));
                }
                else if (value.Length <= ushort.MaxValue)
                {
                    WriteTag(tagNumber, ElementType.String16);
                    writer.Write((ushort)Encoding.UTF8.GetByteCount(value));
                }
                else
                {
                    WriteTag(tagNumber, ElementType.String32);
                    writer.Write((uint)Encoding.UTF8.GetByteCount(value));
                }
                writer.Write(value);
            }
        }

        public void WriteBytes(long tagNumber, byte[] value, int maxLen = int.MaxValue, int minLen = 0)
        {
            if (value == null)
                WriteTag(tagNumber, ElementType.Null);
            else
            {
                if (value.Length > maxLen)
                    throw new InvalidDataException("Constraint Violated! Maximum length: " + maxLen + ", Actual: " + value.Length);
                if (value.Length < minLen)
                    throw new InvalidDataException("Constraint Violated! Minimum length: " + minLen + ", Actual: " + value.Length);
                if (value.Length <= byte.MaxValue)
                {
                    WriteTag(tagNumber, ElementType.Bytes8);
                    writer.Write((byte)value.Length);
                }
                else if (value.Length <= ushort.MaxValue)
                {
                    WriteTag(tagNumber, ElementType.Bytes16);
                    writer.Write((ushort)value.Length);
                }
                else
                {
                    WriteTag(tagNumber, ElementType.Bytes32);
                    writer.Write((uint)value.Length);
                }
                writer.Write(value);
            }
        }

        public void WriteAny(long tagNumber, object any)
        {
            if (any is TLVPayload payload)
                payload.Serialize(this, tagNumber);
            else if (any is Array array)
            {
                StartArray(tagNumber);
                foreach (object item in array)
                    WriteAny(-1, item);
                EndContainer();
            }
            else if (any == null)
                WriteTag(tagNumber, ElementType.Null);
            else if (any is bool)
                WriteBool(tagNumber, (bool)any);
            else if (any is uint || any is ulong || any is ushort || any is byte)
                WriteULong(tagNumber, (ulong)(dynamic)any);
            else if (any is int || any is long || any is short || any is sbyte)
                WriteLong(tagNumber, (long)(dynamic)any);
            else if (any is byte[])
                WriteBytes(tagNumber, (byte[])any);
            else if (any is string)
                WriteString(tagNumber, (string)any);
            else if (any is float)
                WriteFloat(tagNumber, (float)any);
            else if (any is double)
                WriteDouble(tagNumber, (double)any);
            else
                throw new NotImplementedException("Unknown type " + any.GetType());
        }
    }
}
