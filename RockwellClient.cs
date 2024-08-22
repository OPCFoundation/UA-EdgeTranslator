
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

                _eeipClient = new EEIPClient();
                _eeipClient.IPAddress = _endpoint;

                uint result = _eeipClient.RegisterSession();

                Log.Logger.Information("Connected to Rockwell PLC: " + result.ToString());
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
            // the generic input is mapped to Ethernet/IP parameters like so:
            // address  -> Ethernet/IP class ID
            // unitID   -> Ethernet/IP instance ID
            // function -> Ethernet/IP attribute ID
            byte[] result = _eeipClient.GetAttributeSingle(int.Parse(addressWithinAsset), unitID, int.Parse(function));
            return Task.FromResult(result);
        }

        public Task Write(string addressWithinAsset, byte unitID, byte[] values, bool singleBitOnly)
        {
            _eeipClient.SetAttributeSingle(int.Parse(addressWithinAsset),  unitID, 1, values);
            return Task.CompletedTask;
        }
    }
}
