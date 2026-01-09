namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Sockets;
    using System.Text;
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

        private string _endpoint = string.Empty;

        // Modbus uses long timeouts (10 seconds minimum)
        private const int _timeout = 10000;

        private ushort _transactionID = 0;

        private const byte _errorFlag = 0x80;

        private object _lock = new object();

        public bool IsConnected { get; private set; } = false;

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

        public List<string> Discover()
        {
            // ModbusTCP does not support discovery
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
            _tcpClient = new TcpClient(ipAddress, port);
            _tcpClient.GetStream().ReadTimeout = _timeout;
            _tcpClient.GetStream().WriteTimeout = _timeout;

            _endpoint = ipAddress + ":" + port.ToString();

            IsConnected = true;
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public void Disconnect()
        {
            if (_tcpClient != null)
            {
                try
                {
                    _tcpClient.Close();
                }
                catch (Exception)
                {
                    // ignore errors on close
                }

                _tcpClient = null;
            }

            IsConnected = false;
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

            Write(addressParts[0], tag.UnitID, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }


        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            var registerAddress = ushort.Parse(addressWithinAsset);

            switch (function)
            {
                case "ForceMultipleCoils": return ReadInternal(unitID, FunctionCode.ForceMultipleCoils, registerAddress, count);
                case "ForceSingleCoil": return ReadInternal(unitID, FunctionCode.ForceSingleCoil, registerAddress, count);
                case "PresetMultipleRegisters": return ReadInternal(unitID, FunctionCode.PresetMultipleRegisters, registerAddress, count);
                case "PresetSingleRegister": return ReadInternal(unitID, FunctionCode.PresetSingleRegister, registerAddress, count);
                case "ReadCoilStatus": return ReadInternal(unitID, FunctionCode.ReadCoilStatus, registerAddress, count);
                case "ReadExceptionStatus": return ReadInternal(unitID, FunctionCode.ReadExceptionStatus, registerAddress, count);
                case "ReadHoldingRegisters": return ReadInternal(unitID, FunctionCode.ReadHoldingRegisters, registerAddress, count);
                case "ReadInputRegisters": return ReadInternal(unitID, FunctionCode.ReadInputRegisters, registerAddress, count);
                case "ReadInputStatus": return ReadInternal(unitID, FunctionCode.ReadInputStatus, registerAddress, count);
                default: return Task.FromResult(Array.Empty<byte>());
            }
        }

        private Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            var registerAddress = ushort.Parse(addressWithinAsset);

            if (singleBitOnly && values.Length > 0)
            {
                return WriteCoil(unitID, registerAddress, values[0] != 0);
            }
            else
            {
                var ushortArrayLength = values.Length / 2;
                var ushortArray = new ushort[ushortArrayLength];

                for (var i = 0; i < ushortArrayLength; i++)
                {
                    ushortArray[i] = BitConverter.ToUInt16(values, i * 2);
                }

                return WriteHoldingRegisters(unitID, registerAddress, ushortArray);
            }
        }

        public Task<byte[]> ReadInternal(byte unitID, FunctionCode function, ushort registerBaseAddress, ushort count)
        {
            if (_tcpClient == null)
            {
                throw new Exception("Not connected to Modbus server");
            }

            lock (_lock)
            {
                // check funtion code
                if (function != FunctionCode.ReadInputRegisters
                 && function != FunctionCode.ReadHoldingRegisters
                 && function != FunctionCode.ReadCoilStatus)
                {
                    throw new ArgumentException("Only coil, input registers and holding registers can be read");
                }

                var aduRequest = new ApplicationDataUnit();
                aduRequest.TransactionID = _transactionID++;
                aduRequest.Length = 6;
                aduRequest.UnitID = unitID;
                aduRequest.FunctionCode = (byte)function;

                aduRequest.Payload[0] = (byte)(registerBaseAddress >> 8);
                aduRequest.Payload[1] = (byte)(registerBaseAddress & 0x00FF);
                aduRequest.Payload[2] = (byte)(count >> 8);
                aduRequest.Payload[3] = (byte)(count & 0x00FF);

                var buffer = new byte[ApplicationDataUnit.maxADU];
                aduRequest.CopyADUToNetworkBuffer(buffer);

                // send request to Modbus server
                _tcpClient.GetStream().Write(buffer, 0, ApplicationDataUnit.headerLength + 4);

                // read response header from Modbus server
                var numBytesRead = _tcpClient.GetStream().Read(buffer, 0, ApplicationDataUnit.headerLength);
                if (numBytesRead != ApplicationDataUnit.headerLength)
                {
                    throw new EndOfStreamException();
                }

                var aduResponse = new ApplicationDataUnit();
                aduResponse.CopyHeaderFromNetworkBuffer(buffer);

                // check for error
                if ((aduResponse.FunctionCode & _errorFlag) > 0)
                {
                    // read error
                    var errorCode = _tcpClient.GetStream().ReadByte();
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
                var length = _tcpClient.GetStream().ReadByte();
                if (length == -1)
                {
                    throw new EndOfStreamException();
                }

                // read response
                var responseBuffer = new byte[length];
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
                if (11 + values.Length * 2 > ApplicationDataUnit.maxADU)
                {
                    throw new ArgumentException("Too many values");
                }

                var aduRequest = new ApplicationDataUnit();
                aduRequest.TransactionID = _transactionID++;
                aduRequest.Length = (ushort)(7 + values.Length * 2);
                aduRequest.UnitID = unitID;
                aduRequest.FunctionCode = (byte)FunctionCode.PresetMultipleRegisters;

                aduRequest.Payload[0] = (byte)(registerBaseAddress >> 8);
                aduRequest.Payload[1] = (byte)(registerBaseAddress & 0x00FF);
                aduRequest.Payload[2] = (byte)((ushort)values.Length >> 8);
                aduRequest.Payload[3] = (byte)((ushort)values.Length & 0x00FF);
                aduRequest.Payload[4] = (byte)(values.Length * 2);

                var payloadIndex = 5;
                foreach (var value in values)
                {
                    aduRequest.Payload[payloadIndex++] = (byte)(value >> 8);
                    aduRequest.Payload[payloadIndex++] = (byte)(value & 0x00FF);
                }

                var buffer = new byte[ApplicationDataUnit.maxADU];
                aduRequest.CopyADUToNetworkBuffer(buffer);

                // send request to Modbus server
                _tcpClient.GetStream().Write(buffer, 0, ApplicationDataUnit.headerLength + 5 + values.Length * 2);

                // read response
                var numBytesRead = _tcpClient.GetStream().Read(buffer, 0, ApplicationDataUnit.headerLength + 4);
                if (numBytesRead != ApplicationDataUnit.headerLength + 4)
                {
                    throw new EndOfStreamException();
                }

                var aduResponse = new ApplicationDataUnit();
                aduResponse.CopyHeaderFromNetworkBuffer(buffer);

                // check for error
                if ((aduResponse.FunctionCode & _errorFlag) > 0)
                {
                    // read error
                    var errorCode = _tcpClient.GetStream().ReadByte();
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
                if (buffer[8] != registerBaseAddress >> 8
                 && buffer[9] != (registerBaseAddress & 0x00FF))
                {
                    throw new Exception("Incorrect base register returned");
                }

                // check number of registers written
                if (buffer[10] != (ushort)values.Length >> 8
                 && buffer[11] != ((ushort)values.Length & 0x00FF))
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
                var aduRequest = new ApplicationDataUnit();
                aduRequest.TransactionID = _transactionID++;
                aduRequest.Length = 6;
                aduRequest.UnitID = unitID;
                aduRequest.FunctionCode = (byte)FunctionCode.ForceSingleCoil;

                aduRequest.Payload[0] = (byte)(coilAddress >> 8);
                aduRequest.Payload[1] = (byte)(coilAddress & 0x00FF);
                aduRequest.Payload[2] = (byte)(set ? 0xFF : 0x0);
                aduRequest.Payload[3] = 0x0;

                var buffer = new byte[ApplicationDataUnit.maxADU];
                aduRequest.CopyADUToNetworkBuffer(buffer);

                // send request to Modbus server
                _tcpClient.GetStream().Write(buffer, 0, ApplicationDataUnit.headerLength + 4);

                // read response
                var numBytesRead = _tcpClient.GetStream().Read(buffer, 0, ApplicationDataUnit.headerLength + 4);
                if (numBytesRead != ApplicationDataUnit.headerLength + 4)
                {
                    throw new EndOfStreamException();
                }

                var aduResponse = new ApplicationDataUnit();
                aduResponse.CopyHeaderFromNetworkBuffer(buffer);

                // check for error
                if ((aduResponse.FunctionCode & _errorFlag) > 0)
                {
                    // read error
                    var errorCode = _tcpClient.GetStream().ReadByte();
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
                if (buffer[8] != coilAddress >> 8
                 && buffer[9] != (coilAddress & 0x00FF))
                {
                    throw new Exception("Incorrect coil register returned");
                }

                // check flag written
                if (buffer[10] != (set ? 0xFF : 0x0)
                 && buffer[11] != 0x0)
                {
                    throw new Exception("Incorrect coil flag returned");
                }
            }
        }

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            throw new NotImplementedException();
        }
    }
}
