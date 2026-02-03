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

        public bool IsConnected { get; private set; } = false;

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

            tag.Write();

            return Task.CompletedTask;
        }

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            return null;
        }
    }
}
