namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Viscon.Communication.Ads;
    using Viscon.Communication.Ads.Common;

    /*
    Authorize this Beckhoff ADS client for accessing the Beckhoff PLC by adding an AMS route.
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

        public List<string> Discover()
        {
            // ADS does not support discovery
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
            try
            {
                _endpoint = ipAddress + ":" + port.ToString();

                var addresses = ipAddress.Split(':');
                if (addresses.Length == 2)
                {
                    var localEndpoint = addresses[0] + ".1.1";
                    var remoteEndpoint = addresses[1] + ".1.1";

                    _adsClient = new AdsClient(localEndpoint, addresses[1], remoteEndpoint, (ushort)port);
                    _adsClient.RequestTimeout = AdsClient.DefaultRequestTimeout * 2;
                    _adsClient.Ams.ConnectAsync().GetAwaiter().GetResult();

                    var result = _adsClient.ReadDeviceInfoAsync().GetAwaiter().GetResult();

                    Log.Logger.Information("Connected to Beckhoff TwinCAT ADS PLC: " + result.ToString());
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
                _adsClient = null;
            }
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public object Read(AssetTag tag)
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
                        throw new ArgumentException("Type not supported by Beckhoff.");
                    }
                }
            }

            return value;
        }

        public void Write(AssetTag tag, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "Float")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "Boolean")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "Integer")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by Beckhoff.");
            }

            Write(addressParts[0], 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }

        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            var varHandle = _adsClient.GetSymhandleByNameAsync(addressWithinAsset).GetAwaiter().GetResult();
            var result = _adsClient.ReadBytesAsync(varHandle, count).GetAwaiter().GetResult();
            return Task.FromResult(result);
        }

        private Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            _adsClient.WriteBytesAsync(uint.Parse(addressWithinAsset), values);
            return Task.CompletedTask;
        }

        public string ExecuteAction(MethodState method, string[] inputArgs, ref string[] outputArgs)
        {
            throw new NotImplementedException();
        }
    }
}
