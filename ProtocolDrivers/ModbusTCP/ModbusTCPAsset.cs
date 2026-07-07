namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using NModbus;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    /// <summary>
    /// Modbus TCP implementation of IAsset using NModbus.
    /// Connect(ipAddress, port): ipAddress = host, port = TCP port.
    /// </summary>
    public class ModbusTCPAsset : IAsset
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

        private TcpClient _tcpClient;
        private IModbusMaster _master;

        private string _endpoint = string.Empty;

        // Modbus uses long timeouts (10 seconds minimum)
        private const int _timeout = 10000;

        // Deliberately throttle writes so a small / single-threaded Modbus server
        // (such as the OpenModSim demo) is not overwhelmed by back-to-back writes.
        private const int _writeThrottleMilliseconds = 1000;

        public bool IsConnected { get; private set; } = false;

        public void Connect(string ipAddress, int port)
        {
            lock (_lock)
            {
                Disconnect();

                _tcpClient = new TcpClient(ipAddress, port);
                _tcpClient.GetStream().ReadTimeout = _timeout;
                _tcpClient.GetStream().WriteTimeout = _timeout;

                var factory = new ModbusFactory();
                _master = factory.CreateMaster(_tcpClient);
                _master.Transport.ReadTimeout = _timeout;
                _master.Transport.WriteTimeout = _timeout;

                _endpoint = ipAddress + ":" + port.ToString();

                IsConnected = true;
            }
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public void Disconnect()
        {
            lock (_lock)
            {
                try { _master?.Dispose(); } catch { /* ignore */ }
                _master = null;

                if (_tcpClient != null)
                {
                    try { _tcpClient.Close(); } catch { /* ignore */ }
                    _tcpClient = null;
                }

                IsConnected = false;
            }
        }

        public object Read(AssetTag tag)
        {
            object value = null;

            FunctionCode functionCode = tag.Entity switch
            {
                "HoldingRegister" => FunctionCode.ReadHoldingRegisters,
                "InputRegister" => FunctionCode.ReadInputRegisters,
                "Coil" => FunctionCode.ReadCoilStatus,
                "DiscreteInput" => FunctionCode.ReadInputStatus,
                _ => throw UnsupportedEntity(tag.Entity)
            };

            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if ((addressParts.Length == 3) && (addressParts[1] == "quantity"))
            {
                ushort quantity = ushort.Parse(addressParts[2]);
                byte[] tagBytes = Read(addressParts[0], tag.UnitID, functionCode.ToString(), quantity).GetAwaiter().GetResult();

                if ((tagBytes != null) && (tagBytes.Length > 0))
                {
                    value = ModbusValueCodec.Decode(tag, tagBytes);
                }
            }

            return value;
        }

        private static Exception UnsupportedEntity(string entity)
        {
            string message = $"Unsupported Modbus entity '{entity ?? "(null)"}'. Expected one of: HoldingRegister, InputRegister, Coil, DiscreteInput.";
            Log.Logger.Error(message);
            return new ArgumentException(message);
        }

        private static Exception ReadOnlyEntity(string entity)
        {
            string message = $"Modbus entity '{entity ?? "(null)"}' is read-only and cannot be written.";
            Log.Logger.Error(message);
            return new InvalidOperationException(message);
        }

        public void Write(AssetTag tag, object value)
        {
            bool writeCoil = tag.Entity switch
            {
                "HoldingRegister" => false,
                "Coil" => true,
                "InputRegister" => throw ReadOnlyEntity(tag.Entity),
                "DiscreteInput" => throw ReadOnlyEntity(tag.Entity),
                _ => throw UnsupportedEntity(tag.Entity)
            };

            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = ModbusValueCodec.Encode(tag, value);

            Write(addressParts[0], tag.UnitID, tagBytes, writeCoil).GetAwaiter().GetResult();
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
                            return Task.FromResult(ModbusValueCodec.RegistersToWireBytes(regs));
                        }

                    case "ReadInputRegisters":
                        {
                            ushort[] regs = _master!.ReadInputRegisters(unitID, startAddress, count);
                            return Task.FromResult(ModbusValueCodec.RegistersToWireBytes(regs));
                        }

                    case "ReadCoilStatus":
                        {
                            // Coil reads return bool[]; pack into Modbus response format bytes (LSB-first).
                            bool[] coils = _master!.ReadCoils(unitID, startAddress, count);
                            return Task.FromResult(ModbusValueCodec.CoilsToWireBytes(coils));
                        }

                    case "ReadInputStatus":
                        {
                            // Discrete input reads return bool[]; pack into Modbus response format bytes (LSB-first).
                            bool[] inputs = _master!.ReadInputs(unitID, startAddress, count);
                            return Task.FromResult(ModbusValueCodec.CoilsToWireBytes(inputs));
                        }

                    default:
                        return Task.FromResult(Array.Empty<byte>());
                }
            }
        }

        private async Task Write(string addressWithinAsset, byte unitID, byte[] values, bool singleBitOnly)
        {
            // Deliberately throttle writes so a small / single-threaded Modbus server
            // is not overwhelmed by back-to-back writes.
            await Task.Delay(_writeThrottleMilliseconds).ConfigureAwait(false);

            lock (_lock)
            {
                ushort startAddress = ushort.Parse(addressWithinAsset);

                if (singleBitOnly)
                {
                    bool set = values != null && values.Length > 0 && values[0] != 0;
                    _master!.WriteSingleCoil(unitID, startAddress, set);
                    return;
                }

                if (values == null)
                {
                    values = Array.Empty<byte>();
                }

                if ((values.Length % 2) != 0)
                {
                    throw new ArgumentException("Register write values must be an even number of bytes.");
                }

                ushort[] regs = new ushort[values.Length / 2];
                for (int i = 0; i < regs.Length; i++)
                {
                    regs[i] = BitConverter.ToUInt16(values, i * 2);
                }

                if (regs.Length == 1)
                {
                    // Single-register writes use PresetSingleRegister (FC 6) instead of
                    // promoting the write to PresetMultipleRegisters (FC 16).
                    _master!.WriteSingleRegister(unitID, startAddress, regs[0]);
                }
                else
                {
                    _master!.WriteMultipleRegisters(unitID, startAddress, regs);
                }
            }
        }
    }
}
