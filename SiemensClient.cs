
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using S7.Net;
    using Serilog;
    using System;
    using System.Threading.Tasks;

    public class SiemensClient : IAsset
    {
        private Plc _S7 = null;
        private string _endpoint = string.Empty;

        public void Connect(string ipAddress, int port)
        {
            try
            {
                _endpoint = ipAddress;
                _S7 = new Plc(CpuType.S71500, _endpoint, 0, (short)port);
                _S7.Open();

                byte result = _S7.ReadStatus();
                if (result != 0x8)
                {
                    throw new Exception("S7 status error: " + result.ToString());
                }

                Log.Logger.Information("Connected to Siemens S7: " + result.ToString());
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public void Disconnect()
        {
            if (_S7 != null)
            {
                _S7.Close();
                _S7 = null;
            }
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            try
            {
                return Task.FromResult(_S7.ReadBytes(DataType.DataBlock, unitID, int.Parse(addressWithinAsset), count));
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return Task.FromResult(new byte[0]);
            }
        }

        public Task Write(string addressWithinAsset, byte unitID, byte[] values, bool singleBitOnly)
        {
            try
            {
                _S7.WriteBytes(DataType.DataBlock, unitID, int.Parse(addressWithinAsset), values);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }

            return Task.CompletedTask;
        }
    }
}
