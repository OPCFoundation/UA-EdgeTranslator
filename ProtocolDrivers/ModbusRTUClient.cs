namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using NModbus;
    using NModbus.Serial;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.IO.Ports;
    using System.Text;
    using System.Threading.Tasks;
    using static ModbusTCPClient;

    /// <summary>
    /// Modbus RTU (direct serial) implementation of IAsset using NModbus.
    /// Connect(ipAddress, port): ipAddress = serial port name/path, port = baud rate.
    /// </summary>
    internal sealed class ModbusRTUClient : IAsset
    {
        private readonly object _lock = new object();

        private SerialPort _serialPort;
        private IModbusMaster _master;

        private const int DefaultTimeoutMs = 10_000;

        public bool IsConnected { get; private set; } = false;

        public List<string> Discover()
        {
            // ModbusRTU does not support discovery
            return new List<string>();
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
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            return td;
        }

        public void Connect(string ipAddress, int port)
        {
            lock (_lock)
            {
                Disconnect();

                // if running on Linux, append /dev/ to the serial port name
                if ((Environment.OSVersion.Platform == PlatformID.Unix) && !ipAddress.StartsWith("/dev/"))
                {
                    ipAddress = "/dev/" + ipAddress;
                }

                _serialPort = new SerialPort(ipAddress)
                {
                    BaudRate = port,
                    DataBits = 8,

                    // IMPORTANT: Many Modbus RTU devices default to 8E1.
                    Parity = Parity.Even,
                    StopBits = StopBits.One,

                    ReadTimeout = DefaultTimeoutMs,
                    WriteTimeout = DefaultTimeoutMs
                };

                _serialPort.Open();

                var factory = new ModbusFactory();
                _master = factory.CreateRtuMaster(_serialPort);

                IsConnected = true;
            }
        }

        public void Disconnect()
        {
            lock (_lock)
            {
                _master = null;

                if (_serialPort != null)
                {
                    try { _serialPort.Close(); } catch { /* ignore */ }
                    _serialPort.Dispose();
                    _serialPort = null;
                }

                IsConnected = false;
            }
        }

        public string GetRemoteEndpoint()
        {
            lock (_lock)
            {
                return _serialPort?.PortName ?? string.Empty;
            }
        }

        public object Read(AssetTag tag)
        {
            object value = null;

            FunctionCode functionCode = FunctionCode.ReadCoilStatus;
            if (tag.Entity == "HoldingRegister")
            {
                functionCode = FunctionCode.ReadHoldingRegisters;
            }

            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if ((addressParts.Length == 3) && (addressParts[1] == "quantity"))
            {
                ushort quantity = ushort.Parse(addressParts[2]);
                byte[] tagBytes = Read(addressParts[0], tag.UnitID, functionCode.ToString(), quantity).GetAwaiter().GetResult();

                if ((tagBytes != null) && tag.IsBigEndian)
                {
                    tagBytes = ByteSwapper.Swap(tagBytes, tag.SwapPerWord);
                }

                if ((tagBytes != null) && (tagBytes.Length > 0))
                {
                    if (tag.Type == "Float")
                    {
                        value = BitConverter.ToSingle(tagBytes);
                    }
                    else if (tag.Type == "Boolean")
                    {
                        value = BitConverter.ToBoolean(tagBytes);
                    }
                    else if (tag.Type == "Integer")
                    {
                        value = BitConverter.ToInt32(tagBytes);
                    }
                    else if (tag.Type == "String")
                    {
                        value = Encoding.UTF8.GetString(tagBytes);
                    }
                    else
                    {
                        throw new ArgumentException("Type not supported by Modbus.");
                    }
                }
            }

            return value;
        }

        public void Write(AssetTag tag, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            ushort quantity = ushort.Parse(addressParts[2]);
            byte[] tagBytes = null;

            if ((tag.Type == "Float") && (quantity == 2))
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if ((tag.Type == "Boolean") && (quantity == 1))
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if ((tag.Type == "Integer") && (quantity == 2))
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by Modbus.");
            }

            if ((tagBytes != null) && tag.IsBigEndian)
            {
                tagBytes = ByteSwapper.Swap(tagBytes, tag.SwapPerWord);
            }

            Write(addressParts[0], tag.UnitID, tagBytes, false).GetAwaiter().GetResult();
        }

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            throw new NotImplementedException();
        }

        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            lock (_lock)
            {
                ushort startAddress = ushort.Parse(addressWithinAsset);

                switch (function)
                {
                    case "ReadHoldingRegisters":
                        {
                            ushort[] regs = _master!.ReadHoldingRegisters(unitID, startAddress, count);
                            return Task.FromResult(ToBigEndianBytes(regs));
                        }

                    case "ReadInputRegisters":
                        {
                            ushort[] regs = _master!.ReadInputRegisters(unitID, startAddress, count);
                            return Task.FromResult(ToBigEndianBytes(regs));
                        }

                    case "ReadCoilStatus":
                        {
                            // Coil reads return bool[]; we pack into Modbus response format bytes (LSB-first).
                            bool[] coils = _master!.ReadCoils(unitID, startAddress, count);
                            return Task.FromResult(PackCoils(coils));
                        }

                    default:
                        return Task.FromResult(Array.Empty<byte>());
                }
            }
        }

        private Task Write(string addressWithinAsset, byte unitID, byte[] values, bool singleBitOnly)
        {
            lock (_lock)
            {
                ushort startAddress = ushort.Parse(addressWithinAsset);

                if (singleBitOnly)
                {
                    bool set = values != null && values.Length > 0 && values[0] != 0;
                    _master!.WriteSingleCoil(unitID, startAddress, set);
                    return Task.CompletedTask;
                }
                else
                {
                    // interpret incoming byte[] as host-endian UInt16 values, then send them as Modbus registers.
                    if (values == null) values = Array.Empty<byte>();

                    if ((values.Length % 2) != 0)
                    {
                        throw new ArgumentException("Register write values must be an even number of bytes.");
                    }

                    ushort[] regs = new ushort[values.Length / 2];
                    for (int i = 0; i < regs.Length; i++)
                    {
                        regs[i] = BitConverter.ToUInt16(values, i * 2);
                    }

                    _master!.WriteMultipleRegisters(unitID, startAddress, regs);

                    return Task.CompletedTask;
                }
            }
        }

        /// <summary>
        /// Convert ushort[] registers to Modbus wire-format bytes: big-endian [Hi][Lo] per register.
        /// </summary>
        private static byte[] ToBigEndianBytes(ushort[] regs)
        {
            byte[] bytes = new byte[regs.Length * 2];
            int j = 0;

            for (int i = 0; i < regs.Length; i++)
            {
                bytes[j++] = (byte)(regs[i] >> 8);
                bytes[j++] = (byte)(regs[i] & 0xFF);
            }

            return bytes;
        }

        /// <summary>
        /// Pack coil bools into Modbus bit-packed format (LSB-first per byte).
        /// </summary>
        private static byte[] PackCoils(bool[] coils)
        {
            int byteCount = (coils.Length + 7) / 8;
            byte[] data = new byte[byteCount];

            for (int i = 0; i < coils.Length; i++)
            {
                if (coils[i])
                {
                    data[i / 8] |= (byte)(1 << (i % 8)); // LSB-first
                }
            }

            return data;
        }
    }
}
