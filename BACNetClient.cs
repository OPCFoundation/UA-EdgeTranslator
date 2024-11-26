
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO.BACnet;
    using System.Linq;
    using System.Threading.Tasks;

    public class BACNetClient : IAsset
    {
        private string _endpoint = string.Empty;
        private BacnetClient _client;

        public void Connect(string ipAddress, int port)
        {
            try
            {
                _endpoint = ipAddress;

                BacnetIpUdpProtocolTransport transport = new(0xBAC0, false);
                _client = new BacnetClient(transport);
                _client.OnIam += OnIAm;
                _client.Start();
                _client.WhoIs(-1, -1, new BacnetAddress(BacnetAddressTypes.IP, _endpoint));

                Log.Logger.Information("Connected to BACNet device at " + ipAddress);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        private void OnIAm(BacnetClient sender, BacnetAddress adr, uint deviceid, uint maxapdu, BacnetSegmentations segmentation, ushort vendorid)
        {
            Log.Logger.Information($"Detected device {deviceid} at {adr}");

            BacnetObjectId deviceObjId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceid);
            sender.ReadPropertyRequest(adr, deviceObjId, BacnetPropertyIds.PROP_OBJECT_LIST, out IList<BacnetValue> value_list, arrayIndex: 0);

            var objectCount = value_list.First().As<uint>();
            for (uint i = 1; i <= objectCount; i++)
            {
                sender.ReadPropertyRequest(adr, deviceObjId, BacnetPropertyIds.PROP_OBJECT_LIST, out value_list, arrayIndex: i);
                Log.Logger.Information("Object " + value_list[0].Tag + ": " + value_list[0].Value);

                BacnetValue Value;
                ReadScalarValue(sender, adr, deviceObjId.instance, value_list[0].As<BacnetObjectId>(), BacnetPropertyIds.PROP_OBJECT_NAME, out Value);
                Log.Logger.Information("Name: " + Value.Value.ToString());

                ReadScalarValue(sender, adr, deviceObjId.instance, value_list[0].As<BacnetObjectId>(), BacnetPropertyIds.PROP_PRESENT_VALUE, out Value);
                Log.Logger.Information("Value: " + Value.Value.ToString());
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
            // TODO
            return Task.FromResult((byte[]) null);
        }

        public Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            // TODO
            return Task.CompletedTask;
        }

        bool ReadScalarValue(BacnetClient sender, BacnetAddress adr, uint device_id, BacnetObjectId BacnetObject, BacnetPropertyIds Proprerty, out BacnetValue Value)
        {
            Value = new BacnetValue(null);

            if (!sender.ReadPropertyRequest(adr, BacnetObject, Proprerty, out IList<BacnetValue> NoScalarValue))
            {
                return false;
            }

            Value = NoScalarValue[0];
            return true;
        }

        bool WriteScalarValue(BacnetClient sender, BacnetAddress adr, uint device_id, BacnetObjectId BacnetObject, BacnetPropertyIds Proprerty, BacnetValue Value)
        {
            BacnetValue[] NoScalarValue = { Value };
            return sender.WritePropertyRequest(adr, BacnetObject, Proprerty, NoScalarValue);
        }
    }
}
