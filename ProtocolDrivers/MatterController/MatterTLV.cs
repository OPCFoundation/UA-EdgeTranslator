using System;
using System.Collections.Generic;
using System.Text;

namespace Matter.Core.TLV
{
    /// <summary>
    /// See Appendix A of the Matter Specification for the TLV encoding.
    /// </summary>
    public class MatterTLV
    {
        private List<byte> _values = new();

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
            //
            _values.Add(0x01 << 5 | 0x16);
            _values.Add(tagNumber);
            return this;
        }

        public MatterTLV AddArray()
        {
            // This is an anonymous tag, shifted 5 bits and then OR'd with 0x22
            // 00010110
            //
            _values.Add(0x16);
            return this;
        }

        public MatterTLV AddList(long tagNumber)
        {
            // This is a Context-Specific Tag (0x01), shifted 5 bits and then OR'd with 0x17
            // to produce a context tag for List, one byte long
            // 00110111
            //
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
            else //if (stringLength < uint.MaxValue)
            {
                _values.Add(0x01 << 5 | 0x0E); // UTFString, 4-octet length
                _values.Add(tagNumber);
                _values.AddRange(BitConverter.GetBytes((uint)stringLength));
                _values.AddRange(utf8String);
            }
            //else
            //{
            //    _values.Add(0x01 << 5 | 0x0F); // UTFString, 8-octet length
            //    _values.Add(tagNumber);
            //    _values.AddRange(BitConverter.GetBytes((ulong)stringLength));
            //    _values.AddRange(utf8String);
            //}

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
                //
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
                //
                _values.Add(0x01 << 5 | 0x11); // Octet String, 2-octet length
                _values.Add(tagNumber);
                _values.AddRange(BitConverter.GetBytes((ushort)value.Length));
                _values.AddRange(value);
            }
            else //if (valueLength < uint.MaxValue)
            {
                // This is a context type 1, shifted 5 bits and then OR'd with 12
                // to produce a context tag for Octet String, 4 bytes
                // 00110010
                //
                _values.Add(0x01 << 5 | 0x12); // Octet String, 4-octet length
                _values.Add(tagNumber);
                _values.AddRange(BitConverter.GetBytes((uint)value.Length));
                _values.AddRange(value);
            }
            //else
            //{
            //    _values.Add(0x01 << 5 | 0x13); // Octet String, 4-octet length
            //    _values.Add(tagNumber);
            //    _values.AddRange(BitConverter.GetBytes((ulong)value.Length));
            //    _values.AddRange(value);
            //}

            return this;
        }

        public MatterTLV AddUInt8(byte tagNumber, byte value)
        {
            _values.Add(0x01 << 5 | 0x4);
            _values.Add(tagNumber);

            // No length required
            //
            _values.Add(value);

            return this;
        }

        public MatterTLV AddUInt8(byte value)
        {
            _values.Add(0x4);
            _values.Add(value);

            return this;
        }

        public MatterTLV AddUInt16(byte tagNumber, ushort value)
        {
            _values.Add(0x01 << 5 | 0x5);
            _values.Add(tagNumber);

            // No length required.
            //
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddUInt32(byte tagNumber, uint value)
        {
            _values.Add(0x01 << 5 | 0x6);
            _values.Add(tagNumber);

            // No length required.
            //
            _values.AddRange(BitConverter.GetBytes(value));

            return this;
        }

        public MatterTLV AddUInt64(byte tagNumber, ulong value)
        {
            _values.Add(0x01 << 5 | 0x7);
            _values.Add(tagNumber);

            // No length required.
            //
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

        internal void Serialize(MatterMessageWriter writer)
        {
            var bytes = _values.ToArray();
            writer.Write(bytes);
        }

        private int _pointer = 0;

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
                //Octet String, 2 - octet length
                length = 8;
            }
            else if ((0x1F & _values[_pointer]) == 0x12)
            {
                //Octet String, 2 - octet length
                length = 4;
            }
            else if ((0x1F & _values[_pointer]) == 0x11)
            {
                //Octet String, 2 - octet length
                length = 2;
            }
            else if ((0x1F & _values[_pointer]) == 0x10) // Context Octet String, 1 - octet length
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

        public byte[] GetUTF8String(int tag)
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

            return bytes;
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

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("\n");

            var indentation = 0;

            var indent = (StringBuilder sb) =>
            {
                for (int x = 0; x < indentation; x++)
                {
                    sb.Append(" ");
                }
            };

            var renderTag = (byte[] bytes, int index) =>
            {
                int tagControl = bytes[index] >> 5;
                int elementType = bytes[index] >> 0 & 0x1F;

                int length = 1; // We have read the tagControl/elementType byte

                sb.Append("|");
                indent(sb);

                try
                {
                    if (elementType == 0x15) // Structure
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        sb.AppendLine("Structure {");
                        indentation += 2;
                    }

                    else if (elementType == 0x16)
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        sb.AppendLine("Array [");
                        indentation += 2;
                    }

                    else if (elementType == 0x17)
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        sb.AppendLine("List [");
                        indentation += 2;
                    }

                    else if (elementType == 0x07) // Unsigned Integer 64bit
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        var value = BitConverter.ToUInt64(bytes, index + length);

                        sb.AppendLine($"Unsigned Int (64bit) ({value}|0x{value:X2})");

                        length += 8;
                    }

                    else if (elementType == 0x06) // Unsigned Integer 32bit
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        var value = BitConverter.ToUInt32(bytes, index + length);

                        sb.AppendLine($"Unsigned Int (32bit) ({value}|0x{value:X2})");

                        length += 4;
                    }

                    else if (elementType == 0x05) // Unsigned Integer 16bit
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        var value = BitConverter.ToUInt16(bytes, index + length);

                        sb.AppendLine($"Unsigned Int (16bit) ({value}|0x{value:X2})");

                        length += 2;
                    }

                    else if (elementType == 0x04) // Unsigned Integer 8bit
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        var value = bytes[index + length];

                        sb.AppendLine($"Unsigned Int (8bit) ({value}|0x{value:X2})");

                        length += 1;
                    }

                    else if (elementType == 0x00) // Signed Integer 8bit
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        var value = bytes[index + length];

                        sb.AppendLine($"Signed Int (8bit) ({value}|0x{value:X2})");

                        length += 1;
                    }

                    else if (elementType == 0x01) // Signed Integer 16bit
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        var value = BitConverter.ToInt16(bytes, index + length);

                        sb.AppendLine($"Signed Int (16bit) ({value}|0x{value:X2})");

                        length += 2;
                    }

                    else if (elementType == 0x0C) // UTF-8 String, 1-octet length
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        // One octet length
                        var stringLength = bytes[index + length];

                        length++;

                        var value = Encoding.UTF8.GetString(bytes.AsSpan().Slice(index + length, stringLength));

                        sb.AppendLine($"UTF-8 String, 1-octet length ({stringLength}) ({value})");

                        length += stringLength;
                    }

                    else if (elementType == 0x0D) // UTF-8 String, 2-octet length
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        // Two octet length
                        var stringLength = BitConverter.ToUInt16(bytes, index + length);

                        length += 2;

                        var value = Encoding.UTF8.GetString(bytes.AsSpan().Slice(index + length, stringLength));

                        sb.AppendLine($"UTF-8 String, 1-octet length ({stringLength}) ({value})");

                        length += stringLength;
                    }

                    else if (elementType == 0x0E) // UTF-8 String, 4-octet length
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");

                            length++;
                        }
                        else if (tagControl == 0x02) // Common Profile Tag Form, 2 octets
                        {
                            var tag = BitConverter.ToInt16(bytes, index + length);
                            sb.Append($"{tag} => ");

                            length += 2;
                        }
                        else if (tagControl == 0x03) // Common Profile Tag Form, 4 octets
                        {
                            var tag = BitConverter.ToInt32(bytes, index + length);
                            sb.Append($"{tag} => ");

                            length += 4;
                        }

                        // Four octet length
                        var stringLength = BitConverter.ToUInt32(bytes, index + length);

                        length += 4;

                        var value = Encoding.UTF8.GetString(bytes.AsSpan().Slice(index + length, (int)stringLength));

                        sb.AppendLine($"UTF-8 String, 4-octet length ({value})");

                        length += (int)stringLength;
                    }

                    else if (elementType == 0x10) // Octet String, 1-octet length
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        // One octet length
                        var stringLength = bytes[index + length];

                        length++;

                        var value = bytes.AsSpan().Slice(index + length, stringLength).ToArray();

                        sb.AppendLine($"Octet String, 1-octet length ({stringLength}) ({BitConverter.ToString(value)})");

                        length += stringLength;
                    }

                    else if (elementType == 0x11) // Octet String, 2-octet length
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        // Two octet length
                        var stringLength = BitConverter.ToUInt16(bytes, index + length);

                        length += 2;

                        var value = bytes.AsSpan().Slice(index + length, stringLength).ToArray();

                        sb.AppendLine($"Octet String, 2-octet length ({BitConverter.ToString(value)})");

                        length += stringLength;
                    }

                    else if (elementType == 0x08 || elementType == 0x09) // Boolean
                    {
                        if (tagControl == 0x01) // Context {
                        {
                            sb.Append($"{bytes[index + 1].ToString()} => ");
                            length++;
                        }

                        sb.AppendLine($"Boolean ({elementType == 0x09})");
                    }

                    else if (elementType == 0x18)
                    {
                        indentation -= 2;
                        sb.AppendLine("}"); // Should be a ] if we opened with an array or list.
                    }

                    else
                    {
                        sb.AppendLine($"Unhandled Tag ({tagControl:X2}|{elementType:X2})");
                    }
                }
                catch
                {

                }

                return length;
            };

            // Move through each
            //
            var bytes = GetBytes();

            sb.AppendLine("TLV Payload");
            sb.AppendLine();
            sb.AppendLine(BitConverter.ToString(bytes).Replace("-", ""));
            sb.AppendLine();

            var i = 0;

            while (i < bytes.Length)
            {
                i += renderTag(bytes, i);
            }

            return sb.ToString();
        }

        public object GetData(int? tag)
        {
            int tagControl = _values[_pointer] >> 5;
            int elementType = _values[_pointer] >> 0 & 0x1F;

            switch (elementType)
            {
                case 0x00:
                case 0x01:
                case 0x02:
                case 0x03:
                    return GetSignedInt(tag);

                case 0x04: //
                case 0x05:
                case 0x06:
                case 0x07:
                    return GetUnsignedInt(tag);
                case 0x15: // Structure

                    List<object> structure = [];

                    OpenStructure(tag);

                    while (!IsEndContainerNext())
                    {
                        structure.Add(GetData(null));
                    }

                    CloseContainer();

                    return structure;

                case 0x16:

                    List<object> array = [];

                    OpenArray(tag);

                    while (!IsEndContainerNext())
                    {
                        array.Add(GetData(null));
                    }

                    CloseContainer();

                    return array;
                case 0x17:

                    List<object> list = [new List<object>()];

                    OpenList(tag);

                    while (!IsEndContainerNext())
                    {
                        list.Add(GetData(null));
                    }

                    CloseContainer();

                    return list;

                default:
                    throw new Exception($"Cannot process elementType {elementType:X2}");
            }
        }
    }
}
