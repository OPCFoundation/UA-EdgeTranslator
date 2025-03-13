
namespace Opc.Ua.Edge.Translator
{
    using libplctag;
    using libplctag.NativeImport;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class RockwellClient : IAsset
    {
        private string _endpoint = string.Empty;

        public List<string> Discover()
        {
            List<string> assets = new();

            // Create a tag for discovery
            Tag tags = new()
            {
                Path = "1,0",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Name = "@Discovery",
                Timeout = TimeSpan.FromSeconds(10),
            };

            tags.Read();

            TagInfo[] tagInfos = DecodeAllTags(tags);
            foreach (TagInfo tag in tagInfos)
            {
                // TODO: Process results
            }

            return assets;
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

                TagInfo[] tagInfos = DecodeAllTags(tags);
                foreach (TagInfo tag in tagInfos)
                {
                    if (!IsTag(tag))
                    {
                        // not interested in anything that is not a tag
                        continue;
                    }

                    if (TagIsUdt(tag))
                    {
                        int udtTypeId = GetUdtId(tag);

                        Tag udtTag = new()
                        {
                            Gateway = ipAddress,
                            Path = "1,0",
                            PlcType = PlcType.ControlLogix,
                            Protocol = Protocol.ab_eip,
                            Name = $"@udt/{udtTypeId}",
                        };

                        udtTag.Read();

                        UdtInfo udt = DecodeUdtInfo(udtTag);
                        foreach (UdtFieldInfo f in udt.Fields)
                        {
                            Log.Logger.Information($"EIP Tag: Id={tag.Name} Name={udt.Name} FieldName={f.Name} Offset={f.Offset} Metadata={f.Metadata} Type=" + ParseDataType(f.Type));
                        }
                    }
                    else
                    {
                        Log.Logger.Information($"EIP Tag: Id={tag.Name} Type=" + ParseDataType(tag.Type));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        private string ParseDataType(ushort eipDataTypeId)
        {
            // Data Type:   Tag Type Value:     Size of Transmitted Data:
            // BOOL         0x0nC1              1 byte
            // The BOOL value includes an additional field (n) for specifying the bit position within the SINT (n = 0 - 7).
            if ((eipDataTypeId & 0xC1) == 0xC1)
            {
                return "BOOL";
            }

            switch (eipDataTypeId)
            {
                case 0xC2: return "SINT";
                case 0xC3: return "INT";
                case 0xC4: return "DINT";
                case 0xC5: return "LINT";
                case 0xC6: return "USINT";
                case 0xC7: return "UINT";
                case 0xC8: return "UDINT";
                case 0xC9: return "ULINT";
                case 0xCA: return "REAL";
                case 0xCB: return "LREAL";
                default: return eipDataTypeId.ToString();
            }
        }

        private TagInfo DecodeOneTagInfo(Tag tag, int offset, out int elementSize)
        {

            var tagInstanceId = tag.GetUInt32(offset);
            var tagType = tag.GetUInt16(offset + 4);
            var tagLength = tag.GetUInt16(offset + 6);
            var tagArrayDims = new uint[]
            {
                tag.GetUInt32(offset + 8),
                tag.GetUInt32(offset + 12),
                tag.GetUInt32(offset + 16)
            };

            var apparentTagNameLength = (int)tag.GetUInt16(offset + 20);
            var actualTagNameLength = Math.Min(apparentTagNameLength, 200 * 2 - 1);

            var tagNameBytes = Enumerable.Range(offset + 22, actualTagNameLength)
                .Select(o => tag.GetUInt8(o))
                .Select(Convert.ToByte)
                .ToArray();

            var tagName = Encoding.ASCII.GetString(tagNameBytes);

            elementSize = 22 + actualTagNameLength;

            return new TagInfo()
            {
                Id = tagInstanceId,
                Type = tagType,
                Name = tagName,
                Length = tagLength,
                Dimensions = tagArrayDims
            };

        }

        private TagInfo[] DecodeAllTags(Tag tag)
        {
            var buffer = new List<TagInfo>();

            var tagSize = tag.GetSize();

            int offset = 0;
            while (offset < tagSize)
            {
                buffer.Add(DecodeOneTagInfo(tag, offset, out int elementSize));
                offset += elementSize;
            }

            return buffer.ToArray();
        }

        private UdtInfo DecodeUdtInfo(Tag tag)
        {

            var template_id = tag.GetUInt16(0);
            var member_desc_size = tag.GetUInt32(2);
            var udt_instance_size = tag.GetUInt32(6);
            var num_members = tag.GetUInt16(10);
            var struct_handle = tag.GetUInt16(12);

            var udtInfo = new UdtInfo()
            {
                Fields = new UdtFieldInfo[num_members],
                NumFields = num_members,
                Handle = struct_handle,
                Id = template_id,
                Size = udt_instance_size
            };

            var offset = 14;

            for (int field_index = 0; field_index < num_members; field_index++)
            {
                ushort field_metadata = tag.GetUInt16(offset);
                offset += 2;

                ushort field_element_type = tag.GetUInt16(offset);
                offset += 2;

                ushort field_offset = tag.GetUInt16(offset);
                offset += 4;

                var field = new UdtFieldInfo()
                {
                    Offset = field_offset,
                    Metadata = field_metadata,
                    Type = field_element_type,
                };

                udtInfo.Fields[field_index] = field;
            }

            var name_str = tag.GetString(offset).Split(';')[0];
            udtInfo.Name = name_str;

            offset += tag.GetStringTotalLength(offset);

            for (int field_index = 0; field_index < num_members; field_index++)
            {
                udtInfo.Fields[field_index].Name = tag.GetString(offset);
                offset += tag.GetStringTotalLength(offset);
            }

            return udtInfo;

        }

        private bool TagIsUdt(TagInfo tag)
        {
            const ushort TYPE_IS_STRUCT = 0x8000;
            const ushort TYPE_IS_SYSTEM = 0x1000;

            return ((tag.Type & TYPE_IS_STRUCT) != 0) && !((tag.Type & TYPE_IS_SYSTEM) != 0);
        }

        private int GetUdtId(TagInfo tag)
        {
            const ushort TYPE_UDT_ID_MASK = 0x0FFF;
            return tag.Type & TYPE_UDT_ID_MASK;
        }

        private bool IsTag(TagInfo tag)
        {
            if (tag.Name.StartsWith("Program:"))
            {
                return false;
            }

            if (tag.Name.StartsWith("Task:"))
            {
                return false;
            }

            if (tag.Name.StartsWith("Map:"))
            {
                return false;
            }

            return true;
        }

        public void Disconnect()
        {
            // nothing to do
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
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
                case "BOOL": return Task.FromResult(BitConverter.GetBytes(tag.GetBit(offset)));
                case "SINT": return Task.FromResult(new byte[] { (byte) tag.GetInt8(offset) } );
                case "INT": return Task.FromResult(BitConverter.GetBytes(tag.GetInt16(offset)));
                case "DINT": return Task.FromResult(BitConverter.GetBytes(tag.GetInt32(offset)));
                case "LINT": return Task.FromResult(BitConverter.GetBytes(tag.GetInt64(offset)));
                case "USINT": return Task.FromResult(new byte[] { tag.GetUInt8(offset) } );
                case "UINT": return Task.FromResult(BitConverter.GetBytes(tag.GetUInt16(offset)));
                case "UDINT": return Task.FromResult(BitConverter.GetBytes(tag.GetUInt32(offset)));
                case "ULINT": return Task.FromResult(BitConverter.GetBytes(tag.GetUInt64(offset)));
                case "REAL": return Task.FromResult(BitConverter.GetBytes(tag.GetFloat32(offset)));
                case "LREAL": return Task.FromResult(BitConverter.GetBytes(tag.GetFloat64(offset)));
                default: return Task.FromResult((byte[]) null);
            }
        }

        public Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
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
                case "SINT": tag.SetInt8(offset, (sbyte) BitConverter.ToChar(values)); break;
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

            return Task.CompletedTask;
        }
    }
}
