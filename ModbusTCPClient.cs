
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    class ModbusTCPClient : IAsset
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

        private TcpClient _tcpClient = null;

        // Modbus uses long timeouts (10 seconds minimum)
        private const int _timeout = 10000;

        private ushort _transactionID = 0;

        private const byte _errorFlag = 0x80;

        private object _lock = new object();

        private void HandlerError(byte errorCode)
        {
            switch (errorCode)
            {
                case 1: throw new Exception("Illegal function");
                case 2: throw new Exception("Illegal data address");
                case 3: throw new Exception("Illegal data value");
                case 4: throw new Exception("Server failure");
                case 5: throw new Exception("Acknowledge");
                case 6: throw new Exception("Server busy");
                case 7: throw new Exception("Negative acknowledge");
                case 8: throw new Exception("Memory parity error");
                case 10: throw new Exception("Gateway path unavailable");
                case 11: throw new Exception("Target unit failed to respond");
                default: throw new Exception("Unknown error");
            }
        }

        public void Connect(string ipAddress, int port)
        {
            _tcpClient = new TcpClient(ipAddress, port);
            _tcpClient.GetStream().ReadTimeout = _timeout;
            _tcpClient.GetStream().WriteTimeout = _timeout;
        }

        public bool IsConnected()
        {
            return _tcpClient != null;
        }

        public void Disconnect()
        {
            if (_tcpClient != null)
            {
                _tcpClient.Close();
                _tcpClient = null;
            }
        }

        public Task<byte[]> Read(byte unitID, string function, uint address, ushort count)
        {
            switch (function)
            {
                case "ForceMultipleCoils": return ReadInternal(unitID, FunctionCode.ForceMultipleCoils, (ushort)address, count);
                case "ForceSingleCoil": return ReadInternal(unitID, FunctionCode.ForceSingleCoil, (ushort)address, count);
                case "PresetMultipleRegisters": return ReadInternal(unitID, FunctionCode.PresetMultipleRegisters, (ushort)address, count);
                case "PresetSingleRegister": return ReadInternal(unitID, FunctionCode.PresetSingleRegister, (ushort)address, count);
                case "ReadCoilStatus": return ReadInternal(unitID, FunctionCode.ReadCoilStatus, (ushort)address, count);
                case "ReadExceptionStatus": return ReadInternal(unitID, FunctionCode.ReadExceptionStatus, (ushort)address, count);
                case "ReadHoldingRegisters": return ReadInternal(unitID, FunctionCode.ReadHoldingRegisters, (ushort)address, count);
                case "ReadInputRegisters": return ReadInternal(unitID, FunctionCode.ReadInputRegisters, (ushort)address, count);
                case "ReadInputStatus": return ReadInternal(unitID, FunctionCode.ReadInputStatus, (ushort)address, count);
                default: return Task.FromResult(Array.Empty<byte>());
            }
        }

        public Task WriteBit(byte unitID, uint address, bool set)
        {
            return WriteCoil(unitID, (ushort)address, set);
        }

        public Task Write(byte unitID, uint address, ushort[] values)
        {
            return WriteHoldingRegisters(unitID, (ushort)address, values);
        }

        public Task<byte[]> ReadInternal(byte unitID, FunctionCode function, ushort registerBaseAddress, ushort count)
        {
            lock (_lock)
            {
                // check funtion code
                if ((function != FunctionCode.ReadInputRegisters)
                 && (function != FunctionCode.ReadHoldingRegisters)
                 && (function != FunctionCode.ReadCoilStatus))
                {
                    throw new ArgumentException("Only coil, input registers and holding registers can be read");
                }

                ApplicationDataUnit aduRequest = new ApplicationDataUnit();
                aduRequest.TransactionID = _transactionID++;
                aduRequest.Length = 6;
                aduRequest.UnitID = unitID;
                aduRequest.FunctionCode = (byte)function;

                aduRequest.Payload[0] = (byte)(registerBaseAddress >> 8);
                aduRequest.Payload[1] = (byte)(registerBaseAddress & 0x00FF);
                aduRequest.Payload[2] = (byte)(count >> 8);
                aduRequest.Payload[3] = (byte)(count & 0x00FF);

                byte[] buffer = new byte[ApplicationDataUnit.maxADU];
                aduRequest.CopyADUToNetworkBuffer(buffer);

                // send request to Modbus server
                _tcpClient.GetStream().Write(buffer, 0, ApplicationDataUnit.headerLength + 4);

                // read response header from Modbus server
                int numBytesRead = _tcpClient.GetStream().Read(buffer, 0, ApplicationDataUnit.headerLength);
                if (numBytesRead != ApplicationDataUnit.headerLength)
                {
                    throw new EndOfStreamException();
                }

                ApplicationDataUnit aduResponse = new ApplicationDataUnit();
                aduResponse.CopyHeaderFromNetworkBuffer(buffer);

                // check for error
                if ((aduResponse.FunctionCode & _errorFlag) > 0)
                {
                    // read error
                    int errorCode = _tcpClient.GetStream().ReadByte();
                    if (errorCode == -1)
                    {
                        throw new EndOfStreamException();
                    }
                    else
                    {
                        HandlerError((byte)errorCode);
                    }
                }

                // read length of response
                int length = _tcpClient.GetStream().ReadByte();
                if (length == -1)
                {
                    throw new EndOfStreamException();
                }

                // read response
                byte[] responseBuffer = new byte[length];
                numBytesRead = _tcpClient.GetStream().Read(responseBuffer, 0, length);
                if (numBytesRead != length)
                {
                    throw new EndOfStreamException();
                }

                return Task.FromResult(responseBuffer);
            }
        }

        public async Task WriteHoldingRegisters(byte unitID, ushort registerBaseAddress, ushort[] values)
        {
            // throttle writing to not overwhelm our poor little Modbus server
            await Task.Delay(1000).ConfigureAwait(false);

            lock (_lock)
            {
                if ((11 + (values.Length * 2)) > ApplicationDataUnit.maxADU)
                {
                    throw new ArgumentException("Too many values");
                }

                ApplicationDataUnit aduRequest = new ApplicationDataUnit();
                aduRequest.TransactionID = _transactionID++;
                aduRequest.Length = (ushort)(7 + (values.Length * 2));
                aduRequest.UnitID = unitID;
                aduRequest.FunctionCode = (byte)FunctionCode.PresetMultipleRegisters;

                aduRequest.Payload[0] = (byte)(registerBaseAddress >> 8);
                aduRequest.Payload[1] = (byte)(registerBaseAddress & 0x00FF);
                aduRequest.Payload[2] = (byte)(((ushort)values.Length) >> 8);
                aduRequest.Payload[3] = (byte)(((ushort)values.Length) & 0x00FF);
                aduRequest.Payload[4] = (byte)(values.Length * 2);

                int payloadIndex = 5;
                foreach (ushort value in values)
                {
                    aduRequest.Payload[payloadIndex++] = (byte)(value >> 8);
                    aduRequest.Payload[payloadIndex++] = (byte)(value & 0x00FF);
                }

                byte[] buffer = new byte[ApplicationDataUnit.maxADU];
                aduRequest.CopyADUToNetworkBuffer(buffer);

                // send request to Modbus server
                _tcpClient.GetStream().Write(buffer, 0, ApplicationDataUnit.headerLength + 5 + (values.Length * 2));

                // read response
                int numBytesRead = _tcpClient.GetStream().Read(buffer, 0, ApplicationDataUnit.headerLength + 4);
                if (numBytesRead != ApplicationDataUnit.headerLength + 4)
                {
                    throw new EndOfStreamException();
                }

                ApplicationDataUnit aduResponse = new ApplicationDataUnit();
                aduResponse.CopyHeaderFromNetworkBuffer(buffer);

                // check for error
                if ((aduResponse.FunctionCode & _errorFlag) > 0)
                {
                    // read error
                    int errorCode = _tcpClient.GetStream().ReadByte();
                    if (errorCode == -1)
                    {
                        throw new EndOfStreamException();
                    }
                    else
                    {
                        HandlerError((byte)errorCode);
                    }
                }

                // check address written
                if ((buffer[8] != (registerBaseAddress >> 8))
                 && (buffer[9] != (registerBaseAddress & 0x00FF)))
                {
                    throw new Exception("Incorrect base register returned");
                }

                // check number of registers written
                if ((buffer[10] != (((ushort)values.Length) >> 8))
                 && (buffer[11] != (((ushort)values.Length) & 0x00FF)))
                {
                    throw new Exception("Incorrect number of registers written returned");
                }
            }
        }

        public async Task WriteCoil(byte unitID, ushort coilAddress, bool set)
        {
            // throttle writing to not overwhelm our poor little Modbus server
            await Task.Delay(1000).ConfigureAwait(false);

            lock (_lock)
            {
                ApplicationDataUnit aduRequest = new ApplicationDataUnit();
                aduRequest.TransactionID = _transactionID++;
                aduRequest.Length = 6;
                aduRequest.UnitID = unitID;
                aduRequest.FunctionCode = (byte)FunctionCode.ForceSingleCoil;

                aduRequest.Payload[0] = (byte)(coilAddress >> 8);
                aduRequest.Payload[1] = (byte)(coilAddress & 0x00FF);
                aduRequest.Payload[2] = (byte)(set ? 0xFF : 0x0);
                aduRequest.Payload[3] = 0x0;

                byte[] buffer = new byte[ApplicationDataUnit.maxADU];
                aduRequest.CopyADUToNetworkBuffer(buffer);

                // send request to Modbus server
                _tcpClient.GetStream().Write(buffer, 0, ApplicationDataUnit.headerLength + 4);

                // read response
                int numBytesRead = _tcpClient.GetStream().Read(buffer, 0, ApplicationDataUnit.headerLength + 4);
                if (numBytesRead != ApplicationDataUnit.headerLength + 4)
                {
                    throw new EndOfStreamException();
                }

                ApplicationDataUnit aduResponse = new ApplicationDataUnit();
                aduResponse.CopyHeaderFromNetworkBuffer(buffer);

                // check for error
                if ((aduResponse.FunctionCode & _errorFlag) > 0)
                {
                    // read error
                    int errorCode = _tcpClient.GetStream().ReadByte();
                    if (errorCode == -1)
                    {
                        throw new EndOfStreamException();
                    }
                    else
                    {
                        HandlerError((byte)errorCode);
                    }
                }

                // check address written
                if ((buffer[8] != (coilAddress >> 8))
                 && (buffer[9] != (coilAddress & 0x00FF)))
                {
                    throw new Exception("Incorrect coil register returned");
                }

                // check flag written
                if ((buffer[10] != (set ? 0xFF : 0x0))
                 && (buffer[11] != 0x0))
                {
                    throw new Exception("Incorrect coil flag returned");
                }
            }
        }
    }
}
