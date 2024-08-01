
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using Sres.Net.EEIP;
    using System;
    using System.Threading.Tasks;

    public class RockwellClient : IAsset
    {
        private EEIPClient _eeipClient = null;
        private string _endpoint = string.Empty;

        public void Connect(string ipAddress, int port)
        {
            try
            {
                _endpoint = ipAddress;
                EEIPClient eeipClient = new EEIPClient();
                eeipClient.IPAddress = _endpoint;
                eeipClient.RegisterSession();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public void Disconnect()
        {
            if (_eeipClient != null)
            {
                _eeipClient.UnRegisterSession();
                _eeipClient = null;
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
                return Task.FromResult(_eeipClient.GetAttributeSingle(int.Parse(addressWithinAsset), unitID, int.Parse(function)));
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
                _eeipClient.SetAttributeSingle(int.Parse(addressWithinAsset),  unitID, 1, values);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }

            return Task.CompletedTask;
        }
    }
}
