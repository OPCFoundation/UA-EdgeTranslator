using System;
using System.Collections.Generic;
using System.IO;
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
            // Empty constructor
        }

        public MatterTLV(byte[] payload)
        {
            _values = [.. payload];
        }

        public MatterTLV AddStructure()
        {
            // Anonymous i.e. has no tag number.
            _values.Add(0x15);
            return this;
        }

        public MatterTLV AddStructure(byte tagNumber)
        {
            // Anonymous i.e. has no tag number.
            _values.Add(0x01 << 5 | 0x15);
            _values.Add(tagNumber);
            return this;
        }

        public MatterTLV AddArray(byte tagNumber)
        {
            // This is a Context-Specific Tag (0x01), shifted 5 bits and then OR'd with 0x16
            // to produce a context tag for Array, 1 byte long
            // 00110110
            _values.Add(0x01 << 5 | 0x16);
            _values.Add(tagNumber);
            return this;
        }

        public MatterTLV AddArray()
        {
            // This is an anonymous tag, shifted 5 bits and then OR'd with 0x22
            // 00010110
            _values.Add(0x16);
            return this;
        }

        public MatterTLV AddList(long tagNumber)
        {
            // This is a Context-Specific Tag (0x01), shifted 5 bits and then OR'd with 0x17
            // to produce a context tag for List, one byte long
            // 00110111
            _values.Add(0x01 << 5 | 0x17);
            _values.Add((byte)tagNumber);
            return this;
        }

        public MatterTLV AddList()
        {
            _values.Add(0x17);
            return this;
        }

        public MatterTLV EndContainer()
        {
            _values.Add(0x18);
            return this;
        }

        public MatterTLV AddUTF8String(byte tagNumber, string value)
        {
            var utf8String = Encoding.UTF8.GetBytes(value);
            var stringLength = value.Length;

            if (stringLength <= 255)
            {
                _values.Add(0x01 << 5 | 0x0C); // UTFString, 1-octet length
                _values.Add(tagNumber);
                _values.Add((byte)stringLength);
                _values.AddRange(utf8String);
            }
            else if (stringLength <= ushort.MaxValue)
            {
                _values.Add(0x01 << 5 | 0x0D); // UTFString, 2-octet length
                _values.Add(tagNumber);
                _values.AddRange(BitConverter.GetBytes((ushort)stringLength));
                _values.AddRange(utf8String);
            }
            else
            {
                _values.Add(0x01 << 5 | 0x0E); // UTFString, 4-octet length
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
                // This is a Context-Specific Tag, shifted 5 bits and then OR'd with 10
                // to produce a context tag for Octet String, 1 bytes length
                // 00110000
                _values.Add(0x01 << 5 | 0x10); // Octet String, 1-octet length
                _values.Add(tagNumber);
                _values.Add((byte)value.Length);
                _values.AddRange(value);
            }
            else if (valueLength <= ushort.MaxValue)
            {
                // This is a Context-Specific Tag, shifted 5 bits and then OR'd with 11
                // to produce a context tag for Octet String, 2 bytes length
                // 00110001
                _values.Add(0x01 << 5 | 0x11); // Octet String, 2-octet length
                _values.Add(tagNumber);
                _values.AddRange(BitConverter.GetBytes((ushort)value.Length));
                _values.AddRange(value);
            }
            else
            {
                // This is a context type 1, shifted 5 bits and then OR'd with 12
                // to produce a context tag for Octet String, 4 bytes
                // 00110010
                _values.Add(0x01 << 5 | 0x12); // Octet String, 4-octet length
                _values.Add(tagNumber);
                _values.AddRange(BitConverter.GetBytes((uint)value.Length));
                _values.AddRange(value);
            }

            return this;
        }

        public MatterTLV AddUInt8(byte tagNumber, byte value)
        {
            _values.Add(0x01 << 5 | 0x4);
            _values.Add(tagNumber);

            // No length required
            _values.Add(value);

            return this;
        }

        public MatterTLV AddUInt8(byte value)
        {
            _values.Add(0x4);
            _values.Add(value);

            return this;
        }

        public MatterTLV AddInt8(byte tagNumber, sbyte value)
        {
            _values.Add(0x01 << 5 | 0x0);
            _values.Add(tagNumber);

            // No length required
            _values.Add((byte)value);

            return this;
        }

        public MatterTLV AddInt8(sbyte value)
        {
            _values.Add(0x0);
            _values.Add((byte)value);

            return this;
        }

        public MatterTLV AddInt16(byte tagNumber, short value)
        {
            if (value < sbyte.MaxValue && value > sbyte.MinValue)
            {
                return AddInt8(tagNumber, (sbyte)value);
            }

            _values.Add(0x01 << 5 | 0x1);
            _values.Add(tagNumber);

            // No length required.
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddInt16(short value)
        {
            if (value < sbyte.MaxValue && value > sbyte.MinValue)
            {
                return AddInt8((sbyte)value);
            }

            _values.Add(0x1);
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddUInt16(byte tagNumber, ushort value)
        {
            if (value < byte.MaxValue)
            {
                return AddUInt8(tagNumber, (byte)value);
            }

            _values.Add(0x01 << 5 | 0x5);
            _values.Add(tagNumber);

            // No length required.
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddUInt16(ushort value)
        {
            if (value < byte.MaxValue)
            {
                return AddUInt8((byte)value);
            }

            _values.Add(0x5);
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddInt32(byte tagNumber, int value)
        {
            if (value < short.MaxValue && value > short.MinValue)
            {
                return AddInt16(tagNumber, (short)value);
            }

            _values.Add(0x01 << 5 | 0x2);
            _values.Add(tagNumber);

            // No length required.
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddInt32(int value)
        {
            if (value < short.MaxValue && value > short.MinValue)
            {
                return AddInt16((short)value);
            }

            _values.Add(0x2);
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddUInt32(byte tagNumber, uint value)
        {
            if (value < ushort.MaxValue)
            {
                return AddUInt16(tagNumber, (ushort)value);
            }

            _values.Add(0x01 << 5 | 0x6);
            _values.Add(tagNumber);

            // No length required.
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddUInt32(uint value)
        {
            if (value < ushort.MaxValue)
            {
                return AddUInt16((ushort)value);
            }

            _values.Add(0x6);
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddInt64(byte tagNumber, long value)
        {
            if (value < int.MaxValue && value > int.MinValue)
            {
                return AddInt32(tagNumber, (int)value);
            }

            _values.Add(0x01 << 5 | 0x3);
            _values.Add(tagNumber);

            // No length required.
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddInt64(long value)
        {
            if (value < int.MaxValue && value > int.MinValue)
            {
                return AddInt32((int)value);
            }

            _values.Add(0x3);
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddUInt64(byte tagNumber, ulong value)
        {
            if (value < uint.MaxValue)
            {
                return AddUInt32(tagNumber, (uint)value);
            }

            _values.Add(0x01 << 5 | 0x7);
            _values.Add(tagNumber);

            // No length required.
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddUInt64(ulong value)
        {
            if (value < uint.MaxValue)
            {
                return AddUInt32((uint)value);
            }

            _values.Add(0x7);
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddUInt64(byte tagNumber, byte[] value)
        {
            if (value.Length != 8)
            {
                throw new Exception("Value must be 8 bytes long");
            }
            _values.Add(0x01 << 5 | 0x7);
            _values.Add(tagNumber);
            _values.AddRange(value);

            return this;
        }

        public MatterTLV AddBool(byte tagNumber, bool value)
        {
            if (value)
            {
                _values.Add(0x01 << 5 | 0x09); // Boolean TRUE
            }
            else
            {
                _values.Add(0x01 << 5 | 0x08); // Boolean FALSE
            }

            _values.Add(tagNumber);

            return this;
        }

        internal byte[] Serialize()
        {
            return _values.ToArray();
        }

        public bool IsNextTag(int tagNumber)
        {
            // Skip the Control octet by adding 1.
            //
            return _values[_pointer + 1] == (byte)tagNumber; // Check if the next tag matches the expected tag number
        }

        public bool IsEndContainerNext()
        {
            return _values[_pointer] == 0x18; // Check if the next tag is an End Container
        }

        public void OpenStructure()
        {
            if (_values[_pointer++] != 0x15) // Tag Anonymous Structure
            {
                throw new Exception("Expected Structure not found");
            }
        }

        public void OpenStructure(int? tag)
        {
            int tagControl = _values[_pointer] >> 5;

            if ((0x1F & _values[_pointer++]) != 0x15) // Structure
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

            if ((0x1F & _values[_pointer++]) != 0x16) // Array
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

            if ((0x1F & _values[_pointer++]) != 0x17) // List
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

        public bool GetBoolean(int tag)
        {
            var selectedByte = _values[_pointer++];

            if (selectedByte != 0x28 && selectedByte != 0x29) // Context Boolean (false)
            {
                throw new Exception("Expected Boolean not found");
            }

            bool value = selectedByte == 0x29; // True

            if (_values[_pointer++] != (byte)tag)
            {
                throw new Exception("Expected tag number not found");
            }

            return value;
        }

        public byte[] GetOctetString(int tag)
        {
            // Check the Control Octet.
            //
            int length = 0;

            if ((0x1F & _values[_pointer]) == 0x13)
            {
                // Octet String, 8 - octet length
                length = 8;
            }
            else if ((0x1F & _values[_pointer]) == 0x12)
            {
                // Octet String, 4 - octet length
                length = 4;
            }
            else if ((0x1F & _values[_pointer]) == 0x11)
            {
                // Octet String, 2 - octet length
                length = 2;
            }
            else if ((0x1F & _values[_pointer]) == 0x10) // Context Octet String, 1 - octet length
            {
                // Octet String, 1 - octet length
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
                valueLength = _values[_pointer++];
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

        public string GetUTF8String(int tag)
        {
            // Check the Control Octet.
            //
            int length = 0;

            if ((0x1F & _values[_pointer]) == 0x0C)
            {
                //Octet String, 1 - octet length
                length = 1;
            }
            else if ((0x1F & _values[_pointer]) == 0x0D)
            {
                //Octet String, 2 - octet length
                length = 2;
            }
            else if ((0x1F & _values[_pointer]) == 0x0E)
            {
                //Octet String, 4 - octet length
                length = 4;
            }
            else if ((0x1F & _values[_pointer]) == 0x0F)
            {
                //Octet String, 8 - octet length
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
                valueLength = _values[_pointer++];
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
            var elementType = (0x1F & _values[_pointer++]);

            if (tag is null)
            {
                _pointer++; // Skip the tag byte. We can't compare since we don't know the tag.
            }
            else
            {
                if (_values[_pointer++] != (byte)tag)
                {
                    throw new Exception("Expected tag number not found");
                }
            }

            long value = 0;

            switch (elementType)
            {
                case 0x00: // 1 Byte Unsigned Integer
                    value = Convert.ToInt64(_values[_pointer++]);
                    break;
                case 0x01: // 2 Byte Unsigned Integer
                    value = BitConverter.ToInt16(_values.ToArray(), _pointer);
                    _pointer += 2;
                    break;
                default:
                    throw new Exception($"Unexpected element type {elementType}");
            }

            return value;
        }

        internal ulong GetUnsignedInt(int? tag)
        {
            var elementType = (0x1F & _values[_pointer++]);

            if (tag is null)
            {
                _pointer++; // Skip the tag byte. We can't compare since we don't know the tag.
            }
            else
            {
                if (_values[_pointer++] != (byte)tag)
                {
                    throw new Exception("Expected tag number not found");
                }
            }

            ulong value = 0;

            switch (elementType)
            {
                case 0x04: // 1 Byte Unsigned Integer
                    value = Convert.ToUInt64(_values[_pointer++]);
                    break;
                case 0x05: // 2 Byte Unsigned Integer
                    value = (ulong)BitConverter.ToUInt16(_values.ToArray(), _pointer);
                    _pointer += 2;
                    break;
                default:
                    throw new Exception($"Unexpected element type {elementType}");
            }

            return value;
        }

        public sbyte GetSignedInt8(int tag)
        {
            if ((0x1F & _values[_pointer++]) != 0x00)
            {
                throw new Exception("Expected Signed Integer, 1-octet value not found");
            }

            if (_values[_pointer++] != (byte)tag)
            {
                throw new Exception("Expected tag number not found");
            }

            sbyte value = (sbyte)_values[_pointer++];

            return value;
        }

        public byte GetUnsignedInt8(int tag)
        {
            if ((0x1F & _values[_pointer++]) != 0x04)
            {
                throw new Exception("Expected Unsigned Integer, 1-octet value not found");
            }

            if (_values[_pointer++] != (byte)tag)
            {
                throw new Exception("Expected tag number not found");
            }

            byte value = _values[_pointer++];

            return value;
        }

        public ushort GetUnsignedInt16(int tag)
        {
            if ((0x1F & _values[_pointer++]) != 0x05)
            {
                throw new Exception("Expected Unsigned Integer, 2-octet value");
            }

            if (_values[_pointer++] != (byte)tag)
            {
                throw new Exception("Expected tag number not found");
            }

            var value = BitConverter.ToUInt16(_values.ToArray(), _pointer);

            _pointer += 2;

            return value;
        }

        public uint GetUnsignedInt32(int tag)
        {
            if ((0x1F & _values[_pointer++]) != 0x06)
            {
                throw new Exception("Expected Unsigned Integer, 4-octet value");
            }

            if (_values[_pointer++] != (byte)tag)
            {
                throw new Exception("Expected tag number not found");
            }

            var value = BitConverter.ToUInt32(_values.ToArray(), _pointer);

            _pointer += 4;

            return value;
        }

        public ulong GetUnsignedInt64(int tag)
        {
            if ((0x1F & _values[_pointer++]) != 0x07)
            {
                throw new Exception("Expected Unsigned Integer, 8-octet value");
            }

            if (_values[_pointer++] != (byte)tag)
            {
                throw new Exception("Expected tag number not found");
            }

            var value = BitConverter.ToUInt64(_values.ToArray(), _pointer);

            _pointer += 8;

            return value;
        }

        public void CloseContainer()
        {
            if (_values[_pointer++] != 0x18) // End Container
            {
                throw new Exception("Expected EndContainer not found");
            }
        }

        public byte[] GetBytes()
        {
            return _values.ToArray();
        }
    }
}
