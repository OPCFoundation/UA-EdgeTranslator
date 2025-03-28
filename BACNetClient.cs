
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO.BACnet;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public class BACNetClient : IAsset
    {
        private string _endpoint = string.Empty;

        private BacnetClient _client = new(new BacnetIpUdpProtocolTransport(0xBAC0, false));

        private List<string> _discoverdAssets = new();

        public List<string> Discover()
        {
            _client.OnIam += OnIAm;
            _client.Start();
            _client.WhoIs();

            Thread.Sleep(10000);

            return _discoverdAssets;
        }

        private void OnIAm(BacnetClient sender, BacnetAddress adr, uint deviceId, uint maxAPDU, BacnetSegmentations segmentation, ushort vendorId)
        {
            string newAddress = "bacnet://" + adr.ToString() + ":" + 0xBAC0.ToString();

            if (!adr.IsMyRouter(adr) && !_discoverdAssets.Contains(newAddress))
            {
                _discoverdAssets.Add(newAddress);
            }
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
                string[] addresses = ipAddress.Split('/');
                if (addresses.Length == 2)
                {
                    _endpoint = addresses[0];
                    uint deviceId = uint.Parse(addresses[1]);

                    _client.Start();

                    Connect(new BacnetAddress(BacnetAddressTypes.IP, _endpoint), deviceId, 0, BacnetSegmentations.SEGMENTATION_NONE, 0);
                    Log.Logger.Information("Connected to BACNet device at " + ipAddress);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        private void Connect(BacnetAddress adr, uint deviceid, uint maxapdu, BacnetSegmentations segmentation, ushort vendorid)
        {
            BacnetObjectId deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceid);
            _client.ReadPropertyRequest(adr, deviceObjId, BacnetPropertyIds.PROP_OBJECT_LIST, out IList<BacnetValue> value_list, arrayIndex: 0);
            if (value_list != null)
            {
                uint objectCount = value_list.First().As<uint>();
                for (uint i = 1; i <= objectCount; i++)
                {
                    _client.ReadPropertyRequest(adr, deviceObjId, BacnetPropertyIds.PROP_OBJECT_LIST, out value_list, arrayIndex: i);
                    Log.Logger.Information("Object " + value_list[0].Tag + ": " + value_list[0].Value);

                    BacnetValue Value;
                    ReadScalarValue(adr, deviceObjId.instance, value_list[0].As<BacnetObjectId>(), BacnetPropertyIds.PROP_OBJECT_NAME, out Value);
                    Log.Logger.Information("Name: " + Value.Value.ToString());

                    ReadScalarValue(adr, deviceObjId.instance, value_list[0].As<BacnetObjectId>(), BacnetPropertyIds.PROP_PRESENT_VALUE, out Value);
                    Log.Logger.Information("Value: " + Value.Value.ToString());
                }
            }
        }

        public void Disconnect()
        {
            // nothing to do
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            try
            {
                BacnetObjectId deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, unitID);
                BacnetValue Value;

                ReadScalarValue(new BacnetAddress(BacnetAddressTypes.IP, _endpoint), deviceObjId.instance, new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, byte.Parse(function)), BacnetPropertyIds.PROP_PRESENT_VALUE, out Value);
                return Task.FromResult(BitConverter.GetBytes(float.Parse(Value.Value.ToString())));
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
                BacnetObjectId deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, unitID);
                BacnetValue value = new(values[0]);

                WriteScalarValue(new BacnetAddress(BacnetAddressTypes.IP, _endpoint), deviceObjId.instance, new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, byte.Parse(function)), BacnetPropertyIds.PROP_PRESENT_VALUE, value);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message);
            }

            return Task.CompletedTask;
        }

        bool ReadScalarValue(BacnetAddress adr, uint device_id, BacnetObjectId BacnetObject, BacnetPropertyIds Proprerty, out BacnetValue value)
        {
            value = new BacnetValue(null);

            if (!_client.ReadPropertyRequest(adr, BacnetObject, Proprerty, out IList<BacnetValue> NoScalarValue))
            {
                return false;
            }

            value = NoScalarValue[0];
            return true;
        }

        bool WriteScalarValue(BacnetAddress adr, uint device_id, BacnetObjectId BacnetObject, BacnetPropertyIds Proprerty, BacnetValue Value)
        {
            BacnetValue[] NoScalarValue = { Value };
            return _client.WritePropertyRequest(adr, BacnetObject, Proprerty, NoScalarValue);
        }
    }
}
