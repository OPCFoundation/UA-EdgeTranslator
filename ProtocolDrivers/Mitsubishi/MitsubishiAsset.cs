namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using MCProtocol;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Text;
    using System.Threading.Tasks;

    public class MitsubishiAsset : IAsset
    {
        private string _endpoint = string.Empty;

        public bool IsConnected { get; private set; } = false;

        private void ConnectCore(string ipAddress, int port)
        {
            try
            {
                _endpoint = ipAddress + ":" + port.ToString();

                PLCData.PLC = new Mitsubishi.McProtocolTcp(ipAddress, port, Mitsubishi.McFrame.MC3E);
                var result = PLCData.PLC.Open().GetAwaiter().GetResult();

                Log.Logger.Information("Connected to Mitsubishi PLC: " + result.ToString());

                IsConnected = true;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        private void DisconnectCore()
        {
            if (PLCData.PLC != null)
            {
                PLCData.PLC.Close();
                PLCData.PLC = null;
            }

            IsConnected = false;
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        private object ReadCore(AssetTag tag)
        {
            object value = null;

            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if (addressParts.Length == 2)
            {
                byte[] tagBytes = Read(addressParts[0], 0, null, ushort.Parse(addressParts[1])).GetAwaiter().GetResult();

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
                        throw new ArgumentException("Type not supported by Mitsubishi.");
                    }
                }
            }

            return value;
        }

        private void WriteCore(AssetTag tag, object value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "Float")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value.ToString()));
            }
            else if (tag.Type == "Boolean")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value.ToString()));
            }
            else if (tag.Type == "Integer")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value.ToString()));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value.ToString());
            }
            else
            {
                throw new ArgumentException("Type not supported by Mitsubishi.");
            }

            Write(addressParts[0], 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }


        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            var data = new PLCData<byte>((Mitsubishi.PlcDeviceType)unitID, int.Parse(addressWithinAsset), count);

            data.ReadData();

            var result = new byte[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = data[i];
            }

            return Task.FromResult(result);
        }

        private Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            var data = new PLCData<byte>((Mitsubishi.PlcDeviceType)unitID, int.Parse(addressWithinAsset), values.Length);

            for (var i = 0; i < values.Length; i++)
            {
                data[i] = values[i];
            }

            data.WriteData();

            return Task.CompletedTask;
        }

        private string ExecuteActionCore(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            return null;
        }

        public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
        {
            ConnectCore(ipAddress, port);
            return Task.CompletedTask;
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
