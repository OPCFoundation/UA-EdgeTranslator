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


namespace MatterDotNet.Protocol.Parsers
{
    using System.Collections.Generic;
    using System.IO;

    public class FieldReader(IList<object> fields)
    {
        public int Count { get { return fields.Count; } }
        public byte? GetByte(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is byte value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type byte but received {fields[(int)tagNumber].GetType()}");
        }
        public sbyte? GetSByte(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is sbyte value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type sbyte but received {fields[(int)tagNumber].GetType()}");
        }
        public bool? GetBool(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is bool value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type bool but received {fields[(int)tagNumber].GetType()}");
        }
        public short? GetShort(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is sbyte smallerVal)
                return smallerVal;
            if (fields[(int)tagNumber] is short value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type short but received {fields[(int)tagNumber].GetType()}");
        }

        public ushort? GetUShort(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is byte smallerVal)
                return smallerVal;
            if (fields[(int)tagNumber] is ushort value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type ushort but received {fields[(int)tagNumber].GetType()}");
        }

        public decimal? GetUDecimal(long tagNumber, bool nullable = false)
        {
            ushort? val = GetUShort(tagNumber, nullable);
            if (!val.HasValue)
                return val;
            decimal ret = (int)(val.Value / 100);
            ret += (val.Value % 100) / 100M;
            return ret;
        }

        public decimal? GetDecimal(long tagNumber, bool nullable = false)
        {
            short? val = GetShort(tagNumber, nullable);
            if (!val.HasValue)
                return val;
            decimal ret = (int)(val.Value / 100);
            ret += (val.Value % 100) / 100M;
            return ret;
        }

        public int? GetInt(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is sbyte smallestVal)
                return smallestVal;
            if (fields[(int)tagNumber] is short smallerVal)
                return smallerVal;
            if (fields[(int)tagNumber] is int value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type int but received {fields[(int)tagNumber].GetType()}");
        }

        public uint? GetUInt(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is byte smallestVal)
                return smallestVal;
            if (fields[(int)tagNumber] is ushort smallerVal)
                return smallerVal;
            if (fields[(int)tagNumber] is uint value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type uint but received {fields[(int)tagNumber].GetType()}");
        }

        public long? GetLong(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is sbyte smallestVal)
                return smallestVal;
            if (fields[(int)tagNumber] is short smallerVal)
                return smallerVal;
            if (fields[(int)tagNumber] is int smallVal)
                return smallVal;
            if (fields[(int)tagNumber] is long value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type long but received {fields[(int)tagNumber].GetType()}");
        }

        public ulong? GetULong(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is byte smallestVal)
                return smallestVal;
            if (fields[(int)tagNumber] is ushort smallerVal)
                return smallerVal;
            if (fields[(int)tagNumber] is uint smallVal)
                return smallVal;
            if (fields[(int)tagNumber] is ulong value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type ulong but received {fields[(int)tagNumber].GetType()}");
        }

        public float? GetFloat(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is float value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type float but received {fields[(int)tagNumber].GetType()}");
        }

        public double? GetDouble(long tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is double value)
                return value;
            throw new InvalidDataException($"Tag {tagNumber}: Expected type double but received {fields[(int)tagNumber].GetType()}");
        }

        public string GetString(long tagNumber, bool nullable = false, int maxLength = int.MaxValue, int minLength = 0)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is not string value)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type string but received {fields[(int)tagNumber].GetType()}");
            if (value.Length > maxLength)
                throw new InvalidDataException($"Constraint Violation! Max length is {maxLength} but received {value.Length}");
            if (value.Length < minLength)
                throw new InvalidDataException($"Constraint Violation! Min length is {minLength} but received {value.Length}");
            return value;
        }

        public byte[] GetBytes(long tagNumber, bool nullable = false, int maxLength = int.MaxValue, int minLength = 0)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[(int)tagNumber] == null && nullable)
                return null;
            if (fields[(int)tagNumber] is not byte[] value)
                throw new InvalidDataException($"Tag {tagNumber}: Expected type byte[] but received {fields[(int)tagNumber].GetType()}");
            if (value.Length > maxLength)
                throw new InvalidDataException($"Constraint Violation! Max length is {maxLength} but received {value.Length}");
            if (value.Length < minLength)
                throw new InvalidDataException($"Constraint Violation! Min length is {minLength} but received {value.Length}");
            return value;
        }

        public object[] GetStruct(int tagNumber, bool nullable = false)
        {
            if (fields.Count <= tagNumber)
                throw new InvalidDataException("Tag " + tagNumber + " not present");
            if (fields[tagNumber] == null && nullable)
                return null;
            return (object[])fields[tagNumber];
        }

        internal bool Has(int tagNumber)
        {
            return tagNumber < fields.Count;
        }
    }
}
