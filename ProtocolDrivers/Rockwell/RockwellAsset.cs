namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using libplctag;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public class RockwellAsset : IAsset
    {
        private string _endpoint = string.Empty;

        /// <summary>
        /// Maps tag address (href) to its EIP structure definition.
        /// Populated by the protocol driver during tag creation.
        /// </summary>
        private readonly Dictionary<string, EIPStructureDefinition> _udtDefinitions = new();

        public bool IsConnected { get; private set; } = false;

        /// <summary>
        /// Registers an EIP structure definition for a UDT tag address
        /// so that Read() can decode the raw PLC bytes field-by-field.
        /// </summary>
        public void RegisterUdtDefinition(string tagAddress, EIPStructureDefinition structDef)
        {
            _udtDefinitions[tagAddress] = structDef;
        }

        public void Connect(string ipAddress, int port)
        {
            try
            {
                _endpoint = ipAddress;

                Tag tags = new()
                {
                    Gateway = ipAddress,
                    Path = "1,0",
                    PlcType = PlcType.ControlLogix,
                    Protocol = Protocol.ab_eip,
                    Name = "@tags",
                    Timeout = TimeSpan.FromSeconds(10),
                };

                tags.Read();

                Log.Logger.Information("Connected to Rockwell ControlLogix PLC at " + ipAddress);
                IsConnected = true;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public void Disconnect()
        {
            IsConnected = false;
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public object Read(AssetTag tag)
        {
            object value = null;

            // UDT: read the entire tag as a raw byte buffer, then decode
            // each field at its EIP offset into a dictionary.
            if (tag.Type == "UDT")
            {
                byte[] rawBuffer = ReadRawTag(tag.Address);
                if ((rawBuffer != null)
                    && _udtDefinitions.TryGetValue(tag.Address, out var structDef)
                    && (structDef.Fields != null))
                {
                    return DecodeUdtFields(rawBuffer, structDef);
                }

                return rawBuffer;
            }

            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if (addressParts.Length == 2)
            {
                if (!addressParts[0].StartsWith("Cxn:Standard:"))
                {
                    byte[] tagBytes = Read(addressParts[0], byte.Parse(addressParts[1]), tag.Type, 0).GetAwaiter().GetResult();

                    if ((tagBytes != null) && (tagBytes.Length > 0))
                    {
                        if (tag.Type == "BOOL")
                        {
                            value = BitConverter.ToBoolean(tagBytes);
                        }
                        else if (tag.Type == "SINT")
                        {
                            value = BitConverter.ToChar(tagBytes);
                        }
                        else if (tag.Type == "INT")
                        {
                            value = BitConverter.ToInt16(tagBytes);
                        }
                        else if (tag.Type == "DINT")
                        {
                            value = BitConverter.ToInt32(tagBytes);
                        }
                        else if (tag.Type == "LINT")
                        {
                            value = BitConverter.ToInt64(tagBytes);
                        }
                        else if (tag.Type == "USINT")
                        {
                            value = BitConverter.ToChar(tagBytes);
                        }
                        else if (tag.Type == "UINT")
                        {
                            value = BitConverter.ToUInt16(tagBytes);
                        }
                        else if (tag.Type == "UDINT")
                        {
                            value = BitConverter.ToInt32(tagBytes);
                        }
                        else if (tag.Type == "ULINT")
                        {
                            value = BitConverter.ToUInt64(tagBytes);
                        }
                        else if (tag.Type == "REAL")
                        {
                            value = BitConverter.ToSingle(tagBytes);
                        }
                        else if (tag.Type == "LREAL")
                        {
                            value = BitConverter.ToDouble(tagBytes);
                        }
                        else
                        {
                            throw new ArgumentException("Type not supported by Ethernet/IP.");
                        }
                    }
                }
            }

            return value;
        }

        public void Write(AssetTag tag, object value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            // UDT: Value is the OPC UA binary-encoded ExtensionObject.
            // Decode each field per the StructureDefinition field order, then write
            // to the PLC tag at the corresponding EIP offsets.
            if (tag.Type == "UDT")
            {
                if (!_udtDefinitions.TryGetValue(tag.Address, out var structDef))
                {
                    throw new ArgumentException($"No UDT definition registered for tag '{tag.Address}'.");
                }

                byte[] body = ((ExtensionObject)value).Body as byte[];

                WriteUdtFromBinaryBody(tag.Address, body, structDef);

                return;
            }

            byte[] tagBytes = null;
            if (tag.Type == "BOOL")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value.ToString()));
            }
            else if (tag.Type == "SINT")
            {
                tagBytes = BitConverter.GetBytes(char.Parse(value.ToString()));
            }
            else if (tag.Type == "INT")
            {
                tagBytes = BitConverter.GetBytes(short.Parse(value.ToString()));
            }
            else if (tag.Type == "DINT")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value.ToString()));
            }
            else if (tag.Type == "LINT")
            {
                tagBytes = BitConverter.GetBytes(Int64.Parse(value.ToString()));
            }
            else if (tag.Type == "USINT")
            {
                tagBytes = BitConverter.GetBytes(char.Parse(value.ToString()));
            }
            else if (tag.Type == "UINT")
            {
                tagBytes = BitConverter.GetBytes(ushort.Parse(value.ToString()));
            }
            else if (tag.Type == "UDINT")
            {
                tagBytes = BitConverter.GetBytes(uint.Parse(value.ToString()));
            }
            else if (tag.Type == "ULINT")
            {
                tagBytes = BitConverter.GetBytes(UInt64.Parse(value.ToString()));
            }
            else if (tag.Type == "REAL")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value.ToString()));
            }
            else if (tag.Type == "LREAL")
            {
                tagBytes = BitConverter.GetBytes(double.Parse(value.ToString()));
            }
            else
            {
                throw new ArgumentException("Type not supported by Rockwell.");
            }

            Write(addressParts[0], byte.Parse(addressParts[1]), tag.Type, tagBytes, false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Reads an entire tag (including UDTs) as a raw byte buffer.
        /// libplctag reads the full UDT structure in a single EIP request.
        /// </summary>
        private byte[] ReadRawTag(string tagName)
        {
            var tag = new Tag()
            {
                Name = tagName,
                Gateway = _endpoint,
                Path = "1,0",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip
            };

            tag.Read();

            int size = tag.GetSize();
            byte[] buffer = new byte[size];
            for (int i = 0; i < size; i++)
            {
                buffer[i] = tag.GetUInt8(i);
            }

            return buffer;
        }

        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            var addressParts = addressWithinAsset.Split('.');

            var tag = new Tag()
            {
                Name = addressParts[0],
                Gateway = _endpoint,
                Path = "1,0",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip
            };

            tag.Read();

            int offset = unitID;

            switch (function)
            {
                case "BOOL": return Task.FromResult(BitConverter.GetBytes(tag.GetBit(offset)));
                case "SINT": return Task.FromResult(new byte[] { (byte)tag.GetInt8(offset) });
                case "INT": return Task.FromResult(BitConverter.GetBytes(tag.GetInt16(offset)));
                case "DINT": return Task.FromResult(BitConverter.GetBytes(tag.GetInt32(offset)));
                case "LINT": return Task.FromResult(BitConverter.GetBytes(tag.GetInt64(offset)));
                case "USINT": return Task.FromResult(new byte[] { tag.GetUInt8(offset) });
                case "UINT": return Task.FromResult(BitConverter.GetBytes(tag.GetUInt16(offset)));
                case "UDINT": return Task.FromResult(BitConverter.GetBytes(tag.GetUInt32(offset)));
                case "ULINT": return Task.FromResult(BitConverter.GetBytes(tag.GetUInt64(offset)));
                case "REAL": return Task.FromResult(BitConverter.GetBytes(tag.GetFloat32(offset)));
                case "LREAL": return Task.FromResult(BitConverter.GetBytes(tag.GetFloat64(offset)));
                default: return Task.FromResult((byte[])null);
            }
        }

        private Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            var tag = new Tag()
            {
                Name = addressWithinAsset,
                Gateway = _endpoint,
                Path = "1,0",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip
            };

            tag.Read();

            int offset = unitID;

            switch (function)
            {
                case "BOOL": tag.SetBit(offset, BitConverter.ToBoolean(values)); break;
                case "SINT": tag.SetInt8(offset, (sbyte)BitConverter.ToChar(values)); break;
                case "INT": tag.SetInt16(offset, BitConverter.ToInt16(values)); break;
                case "DINT": tag.SetInt32(offset, BitConverter.ToInt32(values)); break;
                case "LINT": tag.SetInt64(offset, BitConverter.ToInt64(values)); break;
                case "USINT": tag.SetUInt8(offset, values[0]); break;
                case "UINT": tag.SetUInt16(offset, BitConverter.ToUInt16(values)); break;
                case "UDINT": tag.SetUInt32(offset, BitConverter.ToUInt32(values)); break;
                case "ULINT": tag.SetUInt64(offset, BitConverter.ToUInt64(values)); break;
                case "REAL": tag.SetFloat32(offset, BitConverter.ToSingle(values)); break;
                case "LREAL": tag.SetFloat64(offset, BitConverter.ToDouble(values)); break;
                default: break;
            }

            tag.Write();

            return Task.CompletedTask;
        }

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            return null;
        }

        /// <summary>
        /// Decodes a raw PLC UDT byte buffer into a dictionary of field name → value
        /// using the EIP field offsets from the structure definition.
        /// Nested UDTs are decoded recursively.
        /// </summary>
        private static Dictionary<string, object> DecodeUdtFields(byte[] buffer, EIPStructureDefinition structDef)
        {
            var result = new Dictionary<string, object>();

            foreach (EIPFieldDefinition field in structDef.Fields)
            {
                if (field.StructureDefinition != null)
                {
                    // Nested UDT: decode recursively from the same buffer
                    // starting at the nested field's offset
                    result[field.Name] = DecodeUdtFields(buffer, field.StructureDefinition);
                }
                else
                {
                    result[field.Name] = ExtractFieldValue(buffer, field);
                }
            }

            return result;
        }

        /// <summary>
        /// Extracts a single primitive field value from the raw PLC buffer
        /// at the EIP byte offset.
        /// </summary>
        private static object ExtractFieldValue(byte[] buffer, EIPFieldDefinition field)
        {
            int offset = field.Offset;

            try
            {
                return field.Type switch
                {
                    "xsd:BOOL"  => offset < buffer.Length && BitConverter.ToBoolean(buffer, offset),
                    "xsd:SINT"  => offset < buffer.Length ? (sbyte)buffer[offset] : (sbyte)0,
                    "xsd:USINT" => offset < buffer.Length ? buffer[offset] : (byte)0,
                    "xsd:INT"   => offset + 1 < buffer.Length ? BitConverter.ToInt16(buffer, offset) : (short)0,
                    "xsd:UINT"  => offset + 1 < buffer.Length ? BitConverter.ToUInt16(buffer, offset) : (ushort)0,
                    "xsd:DINT"  => offset + 3 < buffer.Length ? BitConverter.ToInt32(buffer, offset) : 0,
                    "xsd:UDINT" => offset + 3 < buffer.Length ? BitConverter.ToUInt32(buffer, offset) : 0u,
                    "xsd:LINT"  => offset + 7 < buffer.Length ? BitConverter.ToInt64(buffer, offset) : 0L,
                    "xsd:ULINT" => offset + 7 < buffer.Length ? BitConverter.ToUInt64(buffer, offset) : 0UL,
                    "xsd:REAL"  => offset + 3 < buffer.Length ? BitConverter.ToSingle(buffer, offset) : 0f,
                    "xsd:LREAL" => offset + 7 < buffer.Length ? BitConverter.ToDouble(buffer, offset) : 0d,
                    "xsd:STRING" => ExtractPlcString(buffer, offset),
                    _           => null
                };
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts a Rockwell STRING from the raw PLC buffer.
        /// Rockwell STRINGs are stored as a 4-byte little-endian length
        /// prefix followed by the ASCII character data.
        /// </summary>
        private static string ExtractPlcString(byte[] buffer, int offset)
        {
            if ((offset + 4) > buffer.Length)
            {
                return string.Empty;
            }

            int length = BitConverter.ToInt32(buffer, offset);
            if (length <= 0 || ((offset + 4 + length) > buffer.Length))
            {
                return string.Empty;
            }

            return System.Text.Encoding.ASCII.GetString(buffer, offset + 4, length);
        }

        /// <summary>
        /// Writes a UDT tag from an OPC UA binary-encoded ExtensionObject body.
        /// The body contains fields encoded sequentially in StructureDefinition field order.
        /// Each field is decoded from the body using OPC UA binary encoding sizes, then
        /// written into the PLC tag buffer at the corresponding EIP byte offset.
        /// </summary>
        private void WriteUdtFromBinaryBody(string tagAddress, byte[] body, EIPStructureDefinition structDef)
        {
            var plcTag = new Tag()
            {
                Name = tagAddress,
                Gateway = _endpoint,
                Path = "1,0",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip
            };

            // Read current tag to initialise the buffer (preserves fields not in the write)
            plcTag.Read();

            int bodyOffset = 0;
            WriteFieldsFromBody(plcTag, body, ref bodyOffset, structDef);

            plcTag.Write();
        }

        /// <summary>
        /// Recursively decodes OPC UA binary-encoded fields from <paramref name="body"/>
        /// and writes each value into the PLC tag at its EIP offset.
        ///
        /// OPC UA binary encoding order matches the StructureDefinition field order.
        /// Field sizes follow OPC UA Part 6 binary encoding:
        ///   Boolean/SByte/Byte = 1, Int16/UInt16 = 2, Int32/UInt32/Float = 4,
        ///   Int64/UInt64/Double = 8, String = 4-byte length prefix + UTF-8 bytes.
        /// </summary>
        private static void WriteFieldsFromBody(
            Tag plcTag,
            byte[] body,
            ref int bodyOffset,
            EIPStructureDefinition structDef)
        {
            if (structDef.Fields == null) return;

            foreach (EIPFieldDefinition field in structDef.Fields)
            {
                if (field.StructureDefinition != null)
                {
                    // Nested UDT — recurse; fields are encoded inline (no length prefix)
                    WriteFieldsFromBody(plcTag, body, ref bodyOffset, field.StructureDefinition);
                    continue;
                }

                int eipOffset = field.Offset;

                switch (field.Type)
                {
                    case "xsd:BOOL":
                        plcTag.SetUInt8(eipOffset, body[bodyOffset]);
                        bodyOffset += 1;
                        break;
                    case "xsd:SINT":
                        plcTag.SetInt8(eipOffset, (sbyte)body[bodyOffset]);
                        bodyOffset += 1;
                        break;
                    case "xsd:USINT":
                        plcTag.SetUInt8(eipOffset, body[bodyOffset]);
                        bodyOffset += 1;
                        break;
                    case "xsd:INT":
                        plcTag.SetInt16(eipOffset, BitConverter.ToInt16(body, bodyOffset));
                        bodyOffset += 2;
                        break;
                    case "xsd:UINT":
                        plcTag.SetUInt16(eipOffset, BitConverter.ToUInt16(body, bodyOffset));
                        bodyOffset += 2;
                        break;
                    case "xsd:DINT":
                        plcTag.SetInt32(eipOffset, BitConverter.ToInt32(body, bodyOffset));
                        bodyOffset += 4;
                        break;
                    case "xsd:UDINT":
                        plcTag.SetUInt32(eipOffset, BitConverter.ToUInt32(body, bodyOffset));
                        bodyOffset += 4;
                        break;
                    case "xsd:LINT":
                        plcTag.SetInt64(eipOffset, BitConverter.ToInt64(body, bodyOffset));
                        bodyOffset += 8;
                        break;
                    case "xsd:ULINT":
                        plcTag.SetUInt64(eipOffset, BitConverter.ToUInt64(body, bodyOffset));
                        bodyOffset += 8;
                        break;
                    case "xsd:REAL":
                        plcTag.SetFloat32(eipOffset, BitConverter.ToSingle(body, bodyOffset));
                        bodyOffset += 4;
                        break;
                    case "xsd:LREAL":
                        plcTag.SetFloat64(eipOffset, BitConverter.ToDouble(body, bodyOffset));
                        bodyOffset += 8;
                        break;
                    case "xsd:STRING":
                        // OPC UA binary: 4-byte length prefix (Int32) + UTF-8 bytes
                        int strLen = BitConverter.ToInt32(body, bodyOffset);
                        bodyOffset += 4;
                        // Rockwell PLC string: 4-byte length prefix (DINT) + char data
                        plcTag.SetInt32(eipOffset, strLen);
                        for (int i = 0; i < strLen; i++)
                        {
                            plcTag.SetUInt8(eipOffset + 4 + i, body[bodyOffset + i]);
                        }
                        bodyOffset += strLen;
                        break;
                    default:
                        Log.Logger.Warning($"Unsupported UDT field type '{field.Type}' for field '{field.Name}' during write.");
                        break;
                }
            }
        }
    }
}
