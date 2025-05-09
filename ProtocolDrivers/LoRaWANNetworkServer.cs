namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using LoRaWan;
    using LoRaWan.NetworkServer;
    using LoRaWan.NetworkServer.BasicsStation;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public class LoRaWANNetworkServer : IAsset
    {
        public LoRaWANNetworkServer()
        {
            using var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;

            var configuration = NetworkServerConfiguration.CreateFromEnvironmentVariables();
            _ = Task.Run(() => BasicsStationNetworkServer.RunServerAsync(configuration, cancellationToken));
        }

        public List<string> Discover()
        {
            return [.. SearchDevicesResult.DeviceList.Keys];
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
                Properties = new Dictionary<string, Property>()
            };

            return td;
        }

        public void Connect(string ipAddress, int port)
        {
            try
            {
                // register the device with the LoRaWAN Network Server
                var addresses = ipAddress.Split('/');
                if (addresses.Length == 2)
                {
                    var devEui = new DevEui(ulong.Parse(addresses[1]));
                    SearchDevicesResult.AddOrUpdateDevice(addresses[0], devEui, new DevNonce(0));
                }
                else
                {
                    Log.Logger.Error("Invalid LoRaWAN address format. Expected format: lorawan://<ip_address>/<device_id>");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        public void Disconnect()
        {
            // nothing to do
        }

        public string GetRemoteEndpoint()
        {
            return string.Empty;
        }

        public Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            try
            {
                // TODO

                return Task.FromResult((byte[])null);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message);
                return Task.FromResult((byte[])null);
            }
        }

        public Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            try
            {
                // TODO
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message);
            }

            return Task.CompletedTask;
        }
    }
}
