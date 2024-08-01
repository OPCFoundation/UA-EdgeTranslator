
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using System;
    using System.Threading.Tasks;
    using TwinCAT.Ads;

    /*
    Authorise your ADS client for the TwinCAT target by adding an AMS route.
    ------------------------------------------------------------------------

    TwinCAT Engineering: Go to the tree item SYSTEM/Routes and add a static route.
    TwinCAT Systray: Open the context menue by right click the TwinCAT systray icon. (not available on Windows CE devices)
    TC2: Go to Properties/AMS Router/Remote Computers and restart TwinCAT
    TC3: Go to Router/Edit routes.
    TcAmsRemoteMgr: Windows CE devices can be configured locally (TC2 requires a TwinCAT restart). Tool location: /Hard Disk/System/TcAmsRemoteMgr.exe
    IPC Diagnose: Beckhoff IPC’s provide a web interface for diagnose and configuration.
    Further information: http://infosys.beckhoff.de/content/1033/devicemanager/index.html?id=286

    Sample AMS route:
      Name:           MyAdsClient
      AMS Net Id:     192.168.0.1.1.1 # NetId of your ADS client, derived from its IP address or set by bhf::ads:SetLocalAdress().
      Address:        192.168.0.1     # Use the IP of the ADS client, which is connected to your TwinCAT target
      Transport Type: TCP/IP
      Remote Route:   None / Server
      Unidirectional: false
      Secure ADS:     false
    */

    public class BeckhoffClient : IAsset
    {
        private AdsClient _adsClient = null;
        private string _endpoint = string.Empty;

        public void Connect(string ipAddress, int port)
        {
            try
            {
                _endpoint = ipAddress + ".1.1";
                _adsClient = new AdsClient();
                _adsClient.Connect(AmsNetId.Parse(_endpoint), port);
                StateInfo result = _adsClient.ReadState();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public void Disconnect()
        {
            if (_adsClient != null)
            {
                _adsClient.Disconnect();
                _adsClient.Dispose();
                _adsClient = null;
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
                byte[] buffer = new byte[count];
                _adsClient.Read(unitID, uint.Parse(addressWithinAsset), buffer);
                return Task.FromResult(buffer);
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
                _adsClient.Write(unitID, uint.Parse(addressWithinAsset), values);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }

            return Task.CompletedTask;
        }
    }
}
