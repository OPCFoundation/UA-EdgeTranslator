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

    /// <summary>
    /// Modbus RTU (direct serial) implementation of IAsset using NModbus.
    /// Connect(ipAddress, port): ipAddress = serial port name/path, port = baud rate.
    /// </summary>
    public class ModbusRTUAsset : IAsset
    {
        public enum FunctionCode : byte
        {
            ReadCoilStatus = 1,
            ReadInputStatus = 2,
            ReadHoldingRegisters = 3,
            ReadInputRegisters = 4,
            ForceSingleCoil = 5,
            PresetSingleRegister = 6,
            ReadExceptionStatus = 7,
            ForceMultipleCoils = 15,
            PresetMultipleRegisters = 16
        }

        private readonly object _lock = new object();

        private SerialPort _serialPort;
        private IModbusMaster _master;

        private const int DefaultTimeoutMs = 10_000;

        public bool IsConnected { get; private set; } = false;

        public void Connect(string ipAddress, int port)
        {
            lock (_lock)
            {
                Disconnect();

                string comPort = string.Empty;
                int baudRate = 0;
                int dataBits = 0;
                Parity parity = Parity.None;
                StopBits stopBits = StopBits.None;
                int unitId = 1;

                // split ipAddress into port name, baud rate, data bits, parity, stop bits and unit ID, formatted as "COM3/19200/8/E/1/1"
                string[] parts = ipAddress.Split('/');
                if (parts.Length >= 8)
                {
                    comPort = parts[2];
                    baudRate = int.Parse(parts[3]);
                    dataBits = int.Parse(parts[4]);
                    unitId = int.Parse(parts[7]);

                    switch (parts[5].ToUpper())
                    {
                        case "N":
                            parity = Parity.None;
                            break;
                        case "E":
                            parity = Parity.Even;
                            break;
                        case "O":
                            parity = Parity.Odd;
                            break;
                        case "M":
                            parity = Parity.Mark;
                            break;
                        case "S":
                            parity = Parity.Space;
                            break;
                        default:
                            parity = Parity.None;
                            break;
                    }

                    switch (parts[6])
                    {
                        case "1":
                            stopBits = StopBits.One;
                            break;
                        case "1.5":
                            stopBits = StopBits.OnePointFive;
                            break;
                        case "2":
                            stopBits = StopBits.Two;
                            break;
                        default:
                            stopBits = StopBits.None;
                            break;
                    }
                }

                // if running on Linux, append /dev/ to the com port name
                if ((Environment.OSVersion.Platform == PlatformID.Unix) && !comPort.StartsWith("/dev/"))
                {
                    comPort = "/dev/" + comPort;
                }

                _serialPort = new SerialPort(comPort)
                {
                    BaudRate = baudRate,
                    DataBits = dataBits,
                    Parity = parity,
                    StopBits = stopBits,
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
            return null;
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
