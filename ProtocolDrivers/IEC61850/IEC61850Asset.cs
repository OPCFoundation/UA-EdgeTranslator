namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using IEC61850.Client;
    using IEC61850.Common;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Text;
    using System.Threading.Tasks;

    internal class IEC61850Asset : IAsset
    {
        private IedConnection _client = new();

        private string _endpoint = string.Empty;

        public bool IsConnected { get; private set; } = false;

        private void ConnectCore(string ipAddress, int port)
        {
            _client.Connect(ipAddress, port);
            _endpoint = ipAddress + ":" + port;
            IsConnected = true;
        }

        private void DisconnectCore()
        {
            try
            {
                _client.Abort();
            }
            catch (Exception)
            {
                // do nothing
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
                        throw new ArgumentException("Type not supported by IEC61850.");
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
                throw new ArgumentException("Type not supported by IEC61850.");
            }

            Write(addressParts[0], 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }

        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            var value = _client.ReadFloatValue(addressWithinAsset, FunctionalConstraint.SP);

#pragma warning disable SYSLIB0011
            BinaryFormatter bf = new();
            using (MemoryStream ms = new())
            {
                bf.Serialize(ms, value);
#pragma warning restore SYSLIB0011

                return Task.FromResult(ms.ToArray());
            }
        }

        private Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            using (MemoryStream memStream = new(values))
            {
#pragma warning disable SYSLIB0011
                BinaryFormatter binForm = new();

                var value = (float)binForm.Deserialize(memStream);
#pragma warning restore SYSLIB0011

                _client.WriteValue(addressWithinAsset, FunctionalConstraint.SP, new MmsValue(values));
            }

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
