
namespace Opc.Ua.Edge.Translator
{
    using MCProtocol;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using System;
    using System.Threading.Tasks;

    public class MitsubishiClient : IAsset
    {
        private string _endpoint = string.Empty;

        public void Connect(string ipAddress, int port)
        {
            try
            {
                _endpoint = ipAddress + ":" + port.ToString();

                PLCData.PLC = new Mitsubishi.McProtocolTcp(ipAddress, port, Mitsubishi.McFrame.MC3E);
                int result = PLCData.PLC.Open().GetAwaiter().GetResult();

                Log.Logger.Information("Connected to Mitsubishi PLC: " + result.ToString());
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public void Disconnect()
        {
            if (PLCData.PLC != null)
            {
                PLCData.PLC.Close();
                PLCData.PLC = null;
            }
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            PLCData<byte> data = new PLCData<byte>((Mitsubishi.PlcDeviceType)unitID, int.Parse(addressWithinAsset), count);

            data.ReadData();

            byte[] result = new byte[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = data[i];
            }

            return Task.FromResult(result);
        }

        public Task Write(string addressWithinAsset, byte unitID, byte[] values, bool singleBitOnly)
        {
            PLCData<byte> data = new PLCData<byte>((Mitsubishi.PlcDeviceType)unitID, int.Parse(addressWithinAsset), values.Length);

            for (int i = 0; i < values.Length; i++)
            {
                data[i] = values[i];
            }

            data.WriteData();

            return Task.CompletedTask;
        }
    }
}
