namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using libplctag;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading.Tasks;

    public class RockwellClient : IAsset
    {
        private string _endpoint = string.Empty;

        public List<string> Discover()
        {
            List<string> assets = new();

            // Ethernet/IP uses the boradcast message "ListIdentity" to discover PLCs on the network
            var broadcastAddress = GetBroadcastAddress();

            // Create a UDP client
            using (var udpClient = new UdpClient())
            {
                udpClient.EnableBroadcast = true;

                var listIdentityMessage = new byte[24];
                for (var i = 0; i < listIdentityMessage.Length; i++)
                {
                    listIdentityMessage[i] = 0;
                }
                listIdentityMessage[0] = 0x63;

                var endPoint = new IPEndPoint(IPAddress.Parse(broadcastAddress), 0xAF12);
                udpClient.Send(listIdentityMessage, listIdentityMessage.Length, endPoint);
                udpClient.Client.ReceiveTimeout = 10000;

                try
                {
                    while (true)
                    {
                        var receiveEndPoint = new IPEndPoint(IPAddress.Any, 0xAF12);
                        var response = udpClient.Receive(ref receiveEndPoint);

                        Log.Logger.Information($"Ethernet/IP discovery: Received response from {receiveEndPoint.Address}");

                        assets.Add("eip://" + receiveEndPoint.Address.ToString());
                    }
                }
                catch (SocketException)
                {
                    // do nothing
                }
            }

            return assets;
        }

        private string GetBroadcastAddress()
        {
            // try to restricted local broadcast
            foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var ip in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork
                     && !ip.Address.ToString().StartsWith("169.254.")
                     && !ip.Address.ToString().StartsWith("127.0."))
                    {
                        var strCurrentIP = ip.Address.ToString().Split('.');
                        var strIPNetMask = ip.IPv4Mask.ToString().Split('.');
                        var BroadcastStr = new StringBuilder();

                        for (var i = 0; i < 4; i++)
                        {
                            BroadcastStr.Append(((byte)(int.Parse(strCurrentIP[i]) | ~int.Parse(strIPNetMask[i]))).ToString());
                            if (i != 3) BroadcastStr.Append('.');
                        }

                        return BroadcastStr.ToString();
                    }
                }
            }

            // return generic broadcast address
            return "255.255.255.255";
        }

        public ThingDescription BrowseAndGenerateTD(string name, string endpoint)
        {
            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + name,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "Thing" },
                Name = name,
                Base = endpoint,
                Title = name,
                Properties = new Dictionary<string, Property>()
            };

            var address = td.Base.Split([':', '/']);
            if (address.Length != 4 || address[0] != "eip")
            {
                throw new Exception("Expected Rockwell PLC address in the format eip://ipaddress!");
            }

            Tag tags = new()
            {
                Gateway = address[3],
                Path = "1,0",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Name = "@tags",
                Timeout = TimeSpan.FromSeconds(10),
            };

            tags.Read();
            var tagInfos = DecodeAllTags(tags);
            foreach (var tag in tagInfos)
            {
                if (!IsTag(tag))
                {
                    // not interested in anything that is not a tag
                    continue;
                }

                if (TagIsUdt(tag))
                {
                    var udtTypeId = GetUdtId(tag);

                    Tag udtTag = new()
                    {
                        Gateway = address[3],
                        Path = "1,0",
                        PlcType = PlcType.ControlLogix,
                        Protocol = Protocol.ab_eip,
                        Name = $"@udt/{udtTypeId}",
                    };

                    udtTag.Read();

                    var udt = DecodeUdtInfo(udtTag);
                    foreach (var f in udt.Fields)
                    {
                        Log.Logger.Information($"EIP Tag: Id={tag.Name} Name={udt.Name} FieldName={f.Name} Offset={f.Offset} Metadata={f.Metadata} Type=" + ParseDataType(f.Type));

                        string propertyName = tag.Name + "." + udt.Name + "." + f.Name;

                        EIPForm form = new()
                        {
                            Href = propertyName + "?" + f.Offset.ToString(),
                            Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                            PollingTime = 1000,
                            Type = ParseDataType(f.Type)
                        };

                        Property property = new()
                        {
                            Type = TypeEnum.Number,
                            ReadOnly = true,
                            Observable = true,
                            Forms = new object[1] { form }
                        };

                        if (!td.Properties.ContainsKey(propertyName))
                        {
                            td.Properties.Add(propertyName, property);
                        }
                    }
                }
                else
                {
                    Log.Logger.Information($"EIP Tag: Id={tag.Name} Type=" + ParseDataType(tag.Type));

                    string properyName = tag.Name;

                    EIPForm form = new()
                    {
                        Href = properyName + "?0",
                        Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                        PollingTime = 1000,
                        Type = ParseDataType(tag.Type)
                    };

                    Property property = new()
                    {
                        Type = TypeEnum.Number,
                        ReadOnly = true,
                        Observable = true,
                        Forms = new object[1] { form }
                    };

                    if (!td.Properties.ContainsKey(properyName))
                    {
                        td.Properties.Add(properyName, property);
                    }
                }
            }

            return td;
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
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        private EIPTypeString ParseDataType(ushort eipDataTypeId)
        {
            // Data Type:   Tag Type Value:     Size of Transmitted Data:
            // BOOL         0x0nC1              1 byte
            // The BOOL value includes an additional field (n) for specifying the bit position within the SINT (n = 0 - 7).
            if ((eipDataTypeId & 0xC1) == 0xC1)
            {
                return EIPTypeString.BOOL;
            }

            switch (eipDataTypeId)
            {
                case 0xC2: return EIPTypeString.SINT;
                case 0xC3: return EIPTypeString.INT;
                case 0xC4: return EIPTypeString.DINT;
                case 0xC5: return EIPTypeString.LINT;
                case 0xC6: return EIPTypeString.USINT;
                case 0xC7: return EIPTypeString.UINT;
                case 0xC8: return EIPTypeString.UDINT;
                case 0xC9: return EIPTypeString.ULINT;
                case 0xCA: return EIPTypeString.REAL;
                case 0xCB: return EIPTypeString.LREAL;
                default: return EIPTypeString.REAL;
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

            var offset = 0;
            while (offset < tagSize)
            {
                buffer.Add(DecodeOneTagInfo(tag, offset, out var elementSize));
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

            for (var field_index = 0; field_index < num_members; field_index++)
            {
                var field_metadata = tag.GetUInt16(offset);
                offset += 2;

                var field_element_type = tag.GetUInt16(offset);
                offset += 2;

                var field_offset = tag.GetUInt16(offset);
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

            for (var field_index = 0; field_index < num_members; field_index++)
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

            return (tag.Type & TYPE_IS_STRUCT) != 0 && !((tag.Type & TYPE_IS_SYSTEM) != 0);
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

        public object Read(AssetTag tag)
        {
            object value = null;

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

        public void Write(AssetTag tag, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "BOOL")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "SINT")
            {
                tagBytes = BitConverter.GetBytes(char.Parse(value));
            }
            else if (tag.Type == "INT")
            {
                tagBytes = BitConverter.GetBytes(short.Parse(value));
            }
            else if (tag.Type == "DINT")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "LINT")
            {
                tagBytes = BitConverter.GetBytes(Int64.Parse(value));
            }
            else if (tag.Type == "USINT")
            {
                tagBytes = BitConverter.GetBytes(char.Parse(value));
            }
            else if (tag.Type == "UINT")
            {
                tagBytes = BitConverter.GetBytes(ushort.Parse(value));
            }
            else if (tag.Type == "UDINT")
            {
                tagBytes = BitConverter.GetBytes(uint.Parse(value));
            }
            else if (tag.Type == "ULINT")
            {
                tagBytes = BitConverter.GetBytes(UInt64.Parse(value));
            }
            else if (tag.Type == "REAL")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "LREAL")
            {
                tagBytes = BitConverter.GetBytes(double.Parse(value));
            }
            else
            {
                throw new ArgumentException("Type not supported by Rockwell.");
            }

            Write(addressParts[0], byte.Parse(addressParts[1]), tag.Type, tagBytes, false).GetAwaiter().GetResult();
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

        public string ExecuteAction(string actionName, string[] inputArgs, string[] outputArgs)
        {
            throw new NotImplementedException();
        }
    }
}
