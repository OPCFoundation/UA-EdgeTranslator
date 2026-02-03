using System;
using System.Collections.Generic;
using System.Text;

namespace Matter.Core
{
    /// <summary>
    /// See Appendix A of the Matter Specification for the TLV encoding.
    /// </summary>
    public class MatterTLV
    {
        private List<byte> _values = new();

        private int _pointer = 0;

        public MatterTLV()
        {
        }

        public MatterTLV(byte[] payload)
        {
            _values = [.. payload];
        }

        internal byte[] Serialize()
        {
            return _values.ToArray();
        }

        public bool IsTagNext(int? tagNumber)
        {
            // Skip the Control octet by adding 1.
            return _values[_pointer + 1] == (byte)tagNumber; // Check if the next tag matches the expected tag number
        }

        public bool IsEndContainerNext()
        {
            // Check if the next tag is an End Container (either 0x18 or 0x1F. 0x1F is an alias for backward compat)
            byte elementType = (byte)(_values[_pointer] & (byte)ElementType.ElementTypeMask);

            return (elementType == (byte)ElementType.EndOfContainer);// || (elementType == 0x1F);
        }

        public byte PeekElementType()
        {
            // Low 5 bits of the control octet encode the element type.
            return (byte)(_values[_pointer] & (byte)ElementType.ElementTypeMask);
        }

        public int? PeekTagNumber()
        {
            byte control = _values[_pointer];
            int tagControl = (control >> 5) & 0x03; // top 2 bits for tag control

            return tagControl switch
            {
                0 => null, // anonymous
                1 => _values[_pointer + 1], // context-specific (1 byte)
                2 => BitConverter.ToUInt16(_values.ToArray(), _pointer + 1), // common profile (2 bytes)
                3 => (_values[_pointer + 1] << 16) | (_values[_pointer + 2] << 8) | _values[_pointer + 3], // fully qualified (3 bytes)
                _ => throw new Exception("Invalid tag control")
            };
        }

        public MatterTLV AddStructure()
        {
            // Anonymous i.e. has no tag number.
            _values.Add((byte)ElementType.Structure);

            return this;
        }

        public MatterTLV AddStructure(byte tagNumber)
        {
            _values.Add((byte)(ElementType.ContextSpecific | ElementType.Structure));
            _values.Add(tagNumber);

            return this;
        }

        public MatterTLV AddArray(byte tagNumber)
        {
            _values.Add((byte)(ElementType.ContextSpecific | ElementType.Array));
            _values.Add(tagNumber);

            return this;
        }

        public MatterTLV AddList(long tagNumber)
        {
            _values.Add((byte)(ElementType.ContextSpecific | ElementType.List));
            _values.Add((byte)tagNumber);

            return this;
        }

        public MatterTLV AddList()
        {
            _values.Add((byte)ElementType.List);

            return this;
        }

        public MatterTLV AddUTF8String(byte tagNumber, string value)
        {
            var utf8String = Encoding.UTF8.GetBytes(value);
            var stringLength = value.Length;

            if (stringLength <= 255)
            {
                _values.Add((byte)(ElementType.ContextSpecific | ElementType.String8)); // UTFString, 1-octet length
                _values.Add(tagNumber);
                _values.Add((byte)stringLength);
                _values.AddRange(utf8String);
            }
            else if (stringLength <= ushort.MaxValue)
            {
                _values.Add((byte)(ElementType.ContextSpecific | ElementType.String16)); // UTFString, 2-octet length
                _values.Add(tagNumber);
                _values.AddRange(BitConverter.GetBytes((ushort)stringLength));
                _values.AddRange(utf8String);
            }
            else
            {
                _values.Add((byte)(ElementType.ContextSpecific | ElementType.String32)); // UTFString, 4-octet length
                _values.Add(tagNumber);
                _values.AddRange(BitConverter.GetBytes((uint)stringLength));
                _values.AddRange(utf8String);
            }

            return this;
        }

        public MatterTLV AddOctetString(byte tagNumber, byte[] value)
        {
            var valueLength = value.Length;

            if (valueLength <= 255)
            {
                _values.Add((byte)(ElementType.ContextSpecific | ElementType.Bytes8)); // Octet String, 1-octet length
                _values.Add(tagNumber);
                _values.Add((byte)value.Length);
                _values.AddRange(value);
            }
            else if (valueLength <= ushort.MaxValue)
            {
                _values.Add((byte)(ElementType.ContextSpecific | ElementType.Bytes16)); // Octet String, 2-octet length
                _values.Add(tagNumber);
                _values.AddRange(BitConverter.GetBytes((ushort)value.Length));
                _values.AddRange(value);
            }
            else
            {
                _values.Add((byte)(ElementType.ContextSpecific | ElementType.Bytes32)); // Octet String, 4-octet length
                _values.Add(tagNumber);
                _values.AddRange(BitConverter.GetBytes((uint)value.Length));
                _values.AddRange(value);
            }

            return this;
        }

        public MatterTLV AddUInt8(byte tagNumber, byte value)
        {
            _values.Add((byte)(ElementType.ContextSpecific | ElementType.Byte));
            _values.Add(tagNumber);
            _values.Add(value); // No length required

            return this;
        }

        public MatterTLV AddUInt8(byte value)
        {
            _values.Add((byte)ElementType.Byte);
            _values.Add(value);

            return this;
        }

        public MatterTLV AddInt8(byte tagNumber, sbyte value)
        {
            _values.Add((byte)(ElementType.ContextSpecific | ElementType.SByte));
            _values.Add(tagNumber);
            _values.Add((byte)value); // No length required

            return this;
        }

        public MatterTLV AddInt8(sbyte value)
        {
            _values.Add((byte)ElementType.SByte);
            _values.Add((byte)value);

            return this;
        }

        public MatterTLV AddInt16(byte tagNumber, short value)
        {
            if (value < sbyte.MaxValue && value > sbyte.MinValue)
            {
                return AddInt8(tagNumber, (sbyte)value);
            }

            _values.Add((byte)(ElementType.ContextSpecific | ElementType.Short));
            _values.Add(tagNumber);
            _values.AddRange(BitConverter.GetBytes(value)); // No length required.

            return this;
        }

        public MatterTLV AddUInt16(byte tagNumber, ushort value)
        {
            if (value < byte.MaxValue)
            {
                return AddUInt8(tagNumber, (byte)value);
            }

            _values.Add((byte)(ElementType.ContextSpecific | ElementType.UShort));
            _values.Add(tagNumber);
            _values.AddRange(BitConverter.GetBytes(value)); // No length required.

            return this;
        }

        public MatterTLV AddInt32(byte tagNumber, int value)
        {
            if (value < short.MaxValue && value > short.MinValue)
            {
                return AddInt16(tagNumber, (short)value);
            }

            _values.Add((byte)(ElementType.ContextSpecific | ElementType.Int));
            _values.Add(tagNumber);
            _values.AddRange(BitConverter.GetBytes(value)); // No length required.

            return this;
        }

        public MatterTLV AddUInt32(byte tagNumber, uint value)
        {
            if (value < ushort.MaxValue)
            {
                return AddUInt16(tagNumber, (ushort)value);
            }

            _values.Add((byte)(ElementType.ContextSpecific | ElementType.UInt));
            _values.Add(tagNumber);
            _values.AddRange(BitConverter.GetBytes(value)); // No length required.

            return this;
        }

        public MatterTLV AddInt64(byte tagNumber, long value)
        {
            if (value < int.MaxValue && value > int.MinValue)
            {
                return AddInt32(tagNumber, (int)value);
            }

            _values.Add((byte)(ElementType.ContextSpecific | ElementType.Long));
            _values.Add(tagNumber);
            _values.AddRange(BitConverter.GetBytes(value)); // No length required.

            return this;
        }

        public MatterTLV AddUInt64(byte tagNumber, ulong value)
        {
            if (value < uint.MaxValue)
            {
                return AddUInt32(tagNumber, (uint)value);
            }

            _values.Add((byte)(ElementType.ContextSpecific | ElementType.ULong));
            _values.Add(tagNumber);
            _values.AddRange(BitConverter.GetBytes(value)); // No length required.

            return this;
        }

        public MatterTLV AddUInt64(byte tagNumber, byte[] value)
        {
            if (value.Length != 8)
            {
                throw new Exception("Value must be 8 bytes long");
            }

            _values.Add((byte)(ElementType.ContextSpecific | ElementType.ULong));
            _values.Add(tagNumber);
            _values.AddRange(value);

            return this;
        }

        public MatterTLV AddBool(byte tagNumber, bool value)
        {
            if (value)
            {
                _values.Add((byte)(ElementType.ContextSpecific | ElementType.True));
            }
            else
            {
                _values.Add((byte)(ElementType.ContextSpecific | ElementType.False));
            }

            _values.Add(tagNumber);

            return this;
        }

        public MatterTLV AddDouble(byte tagNumber, double value)
        {
            _values.Add((byte)(ElementType.ContextSpecific | ElementType.Double));
            _values.Add(tagNumber);
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddFloat(byte tagNumber, float value)
        {
            _values.Add((byte)(ElementType.ContextSpecific | ElementType.Float));
            _values.Add(tagNumber);
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddObject(byte tagNumber, object value)
        {
            if (value is bool)
            {
                AddBool(tagNumber, (bool)value);
            }
            else if (value is uint || value is ulong || value is ushort || value is byte)
            {
                AddUInt64(tagNumber, (ulong)value);
            }
            else if (value is int || value is long || value is short || value is sbyte)
            {
                AddInt64(tagNumber, (long)value);
            }
            else if (value is float)
            {
                AddFloat(tagNumber, (float)value);
            }
            else if (value is double)
            {
                AddDouble(tagNumber, (double)value);
            }
            else if (value is byte[])
            {
                AddOctetString(tagNumber, (byte[])value);
            }
            else if (value is string)
            {
                AddUTF8String(tagNumber, (string)value);
            }
            else if (value is Array array)
            {
                AddArray(tagNumber);
                for (byte i = 0; i < array.Length; i++)
                {
                    AddObject(i, array.GetValue(i));
                }
                EndContainer();
            }
            else
            {
                throw new Exception("Unknown type " + value.GetType());
            }

            return this;
        }

        public MatterTLV EndContainer()
        {
            _values.Add((byte)ElementType.EndOfContainer);

            return this;
        }

        public void OpenStructure()
        {
            if (_values[_pointer++] != (byte)ElementType.Structure)
            {
                throw new Exception("Expected Structure not found");
            }
        }

        public void OpenStructure(int? tag)
        {
            int tagControl = _values[_pointer] >> 5;

            if ((0x1F & _values[_pointer++]) != (byte)ElementType.Structure)
            {
                throw new Exception("Expected Structure not found");
            }

            if (tag is null)
            {
                if (tagControl == 0x01)
                {
                    _pointer++; // Skip the tag byte. We can't compare since we don't know the tag.
                }
            }
            else
            {
                if (_values[_pointer++] != (byte)tag)
                {
                    throw new Exception($"Expected tag number {tag} not found");
                }
            }
        }

        public void OpenArray(int? tag)
        {
            int tagControl = _values[_pointer] >> 5;

            if ((0x1F & _values[_pointer++]) != (byte)ElementType.Array)
            {
                throw new Exception("Expected Array not found");
            }

            if (tag is null)
            {
                if (tagControl == 0x01)
                {
                    _pointer++; // Skip the tag byte. We can't compare since we don't know the tag.
                }
            }
            else
            {
                if (_values[_pointer++] != (byte)tag)
                {
                    throw new Exception($"Expected tag number {tag} not found");
                }
            }
        }

        public void OpenList(int? tag)
        {
            int tagControl = _values[_pointer] >> 5;

            if ((0x1F & _values[_pointer++]) != (byte)ElementType.List)
            {
                throw new Exception("Expected List not found");
            }

            if (tag is null)
            {
                if (tagControl == 0x01)
                {
                    _pointer++; // Skip the tag byte. We can't compare since we don't know the tag.
                }
            }
            else
            {
                if (_values[_pointer++] != (byte)tag)
                {
                    throw new Exception("Expected tag number not found");
                }
            }
        }

        public bool GetBoolean(int? tag)
        {
            var selectedByte = _values[_pointer++];

            if ((selectedByte != (byte)(ElementType.ContextSpecific | ElementType.True)) && (selectedByte != (byte)(ElementType.ContextSpecific | ElementType.False)))
            {
                throw new Exception("Expected Boolean not found");
            }

            bool value = selectedByte == (byte)(ElementType.ContextSpecific | ElementType.True);

            if (_values[_pointer++] != (byte)tag)
            {
                throw new Exception("Expected tag number not found");
            }

            return value;
        }

        public byte[] GetOctetString(int? tag)
        {
            int length = 0;
            if (((byte)ElementType.ElementTypeMask & _values[_pointer]) == (byte)ElementType.Bytes64)
            {
                length = 8;
            }
            else if (((byte)ElementType.ElementTypeMask & _values[_pointer]) == (byte)ElementType.Bytes32)
            {
                length = 4;
            }
            else if (((byte)ElementType.ElementTypeMask & _values[_pointer]) == (byte)ElementType.Bytes16)
            {
                length = 2;
            }
            else if (((byte)ElementType.ElementTypeMask & _values[_pointer]) == (byte)ElementType.Bytes8)
            {
                length = 1;
            }

            _pointer++;

            if (_values[_pointer++] != (byte)tag)
            {
                throw new Exception("Expected tag number not found");
            }

            ulong valueLength = 0;
            if (length == 1)
            {
                valueLength = _values[_pointer];
                _pointer += 1;
            }
            else if (length == 2)
            {
                valueLength = BitConverter.ToUInt16(_values.ToArray(), _pointer);
                _pointer += 2;
            }
            else if (length == 4)
            {
                valueLength = BitConverter.ToUInt32(_values.ToArray(), _pointer);
                _pointer += 4;
            }
            else if (length == 8)
            {
                valueLength = BitConverter.ToUInt64(_values.ToArray(), _pointer);
                _pointer += 8;
            }

            var bytes = new byte[valueLength];

            Array.Copy(_values.ToArray(), _pointer, bytes, 0, (int)valueLength);

            _pointer += (int)valueLength;

            return bytes;
        }

        public string GetUTF8String(int? tag)
        {
            int length = 0;
            if (((byte)ElementType.ElementTypeMask & _values[_pointer]) == (byte)ElementType.String8)
            {
                length = 1;
            }
            else if (((byte)ElementType.ElementTypeMask & _values[_pointer]) == (byte)ElementType.String16)
            {
                length = 2;
            }
            else if (((byte)ElementType.ElementTypeMask & _values[_pointer]) == (byte)ElementType.String32)
            {
                length = 4;
            }
            else if (((byte)ElementType.ElementTypeMask & _values[_pointer]) == (byte)ElementType.String64)
            {
                length = 8;
            }

            _pointer++;

            if (_values[_pointer++] != (byte)tag)
            {
                throw new Exception("Expected tag number not found");
            }

            ulong valueLength = 0;
            if (length == 1)
            {
                valueLength = _values[_pointer];
                _pointer += 1;
            }
            else if (length == 2)
            {
                valueLength = BitConverter.ToUInt16(_values.ToArray(), _pointer);
                _pointer += 2;
            }
            else if (length == 4)
            {
                valueLength = BitConverter.ToUInt32(_values.ToArray(), _pointer);
                _pointer += 4;
            }
            else if (length == 8)
            {
                valueLength = BitConverter.ToUInt64(_values.ToArray(), _pointer);
                _pointer += 8;
            }

            var bytes = new byte[valueLength];

            Array.Copy(_values.ToArray(), _pointer, bytes, 0, (int)valueLength);

            _pointer += (int)valueLength;

            return Encoding.UTF8.GetString(bytes);
        }

        internal long GetSignedInt(int? tag)
        {
            int tagControl = _values[_pointer] >> 5;
            var elementType = ((byte)ElementType.ElementTypeMask & _values[_pointer++]);

            if (tag is null)
            {
                if (tagControl == 0x01)
                {
                    _pointer++; // Skip the tag byte. We can't compare since we don't know the tag.
                }
            }
            else
            {
                if (_values[_pointer++] != (byte)tag)
                {
                    throw new Exception("Expected tag number not found");
                }
            }

            long value;
            switch (elementType)
            {
                case (byte)ElementType.SByte:
                    value = (sbyte)_values[_pointer];
                    _pointer += 1;
                    break;
                case (byte)ElementType.Short:
                    value = BitConverter.ToInt16(_values.ToArray(), _pointer);
                    _pointer += 2;
                    break;
                case (byte)ElementType.Int:
                    value = BitConverter.ToInt32(_values.ToArray(), _pointer);
                    _pointer += 4;
                    break;
                case (byte)ElementType.Long:
                    value = BitConverter.ToInt64(_values.ToArray(), _pointer);
                    _pointer += 8;
                    break;
                default:
                    throw new Exception($"Unexpected element type {elementType}");
            }

            return value;
        }

        internal ulong GetUnsignedInt(int? tag)
        {
            int tagControl = _values[_pointer] >> 5;
            var elementType = ((byte)ElementType.ElementTypeMask & _values[_pointer++]);

            if (tag is null)
            {
                if (tagControl == 0x01)
                {
                    _pointer++; // Skip the tag byte. We can't compare since we don't know the tag.
                }
            }
            else
            {
                if (_values[_pointer++] != (byte)tag)
                {
                    throw new Exception("Expected tag number not found");
                }
            }

            ulong value;
            switch (elementType)
            {
                case (byte)ElementType.Byte:
                    value = _values[_pointer];
                    _pointer += 1;
                    break;
                case (byte)ElementType.UShort:
                    value = BitConverter.ToUInt16(_values.ToArray(), _pointer);
                    _pointer += 2;
                    break;
                case (byte)ElementType.UInt:
                    value = BitConverter.ToUInt32(_values.ToArray(), _pointer);
                    _pointer += 4;
                    break;
                case (byte)ElementType.ULong:
                    value = BitConverter.ToUInt64(_values.ToArray(), _pointer);
                    _pointer += 8;
                    break;
                default:
                    throw new Exception($"Unexpected element type {elementType}");
            }

            return value;
        }

        public object GetObject(int? tag)
        {
            int elementType = PeekElementType();
            switch (elementType)
            {
                case (byte)ElementType.True:
                case (byte)ElementType.False:
                    return GetBoolean(tag);

                case (byte)ElementType.SByte:
                case (byte)ElementType.Short:
                case (byte)ElementType.Int:
                case (byte)ElementType.Long:
                    return GetSignedInt(tag);

                case (byte)ElementType.Byte:
                case (byte)ElementType.UShort:
                case (byte)ElementType.UInt:
                case (byte)ElementType.ULong:
                    return GetUnsignedInt(tag);

                case (byte)ElementType.Bytes8:
                case (byte)ElementType.Bytes16:
                case (byte)ElementType.Bytes32:
                case (byte)ElementType.Bytes64:
                    return GetOctetString(tag);

                case (byte)ElementType.String8:
                case (byte)ElementType.String16:
                case (byte)ElementType.String32:
                case (byte)ElementType.String64:
                    return GetUTF8String(tag);

                case (byte)ElementType.Structure:

                    List<object> structure = new();

                    OpenStructure(tag);
                    while (!IsEndContainerNext())
                    {
                        int? tagNumber = PeekTagNumber();
                        structure.Add(GetObject(tagNumber));
                    }
                    CloseContainer();
                    return structure;

                case (byte)ElementType.Array:

                    List<object> array = new();

                    OpenArray(tag);
                    while (!IsEndContainerNext())
                    {
                        int? tagNumber = PeekTagNumber();
                        array.Add(GetObject(tagNumber));
                    }
                    CloseContainer();
                    return array;

                case (byte)ElementType.List:

                    List<object> list = new();

                    OpenList(tag);
                    while (!IsEndContainerNext())
                    {
                        int? tagNumber = PeekTagNumber();
                        list.Add(GetObject(tagNumber));
                    }
                    CloseContainer();
                    return list;

                default:
                    throw new Exception($"Unsupported element type {elementType}");
            }
        }

        public void CloseContainer()
        {
            if (_values[_pointer++] != (byte)ElementType.EndOfContainer)
            {
                throw new Exception("Expected EndContainer not found");
            }
        }
    }
}
