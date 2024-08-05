
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using System;
    using System.Threading.Tasks;
    using Viscon.Communication.Ads;
    using Viscon.Communication.Ads.Common;

    /*
    Authorise this Beckhoff ADS client for accessing the Beckhoff PLC by adding an AMS route.
    -----------------------------------------------------------------------------------------

    TwinCAT Engineering: Go to the tree item SYSTEM/Routes and add a static route.
    TwinCAT Systray: Open the context menu by right clicking the TwinCAT systray icon. (not available on Windows CE devices)
    TC2: Go to Properties/AMS Router/Remote Computers and restart TwinCAT
    TC3: Go to Router/Edit routes.
    TcAmsRemoteMgr: Windows CE devices can be configured locally (TC2 requires a TwinCAT restart). Tool location: /Hard Disk/System/TcAmsRemoteMgr.exe
    IPC Diagnose: Beckhoff IPC’s provide a web interface for diagnose and configuration.
    Further information: http://infosys.beckhoff.de/content/1033/devicemanager/index.html?id=286

    Sample AMS route:
      Name:           UA-EdgeTranslator
      AMS Net Id:     192.168.0.1.1.1 # NetId of UA-EdgeTranslator, derived from its IP address
      Address:        192.168.0.1     # IP address of UA-EdgeTranslator
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
                _endpoint = ipAddress;
                string[] addresses = ipAddress.Split(':');
                if (addresses.Length == 2)
                {
                    string localEndpoint = addresses[0] + ".1.1";
                    string remoteEndpoint = addresses[1] + ".1.1";
                    _adsClient = new AdsClient(localEndpoint, addresses[1], remoteEndpoint, (ushort)port);
                    _adsClient.Ams.ConnectAsync().GetAwaiter().GetResult();
                    AdsDeviceInfo result = _adsClient.ReadDeviceInfoAsync().GetAwaiter().GetResult();
                }
                else
                {
                    throw new ArgumentException("Expected ipAddress to contain both the local and remote AMS ip addresses, seperated by a ':'");
                }
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
                byte[] result = _adsClient.ReadBytesAsync(uint.Parse(addressWithinAsset), count).GetAwaiter().GetResult();
                return Task.FromResult(result);
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
                _adsClient.WriteBytesAsync(uint.Parse(addressWithinAsset), values);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }

            return Task.CompletedTask;
        }
    }
}
