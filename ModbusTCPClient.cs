
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PVMonitor
{
    public class ApplicationDataUnit
    {
        public const uint maxADU = 260;
        public const int headerLength = 8;

        public ushort TransactionID;

        // protocol is always 0 for Modbus
        public ushort ProtocolID = 0;

        public ushort Length;

        public byte UnitID;

        public byte FunctionCode;

        public byte[] Payload = new byte[maxADU - headerLength];

        public void CopyADUToNetworkBuffer(byte[] buffer)
        {
            if (buffer.Length < maxADU)
            {
                throw new ArgumentException("buffer must be at least " + maxADU.ToString() + " bytes long");
            }

            buffer[0] = (byte) (TransactionID >> 8);
            buffer[1] = (byte) (TransactionID & 0x00FF);

            buffer[2] = (byte) (ProtocolID >> 8);
            buffer[3] = (byte) (ProtocolID & 0x00FF);

            buffer[4] = (byte) (Length >> 8);
            buffer[5] = (byte) (Length & 0x00FF);

            buffer[6] = UnitID;

            buffer[7] = FunctionCode;

            Payload.CopyTo(buffer, 8);
        }

        public void CopyHeaderFromNetworkBuffer(byte[] buffer)
        {
            if (buffer.Length < headerLength)
            {
                throw new ArgumentException("buffer must be at least " + headerLength.ToString() + " bytes long");
            }

            TransactionID |= (ushort)(buffer[0] << 8);
            TransactionID = buffer[1];

            ProtocolID |= (ushort)(buffer[2] << 8);
            ProtocolID = buffer[3];

            Length = (ushort)(buffer[4] << 8);
            Length = buffer[5];

            UnitID = buffer[6];

            FunctionCode = buffer[7];
        }
    }

    class ModbusTCPClient
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

        private TcpClient tcpClient = null;

        // Modbus uses long timeouts (10 seconds minimum)
        private const int timeout = 10000;

        private ushort transactionID = 0;

        private const byte errorFlag = 0x80;

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
            tcpClient = new TcpClient(ipAddress, port);
            tcpClient.GetStream().ReadTimeout = timeout;
            tcpClient.GetStream().WriteTimeout = timeout;
        }

        public void Disconnect()
        {
            tcpClient.Close();
            tcpClient = null;
        }

        public async Task<byte[]> Read(byte unitID, FunctionCode function, ushort registerBaseAddress, ushort count)
        {
            // debounce reading to not overwhelm our poor little Modbus server
            await Task.Delay(1000).ConfigureAwait(false);

            // check funtion code
            if ((function != FunctionCode.ReadInputRegisters)
             && (function != FunctionCode.ReadHoldingRegisters)
             && (function != FunctionCode.ReadCoilStatus))
            {
                throw new ArgumentException("Only coil, input registers and holding registers can be read");
            }

            ApplicationDataUnit aduRequest = new ApplicationDataUnit();
            aduRequest.TransactionID = transactionID++;
            aduRequest.Length = 6;
            aduRequest.UnitID = unitID;
            aduRequest.FunctionCode = (byte) function;

            aduRequest.Payload[0] = (byte) (registerBaseAddress >> 8);
            aduRequest.Payload[1] = (byte) (registerBaseAddress & 0x00FF);
            aduRequest.Payload[2] = (byte) (count >> 8);
            aduRequest.Payload[3] = (byte) (count & 0x00FF);

            byte[] buffer = new byte[ApplicationDataUnit.maxADU];
            aduRequest.CopyADUToNetworkBuffer(buffer);

            // send request to Modbus server
            tcpClient.GetStream().Write(buffer, 0, ApplicationDataUnit.headerLength + 4);

            // read response header from Modbus server
            int numBytesRead = tcpClient.GetStream().Read(buffer, 0, ApplicationDataUnit.headerLength);
            if (numBytesRead != ApplicationDataUnit.headerLength)
            {
                throw new EndOfStreamException();
            }

            ApplicationDataUnit aduResponse = new ApplicationDataUnit();
            aduResponse.CopyHeaderFromNetworkBuffer(buffer);

            // check for error
            if ((aduResponse.FunctionCode & errorFlag) > 0)
            {
                // read error
                int errorCode = tcpClient.GetStream().ReadByte();
                if (errorCode == -1)
                {
                    throw new EndOfStreamException();
                }
                else
                {
                    HandlerError((byte) errorCode);
                }
            }

            // read length of response
            int length = tcpClient.GetStream().ReadByte();
            if (length == -1)
            {
                throw new EndOfStreamException();
            }

            // read response
            byte[] responseBuffer = new byte[length];
            numBytesRead = tcpClient.GetStream().Read(responseBuffer, 0, length);
            if (numBytesRead != length)
            {
                throw new EndOfStreamException();
            }

            return responseBuffer;
        }

        public async Task WriteHoldingRegisters(byte unitID, ushort registerBaseAddress, ushort[] values)
        {
            // debounce writing to not overwhelm our poor little Modbus server
            await Task.Delay(1000).ConfigureAwait(false);

            if ((11 + (values.Length * 2)) > ApplicationDataUnit.maxADU)
            {
                throw new ArgumentException("Too many values");
            }

            ApplicationDataUnit aduRequest = new ApplicationDataUnit();
            aduRequest.TransactionID = transactionID++;
            aduRequest.Length = (ushort) (7 + (values.Length * 2));
            aduRequest.UnitID = unitID;
            aduRequest.FunctionCode = (byte)FunctionCode.PresetMultipleRegisters;

            aduRequest.Payload[0] = (byte) (registerBaseAddress >> 8);
            aduRequest.Payload[1] = (byte) (registerBaseAddress & 0x00FF);
            aduRequest.Payload[2] = (byte) (((ushort) values.Length) >> 8);
            aduRequest.Payload[3] = (byte) (((ushort) values.Length) & 0x00FF);
            aduRequest.Payload[4] = (byte) (values.Length * 2);

            int payloadIndex = 5;
            foreach(ushort value in values)
            {
                aduRequest.Payload[payloadIndex++] = (byte) (value >> 8);
                aduRequest.Payload[payloadIndex++] = (byte) (value & 0x00FF);
            }

            byte[] buffer = new byte[ApplicationDataUnit.maxADU];
            aduRequest.CopyADUToNetworkBuffer(buffer);

            // send request to Modbus server
            tcpClient.GetStream().Write(buffer, 0, ApplicationDataUnit.headerLength + 5 + (values.Length * 2));

            // read response
            int numBytesRead = tcpClient.GetStream().Read(buffer, 0, ApplicationDataUnit.headerLength + 4);
            if (numBytesRead != ApplicationDataUnit.headerLength + 4)
            {
                throw new EndOfStreamException();
            }

            ApplicationDataUnit aduResponse = new ApplicationDataUnit();
            aduResponse.CopyHeaderFromNetworkBuffer(buffer);

            // check for error
            if ((aduResponse.FunctionCode & errorFlag) > 0)
            {
                // read error
                int errorCode = tcpClient.GetStream().ReadByte();
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
            if ((buffer[10] != (((ushort) values.Length) >> 8))
             && (buffer[11] != (((ushort) values.Length) & 0x00FF)))
            {
                throw new Exception("Incorrect number of registers written returned");
            }
        }

        public async Task WriteCoil(byte unitID, ushort coilAddress, bool set)
        {
            // debounce writing to not overwhelm our poor little Modbus server
            await Task.Delay(1000).ConfigureAwait(false);

            ApplicationDataUnit aduRequest = new ApplicationDataUnit();
            aduRequest.TransactionID = transactionID++;
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
            tcpClient.GetStream().Write(buffer, 0, ApplicationDataUnit.headerLength + 4);

            // read response
            int numBytesRead = tcpClient.GetStream().Read(buffer, 0, ApplicationDataUnit.headerLength + 4);
            if (numBytesRead != ApplicationDataUnit.headerLength + 4)
            {
                throw new EndOfStreamException();
            }

            ApplicationDataUnit aduResponse = new ApplicationDataUnit();
            aduResponse.CopyHeaderFromNetworkBuffer(buffer);

            // check for error
            if ((aduResponse.FunctionCode & errorFlag) > 0)
            {
                // read error
                int errorCode = tcpClient.GetStream().ReadByte();
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
