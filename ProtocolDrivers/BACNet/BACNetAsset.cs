namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO.BACnet;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class BACNetAsset : IAsset
    {
        private string _endpoint = string.Empty;

        private uint _deviceId = 0;

        private BacnetAddress _deviceAddress = null;

        private BacnetClient _client = new(new BacnetIpUdpProtocolTransport(0xBAC0, false));

        public bool IsConnected { get; private set; } = false;

        public void Connect(string ipAddress, int port)
        {
            try
            {
                var addresses = ipAddress.Split('/');
                if (addresses.Length == 2)
                {
                    _endpoint = addresses[0];
                    _deviceId = uint.Parse(addresses[1], CultureInfo.InvariantCulture);
                    _deviceAddress = new BacnetAddress(BacnetAddressTypes.IP, _endpoint);

                    _client.Start();

                    Connect(_deviceAddress, _deviceId, 0, BacnetSegmentations.SEGMENTATION_NONE, 0);
                    Log.Logger.Information("Connected to BACNet device at " + ipAddress);
                    IsConnected = true;
                }
                else
                {
                    Log.Logger.Error("Invalid BACNet address format. Expected format: bacnet://<ip_address>/<device_id>");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, ex.Message);
            }
        }

        private void Connect(BacnetAddress adr, uint deviceid, uint maxapdu, BacnetSegmentations segmentation, ushort vendorid)
        {
            var deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceid);
            if (!_client.ReadPropertyRequest(adr, deviceObjId, BacnetPropertyIds.PROP_OBJECT_LIST, out var value_list, arrayIndex: 0))
            {
                Log.Logger.Warning("Could not read object list from BACNet device " + deviceid);
                return;
            }

            if (value_list != null)
            {
                var objectCount = value_list.First().As<uint>();
                for (uint i = 1; i <= objectCount; i++)
                {
                    if (!_client.ReadPropertyRequest(adr, deviceObjId, BacnetPropertyIds.PROP_OBJECT_LIST, out value_list, arrayIndex: i))
                    {
                        continue;
                    }

                    Log.Logger.Information("Object " + value_list[0].Tag + ": " + value_list[0].Value);

                    BacnetObjectId objectId = value_list[0].As<BacnetObjectId>();

                    if (ReadScalarValue(adr, deviceObjId.instance, objectId, BacnetPropertyIds.PROP_OBJECT_NAME, out BacnetValue nameValue))
                    {
                        Log.Logger.Information("Name: " + nameValue.Value);
                    }

                    if (ReadScalarValue(adr, deviceObjId.instance, objectId, BacnetPropertyIds.PROP_PRESENT_VALUE, out BacnetValue presentValue))
                    {
                        Log.Logger.Information("Value: " + presentValue.Value);
                    }
                }
            }
        }

        public void Disconnect()
        {
            try
            {
                _client?.Dispose();
            }
            catch (Exception)
            {
                // ignore errors on close
            }

            IsConnected = false;
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public object Read(AssetTag tag)
        {
            object value = null;

            if (!TryParseBacnetObjectAddress(tag.Address, out BacnetObjectId objectId))
            {
                Log.Logger.Error("Invalid BACNet object address: " + tag.Address);
                return null;
            }

            byte[] tagBytes = Read(objectId).GetAwaiter().GetResult();

            if ((tagBytes != null) && (tagBytes.Length > 0))
            {
                if (tag.Type == "Float")
                {
                    if (tagBytes.Length >= 4)
                    {
                        value = BitConverter.ToSingle(tagBytes, 0);
                    }
                }
                else if (tag.Type == "Boolean")
                {
                    value = tagBytes[0] != 0;
                }
                else if (tag.Type == "Integer")
                {
                    if (tagBytes.Length >= 4)
                    {
                        value = BitConverter.ToInt32(tagBytes, 0);
                    }
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

            return value;
        }

        public void Write(AssetTag tag, object value)
        {
            if (!TryParseBacnetObjectAddress(tag.Address, out BacnetObjectId objectId))
            {
                Log.Logger.Error("Invalid BACNet object address: " + tag.Address);
                return;
            }

            BacnetValue bacnetValue;

            if (tag.Type == "Float")
            {
                bacnetValue = new BacnetValue(
                    BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL,
                    float.Parse(value.ToString(), CultureInfo.InvariantCulture));
            }
            else if (tag.Type == "Boolean")
            {
                bacnetValue = new BacnetValue(
                    BacnetApplicationTags.BACNET_APPLICATION_TAG_BOOLEAN,
                    bool.Parse(value.ToString()));
            }
            else if (tag.Type == "Integer")
            {
                bacnetValue = new BacnetValue(
                    BacnetApplicationTags.BACNET_APPLICATION_TAG_SIGNED_INT,
                    int.Parse(value.ToString(), CultureInfo.InvariantCulture));
            }
            else if (tag.Type == "String")
            {
                bacnetValue = new BacnetValue(
                    BacnetApplicationTags.BACNET_APPLICATION_TAG_CHARACTER_STRING,
                    value.ToString());
            }
            else
            {
                throw new ArgumentException("Type not supported by BACNet.");
            }

            Write(objectId, bacnetValue).GetAwaiter().GetResult();
        }

        // Parse a BACnet href of the form "bacnet://<deviceId>/<objectType>,<instance>"
        // or the path-only variant "<objectType>,<instance>".
        private static bool TryParseBacnetObjectAddress(string address, out BacnetObjectId objectId)
        {
            objectId = default;

            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            string path = address;
            int schemeIndex = path.IndexOf("://", StringComparison.Ordinal);
            if (schemeIndex >= 0)
            {
                int slash = path.IndexOf('/', schemeIndex + 3);
                if (slash < 0)
                {
                    return false;
                }
                path = path.Substring(slash + 1);
            }

            // Strip any query string
            int q = path.IndexOf('?');
            if (q >= 0)
            {
                path = path.Substring(0, q);
            }

            string[] parts = path.Split(',');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!uint.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint typeValue)
                || !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint instance))
            {
                return false;
            }

            objectId = new BacnetObjectId((BacnetObjectTypes)typeValue, instance);
            return true;
        }

        private Task<byte[]> Read(BacnetObjectId objectId)
        {
            try
            {
                if (!ReadScalarValue(_deviceAddress, _deviceId, objectId, BacnetPropertyIds.PROP_PRESENT_VALUE, out BacnetValue value))
                {
                    return Task.FromResult((byte[])null);
                }

                return Task.FromResult(EncodeValue(value));
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, ex.Message);
                return Task.FromResult((byte[])null);
            }
        }

        private Task Write(BacnetObjectId objectId, BacnetValue value)
        {
            try
            {
                WriteScalarValue(_deviceAddress, _deviceId, objectId, BacnetPropertyIds.PROP_PRESENT_VALUE, value);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, ex.Message);
            }

            return Task.CompletedTask;
        }

        private static byte[] EncodeValue(BacnetValue value)
        {
            if (value.Value == null)
            {
                return Array.Empty<byte>();
            }

            return value.Value switch
            {
                float f => BitConverter.GetBytes(f),
                double d => BitConverter.GetBytes((float)d),
                int i => BitConverter.GetBytes(i),
                uint ui => BitConverter.GetBytes((int)ui),
                short s => BitConverter.GetBytes((int)s),
                ushort us => BitConverter.GetBytes((int)us),
                bool b => new[] { (byte)(b ? 1 : 0) },
                string str => Encoding.UTF8.GetBytes(str),
                _ => Encoding.UTF8.GetBytes(value.Value.ToString() ?? string.Empty),
            };
        }

        private bool ReadScalarValue(BacnetAddress adr, uint device_id, BacnetObjectId bacnetObject, BacnetPropertyIds property, out BacnetValue value)
        {
            value = new BacnetValue(null);

            if (!_client.ReadPropertyRequest(adr, bacnetObject, property, out var NoScalarValue))
            {
                return false;
            }

            if (NoScalarValue == null || NoScalarValue.Count == 0)
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

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            return null;
        }
    }
}
