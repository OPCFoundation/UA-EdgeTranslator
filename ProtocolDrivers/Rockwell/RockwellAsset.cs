namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using libplctag;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A Logix controller reached over EtherNet/IP, read and written as the W3C WoT
    /// EtherNet/IP binding describes: symbolic tag paths sent as written, the CIP route
    /// path of the endpoint, Logix primitive types, raw byte offsets, linear value
    /// conversion and UDT structure definitions whose nested offsets are relative.
    /// </summary>
    public class RockwellAsset : IAsset
    {
        private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(10);

        private string _gateway = string.Empty;

        private string _routePath = EipEndpoint.DefaultRoutePath;

        /// <summary>
        /// The form of each tag href, registered by the protocol driver when the tag is
        /// created: its type, conversion, timeout and structure definition.
        /// </summary>
        private readonly Dictionary<string, EIPForm> _forms = new();

        public bool IsConnected { get; private set; } = false;

        /// <summary>The CIP route path from the communication module to the controller.</summary>
        public string RoutePath => _routePath;

        /// <summary>Registers the form of a tag so reads and writes can encode its value.</summary>
        public void RegisterForm(string href, EIPForm form)
        {
            if (!string.IsNullOrEmpty(href) && form != null)
            {
                _forms[href] = form;
            }
        }

        /// <summary>Connects to the controller an <c>eip</c> endpoint names, through its route path.</summary>
        public Task ConnectAsync(EipEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            _routePath = endpoint.RoutePath;
            ConnectCore(endpoint.Gateway);
            return Task.CompletedTask;
        }

        public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
        {
            // An eip endpoint carries its route path; a bare host uses the default route.
            if (EipEndpoint.TryParse(ipAddress, out EipEndpoint endpoint))
            {
                return ConnectAsync(endpoint, cancellationToken);
            }

            _routePath = EipEndpoint.DefaultRoutePath;
            ConnectCore(port > 0 && port != EipEndpoint.DefaultPort ? ipAddress + ":" + port : ipAddress);
            return Task.CompletedTask;
        }

        private void ConnectCore(string gateway)
        {
            try
            {
                _gateway = gateway;

                Tag tags = NewTag("@tags", null);
                tags.Read();

                Log.Logger.Information("Connected to Rockwell ControlLogix PLC at " + gateway + " (route " + _routePath + ")");
                IsConnected = true;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        private Tag NewTag(string name, EIPForm form)
        {
            Tag tag = new Tag()
            {
                Name = name,
                Gateway = _gateway,
                Path = _routePath,
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Timeout = form?.Timeout is > 0 ? TimeSpan.FromMilliseconds(form.Timeout.Value) : s_defaultTimeout
            };

            // An array form transfers several elements in one CIP Read/Write Tag request.
            if (form != null && form.IsArray)
            {
                tag.ElementCount = form.EffectiveElementCount;
            }

            return tag;
        }

        private void DisconnectCore()
        {
            IsConnected = false;
        }

        public string GetRemoteEndpoint()
        {
            return _gateway;
        }

        private EIPForm FormOf(AssetTag tag) => _forms.TryGetValue(tag.Address, out EIPForm form) ? form : null;

        private object ReadCore(AssetTag tag)
        {
            EIPForm form = FormOf(tag);
            EipTagReference reference = EipTagReference.Parse(tag.Address);

            // Connection entries hold no data (see RockwellProtocolDriver.IsTag).
            if (reference.TagPath.StartsWith("Cxn:", StringComparison.Ordinal))
            {
                return null;
            }

            Tag plcTag = NewTag(reference.TagPath, form);
            plcTag.Read();

            // A structured tag is decoded field by field into name -> value.
            if (form?.StructureDefinition?.Fields != null)
            {
                return DecodeUdtFields(ReadBuffer(plcTag), reference.Offset, form.StructureDefinition);
            }

            // An array form returns its elements as a typed array.
            if (form != null && form.IsArray && form.Type is EIPTypeString elementType)
            {
                return DecodeArray(ReadBuffer(plcTag), reference.Offset, elementType, form.EffectiveElementCount, form);
            }

            // A structured tag named without a definition whose layout could not be read from
            // the controller: return its raw bytes rather than guess a decoding.
            if (form?.Type is not EIPTypeString type)
            {
                return ReadBuffer(plcTag);
            }

            int size = plcTag.GetSize();
            if (reference.Offset + ByteSize(type) > size)
            {
                throw new ArgumentOutOfRangeException(nameof(tag), $"Offset {reference.Offset} exceeds the {size}-byte tag {reference.TagPath}.");
            }

            return form.ToApplicationValue(GetValue(plcTag, reference.Offset, type));
        }

        private static byte[] ReadBuffer(Tag plcTag)
        {
            int size = plcTag.GetSize();
            byte[] buffer = new byte[size];
            for (int i = 0; i < size; i++)
            {
                buffer[i] = plcTag.GetUInt8(i);
            }

            return buffer;
        }

        private static object GetValue(Tag plcTag, int offset, EIPTypeString type)
        {
            return type switch
            {
                // A scalar BOOL travels in one byte whose least significant bit is the value.
                EIPTypeString.BOOL => (plcTag.GetUInt8(offset) & 1) != 0,
                EIPTypeString.SINT => plcTag.GetInt8(offset),
                EIPTypeString.INT => plcTag.GetInt16(offset),
                EIPTypeString.DINT => plcTag.GetInt32(offset),
                EIPTypeString.LINT => plcTag.GetInt64(offset),
                EIPTypeString.USINT or EIPTypeString.BYTE => plcTag.GetUInt8(offset),
                EIPTypeString.UINT or EIPTypeString.WORD => plcTag.GetUInt16(offset),
                EIPTypeString.UDINT or EIPTypeString.DWORD => plcTag.GetUInt32(offset),
                EIPTypeString.ULINT or EIPTypeString.LWORD => plcTag.GetUInt64(offset),
                EIPTypeString.REAL => plcTag.GetFloat32(offset),
                EIPTypeString.LREAL => plcTag.GetFloat64(offset),
                EIPTypeString.STRING => ExtractPlcString(ReadBuffer(plcTag), offset),
                _ => throw new NotSupportedException($"EtherNet/IP type {type} is not supported.")
            };
        }

        private static void SetValue(Tag plcTag, int offset, EIPTypeString type, object value)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            switch (type)
            {
                case EIPTypeString.BOOL: plcTag.SetUInt8(offset, (byte)(Convert.ToBoolean(value, c) ? 1 : 0)); break;
                case EIPTypeString.SINT: plcTag.SetInt8(offset, Convert.ToSByte(value, c)); break;
                case EIPTypeString.INT: plcTag.SetInt16(offset, Convert.ToInt16(value, c)); break;
                case EIPTypeString.DINT: plcTag.SetInt32(offset, Convert.ToInt32(value, c)); break;
                case EIPTypeString.LINT: plcTag.SetInt64(offset, Convert.ToInt64(value, c)); break;
                case EIPTypeString.USINT or EIPTypeString.BYTE: plcTag.SetUInt8(offset, Convert.ToByte(value, c)); break;
                case EIPTypeString.UINT or EIPTypeString.WORD: plcTag.SetUInt16(offset, Convert.ToUInt16(value, c)); break;
                case EIPTypeString.UDINT or EIPTypeString.DWORD: plcTag.SetUInt32(offset, Convert.ToUInt32(value, c)); break;
                case EIPTypeString.ULINT or EIPTypeString.LWORD: plcTag.SetUInt64(offset, Convert.ToUInt64(value, c)); break;
                case EIPTypeString.REAL: plcTag.SetFloat32(offset, Convert.ToSingle(value, c)); break;
                case EIPTypeString.LREAL: plcTag.SetFloat64(offset, Convert.ToDouble(value, c)); break;
                case EIPTypeString.STRING: SetPlcString(plcTag, offset, Convert.ToString(value, c)); break;
                default: throw new NotSupportedException($"EtherNet/IP type {type} is not supported.");
            }
        }

        private static int ByteSize(EIPTypeString type)
        {
            return type switch
            {
                EIPTypeString.BOOL or EIPTypeString.SINT or EIPTypeString.USINT or EIPTypeString.BYTE => 1,
                EIPTypeString.INT or EIPTypeString.UINT or EIPTypeString.WORD => 2,
                EIPTypeString.DINT or EIPTypeString.UDINT or EIPTypeString.DWORD or EIPTypeString.REAL => 4,
                EIPTypeString.LINT or EIPTypeString.ULINT or EIPTypeString.LWORD or EIPTypeString.LREAL => 8,
                EIPTypeString.STRING => 4,
                _ => 1
            };
        }

        private void WriteCore(AssetTag tag, object value)
        {
            EIPForm form = FormOf(tag);
            EipTagReference reference = EipTagReference.Parse(tag.Address);

            // Read first: a write must preserve the bytes no field covers, such as padding.
            Tag plcTag = NewTag(reference.TagPath, form);
            plcTag.Read();

            if (form?.StructureDefinition?.Fields != null)
            {
                // The value is the OPC UA binary body of the structure, fields in definition order.
                ExtensionObject extensionObject = (ExtensionObject)value;
                byte[] body = extensionObject.TryGetAsBinary(out ByteString encodedBody, ServiceMessageContext.CreateEmpty(Program.Telemetry))
                    && !encodedBody.IsNull
                    ? encodedBody.ToArray()
                    : null;

                int bodyOffset = 0;
                WriteFieldsFromBody(plcTag, body, ref bodyOffset, reference.Offset, form.StructureDefinition);
            }
            else if (form != null && form.IsArray && form.Type is EIPTypeString elementType)
            {
                byte[] buffer = ReadBuffer(plcTag);
                EncodeArray(buffer, reference.Offset, elementType, form.EffectiveElementCount, value as System.Collections.IEnumerable, form);
                for (int i = 0; i < buffer.Length; i++)
                {
                    plcTag.SetUInt8(i, buffer[i]);
                }
            }
            else if (form?.Type is EIPTypeString type)
            {
                SetValue(plcTag, reference.Offset, type, form.ToControllerValue(value));
            }
            else
            {
                throw new NotSupportedException($"Tag {reference.TagPath} has neither an eipv:type nor an eipv:structureDefinition.");
            }

            plcTag.Write();
        }

        private string ExecuteActionCore(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            return null;
        }

        /// <summary>
        /// Decodes a raw UDT buffer into field name -> value. Offsets of a nested
        /// structure are relative to the start of that structure, so each level adds its
        /// own base offset; a BOOL member is a bit of the byte at its byte offset.
        /// </summary>
        public static Dictionary<string, object> DecodeUdtFields(byte[] buffer, int baseOffset, EIPStructureDefinition structDef)
        {
            var result = new Dictionary<string, object>();

            foreach (EIPFieldDefinition field in structDef.Fields)
            {
                int offset = baseOffset + field.ByteOffset;
                result[field.Name] = field.StructureDefinition != null
                    ? DecodeUdtFields(buffer, offset, field.StructureDefinition)
                    : ExtractFieldValue(buffer, offset, field);
            }

            return result;
        }

        /// <summary>
        /// Decodes <paramref name="count"/> elements of <paramref name="type"/> packed back to
        /// back (row-major, no padding) from <paramref name="offset"/>, applying the form's
        /// conversion to each element. A converted array is returned as <c>double[]</c>.
        /// </summary>
        public static Array DecodeArray(byte[] buffer, int offset, EIPTypeString type, int count, EIPForm form = null)
        {
            int size = ByteSize(type);
            if (offset < 0 || count < 0 || offset + (long)count * size > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count), $"{count} elements of {type} at offset {offset} exceed the {buffer.Length}-byte tag.");
            }

            EIPFieldDefinition element = new EIPFieldDefinition { Type = type };
            Array raw = Array.CreateInstance(ClrTypeOf(type), count);
            for (int i = 0; i < count; i++)
            {
                raw.SetValue(ExtractFieldValue(buffer, offset + i * size, element), i);
            }

            if (form == null || !form.HasValueConversion)
            {
                return raw;
            }

            double[] converted = new double[count];
            for (int i = 0; i < count; i++)
            {
                converted[i] = Convert.ToDouble(form.ToApplicationValue(raw.GetValue(i)), CultureInfo.InvariantCulture);
            }

            return converted;
        }

        /// <summary>
        /// Encodes the elements of <paramref name="values"/> back to back from
        /// <paramref name="offset"/>; exactly <paramref name="count"/> elements are required.
        /// </summary>
        public static void EncodeArray(byte[] buffer, int offset, EIPTypeString type, int count, System.Collections.IEnumerable values, EIPForm form = null)
        {
            if (values == null || values is string)
            {
                throw new ArgumentException($"An array of {count} {type} elements is required.", nameof(values));
            }

            var items = new List<object>();
            foreach (object item in values)
            {
                items.Add(item);
            }

            if (items.Count != count)
            {
                throw new ArgumentException($"{items.Count} elements given for an array of {count}.", nameof(values));
            }

            int size = ByteSize(type);
            if (offset < 0 || offset + (long)count * size > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count), $"{count} elements of {type} at offset {offset} exceed the {buffer.Length}-byte tag.");
            }

            CultureInfo c = CultureInfo.InvariantCulture;
            for (int i = 0; i < count; i++)
            {
                object value = form == null ? items[i] : form.ToControllerValue(items[i]);
                byte[] bytes = type switch
                {
                    EIPTypeString.SINT => new[] { unchecked((byte)Convert.ToSByte(value, c)) },
                    EIPTypeString.USINT or EIPTypeString.BYTE => new[] { Convert.ToByte(value, c) },
                    EIPTypeString.INT => BitConverter.GetBytes(Convert.ToInt16(value, c)),
                    EIPTypeString.UINT or EIPTypeString.WORD => BitConverter.GetBytes(Convert.ToUInt16(value, c)),
                    EIPTypeString.DINT => BitConverter.GetBytes(Convert.ToInt32(value, c)),
                    EIPTypeString.UDINT or EIPTypeString.DWORD => BitConverter.GetBytes(Convert.ToUInt32(value, c)),
                    EIPTypeString.LINT => BitConverter.GetBytes(Convert.ToInt64(value, c)),
                    EIPTypeString.ULINT or EIPTypeString.LWORD => BitConverter.GetBytes(Convert.ToUInt64(value, c)),
                    EIPTypeString.REAL => BitConverter.GetBytes(Convert.ToSingle(value, c)),
                    EIPTypeString.LREAL => BitConverter.GetBytes(Convert.ToDouble(value, c)),
                    _ => throw new NotSupportedException($"Arrays of {type} are not supported.")
                };

                Buffer.BlockCopy(bytes, 0, buffer, offset + i * size, bytes.Length);
            }
        }

        /// <summary>The CLR element type a Logix primitive decodes to.</summary>
        private static Type ClrTypeOf(EIPTypeString type)
        {
            return type switch
            {
                EIPTypeString.BOOL => typeof(bool),
                EIPTypeString.SINT => typeof(sbyte),
                EIPTypeString.USINT or EIPTypeString.BYTE => typeof(byte),
                EIPTypeString.INT => typeof(short),
                EIPTypeString.UINT or EIPTypeString.WORD => typeof(ushort),
                EIPTypeString.DINT => typeof(int),
                EIPTypeString.UDINT or EIPTypeString.DWORD => typeof(uint),
                EIPTypeString.LINT => typeof(long),
                EIPTypeString.ULINT or EIPTypeString.LWORD => typeof(ulong),
                EIPTypeString.REAL => typeof(float),
                EIPTypeString.LREAL => typeof(double),
                EIPTypeString.STRING => typeof(string),
                _ => typeof(object)
            };
        }

        private static object ExtractFieldValue(byte[] buffer, int offset, EIPFieldDefinition field)
        {
            if (field.Type is not EIPTypeString type)
            {
                return null;
            }

            if (offset < 0 || offset + ByteSize(type) > buffer.Length)
            {
                return null;
            }

            return type switch
            {
                EIPTypeString.BOOL => (buffer[offset] & (1 << (field.BitOffset ?? 0))) != 0,
                EIPTypeString.SINT => (sbyte)buffer[offset],
                EIPTypeString.USINT or EIPTypeString.BYTE => buffer[offset],
                EIPTypeString.INT => BitConverter.ToInt16(buffer, offset),
                EIPTypeString.UINT or EIPTypeString.WORD => BitConverter.ToUInt16(buffer, offset),
                EIPTypeString.DINT => BitConverter.ToInt32(buffer, offset),
                EIPTypeString.UDINT or EIPTypeString.DWORD => BitConverter.ToUInt32(buffer, offset),
                EIPTypeString.LINT => BitConverter.ToInt64(buffer, offset),
                EIPTypeString.ULINT or EIPTypeString.LWORD => BitConverter.ToUInt64(buffer, offset),
                EIPTypeString.REAL => BitConverter.ToSingle(buffer, offset),
                EIPTypeString.LREAL => BitConverter.ToDouble(buffer, offset),
                EIPTypeString.STRING => ExtractPlcString(buffer, offset),
                _ => null
            };
        }

        /// <summary>
        /// Reads a Logix STRING: a DINT <c>LEN</c> followed by the SINT <c>DATA</c> characters.
        /// </summary>
        public static string ExtractPlcString(byte[] buffer, int offset)
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

        private static void SetPlcString(Tag plcTag, int offset, string value)
        {
            byte[] data = System.Text.Encoding.ASCII.GetBytes(value ?? string.Empty);
            plcTag.SetInt32(offset, data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                plcTag.SetUInt8(offset + 4 + i, data[i]);
            }
        }

        /// <summary>
        /// Writes each field of an OPC UA binary structure body into the controller
        /// buffer at its byte offset (relative to its enclosing structure). Field sizes
        /// follow OPC UA Part 6: Boolean/SByte/Byte 1, Int16/UInt16 2, Int32/UInt32/Float 4,
        /// Int64/UInt64/Double 8, String a 4-byte length plus the bytes.
        /// </summary>
        private static void WriteFieldsFromBody(Tag plcTag, byte[] body, ref int bodyOffset, int baseOffset, EIPStructureDefinition structDef)
        {
            if (structDef.Fields == null || body == null)
            {
                return;
            }

            foreach (EIPFieldDefinition field in structDef.Fields)
            {
                int offset = baseOffset + field.ByteOffset;

                if (field.StructureDefinition != null)
                {
                    // Nested UDT: its fields follow inline in the OPC UA body.
                    WriteFieldsFromBody(plcTag, body, ref bodyOffset, offset, field.StructureDefinition);
                    continue;
                }

                switch (field.Type)
                {
                    case EIPTypeString.BOOL:
                        {
                            // A packed BOOL member owns one bit; keep the bits of its neighbours.
                            int bit = field.BitOffset ?? 0;
                            byte current = plcTag.GetUInt8(offset);
                            byte updated = body[bodyOffset] != 0 ? (byte)(current | (1 << bit)) : (byte)(current & ~(1 << bit));
                            plcTag.SetUInt8(offset, updated);
                            bodyOffset += 1;
                            break;
                        }
                    case EIPTypeString.SINT: plcTag.SetInt8(offset, (sbyte)body[bodyOffset]); bodyOffset += 1; break;
                    case EIPTypeString.USINT or EIPTypeString.BYTE: plcTag.SetUInt8(offset, body[bodyOffset]); bodyOffset += 1; break;
                    case EIPTypeString.INT: plcTag.SetInt16(offset, BitConverter.ToInt16(body, bodyOffset)); bodyOffset += 2; break;
                    case EIPTypeString.UINT or EIPTypeString.WORD: plcTag.SetUInt16(offset, BitConverter.ToUInt16(body, bodyOffset)); bodyOffset += 2; break;
                    case EIPTypeString.DINT: plcTag.SetInt32(offset, BitConverter.ToInt32(body, bodyOffset)); bodyOffset += 4; break;
                    case EIPTypeString.UDINT or EIPTypeString.DWORD: plcTag.SetUInt32(offset, BitConverter.ToUInt32(body, bodyOffset)); bodyOffset += 4; break;
                    case EIPTypeString.LINT: plcTag.SetInt64(offset, BitConverter.ToInt64(body, bodyOffset)); bodyOffset += 8; break;
                    case EIPTypeString.ULINT or EIPTypeString.LWORD: plcTag.SetUInt64(offset, BitConverter.ToUInt64(body, bodyOffset)); bodyOffset += 8; break;
                    case EIPTypeString.REAL: plcTag.SetFloat32(offset, BitConverter.ToSingle(body, bodyOffset)); bodyOffset += 4; break;
                    case EIPTypeString.LREAL: plcTag.SetFloat64(offset, BitConverter.ToDouble(body, bodyOffset)); bodyOffset += 8; break;
                    case EIPTypeString.STRING:
                        {
                            int length = BitConverter.ToInt32(body, bodyOffset);
                            bodyOffset += 4;
                            SetPlcString(plcTag, offset, System.Text.Encoding.ASCII.GetString(body, bodyOffset, Math.Max(0, length)));
                            bodyOffset += Math.Max(0, length);
                            break;
                        }
                    default:
                        Log.Logger.Warning($"Unsupported UDT field type '{field.Type}' for field '{field.Name}' during write.");
                        break;
                }
            }
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCore();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisconnectCore();
            return ValueTask.CompletedTask;
        }

        public Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReadCore(tag));
        }

        public Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default)
        {
            WriteCore(tag, value);
            return Task.CompletedTask;
        }

        public Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default)
        {
            IList<object> outputArgs = null;
            string status = ExecuteActionCore(method, inputArgs, ref outputArgs);
            return Task.FromResult(AssetActionResult.FromOutputs(status, outputArgs));
        }
    }
}
