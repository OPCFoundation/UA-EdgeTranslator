namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO.BACnet;
    using System.Linq;
    using System.Text;
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
            var newAddress = "bacnet://" + adr.ToString() + ":" + 0xBAC0.ToString();

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
                var addresses = ipAddress.Split('/');
                if (addresses.Length == 2)
                {
                    _endpoint = addresses[0];
                    var deviceId = uint.Parse(addresses[1]);

                    _client.Start();

                    Connect(new BacnetAddress(BacnetAddressTypes.IP, _endpoint), deviceId, 0, BacnetSegmentations.SEGMENTATION_NONE, 0);
                    Log.Logger.Information("Connected to BACNet device at " + ipAddress);
                }
                else
                {
                    Log.Logger.Error("Invalid BACNet address format. Expected format: bacnet://<ip_address>/<device_id>");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        private void Connect(BacnetAddress adr, uint deviceid, uint maxapdu, BacnetSegmentations segmentation, ushort vendorid)
        {
            var deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceid);
            _client.ReadPropertyRequest(adr, deviceObjId, BacnetPropertyIds.PROP_OBJECT_LIST, out var value_list, arrayIndex: 0);
            if (value_list != null)
            {
                var objectCount = value_list.First().As<uint>();
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
                        throw new ArgumentException("Type not supported by BACNet.");
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
                throw new ArgumentException("Type not supported by BACNet.");
            }

            Write(addressParts[0], 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }

        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            try
            {
                var deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, unitID);
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

        private Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            try
            {
                var deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, unitID);
                BacnetValue value = new(values[0]);

                WriteScalarValue(new BacnetAddress(BacnetAddressTypes.IP, _endpoint), deviceObjId.instance, new BacnetObjectId(BacnetObjectTypes.OBJECT_ANALOG_VALUE, byte.Parse(function)), BacnetPropertyIds.PROP_PRESENT_VALUE, value);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message);
            }

            return Task.CompletedTask;
        }

        private bool ReadScalarValue(BacnetAddress adr, uint device_id, BacnetObjectId bacnetObject, BacnetPropertyIds property, out BacnetValue value)
        {
            value = new BacnetValue(null);

            if (!_client.ReadPropertyRequest(adr, bacnetObject, property, out var NoScalarValue))
            {
                return false;
            }

            value = NoScalarValue[0];
            return true;
        }

        private bool WriteScalarValue(BacnetAddress adr, uint device_id, BacnetObjectId bacnetObject, BacnetPropertyIds property, BacnetValue value)
        {
            BacnetValue[] NoScalarValue = { value };
            return _client.WritePropertyRequest(adr, bacnetObject, property, NoScalarValue);
        }

        public string ExecuteAction(string actionName, string[] inputArgs, string[] outputArgs)
        {
            throw new NotImplementedException();
        }
    }
}
