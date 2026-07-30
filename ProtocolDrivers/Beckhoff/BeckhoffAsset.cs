namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Text;
    using System.Threading.Tasks;
    using Viscon.Communication.Ads;

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

    public class BeckhoffAsset : IAsset
    {
        private AdsClient _adsClient = null;

        private string _endpoint = string.Empty;

        public bool IsConnected { get; private set; } = false;

        private void ConnectCore(string ipAddress, int port)
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
                    IsConnected = true;
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

        private void DisconnectCore()
        {
            if (_adsClient != null)
            {
                _adsClient = null;
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
                        throw new ArgumentException("Type not supported by Beckhoff.");
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
