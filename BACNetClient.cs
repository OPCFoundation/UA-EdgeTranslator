
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO.BACnet;
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
                _client.WhoIs();

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

            IList<BacnetValue> value_list;
            sender.ReadPropertyRequest(adr, new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceid), BacnetPropertyIds.PROP_OBJECT_LIST, out value_list);

            LinkedList<BacnetObjectId> object_list = new LinkedList<BacnetObjectId>();
            foreach (BacnetValue value in value_list)
            {
                if (Enum.IsDefined(typeof(BacnetObjectTypes), ((BacnetObjectId)value.Value).Type))
                {
                    object_list.AddLast((BacnetObjectId)value.Value);
                }
            }

            foreach (BacnetObjectId object_id in object_list)
            {
                //read all properties
                IList<BacnetValue> values = null;
                try
                {
                    if (!sender.ReadPropertyRequest(adr, object_id, BacnetPropertyIds.PROP_PRESENT_VALUE, out values))
                    {
                        Log.Logger.Error("Couldn't fetch 'present value' for object: " + object_id.ToString());
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("Couldn't fetch 'present value' for object: " + object_id.ToString() + ": " + ex.Message);
                    continue;
                }

                Log.Logger.Information("Object Name: " + object_id.ToString() + ", Property Id: " + values[0].Tag.ToString() + ", Value: " + values[0].Value.ToString());
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
    }
}
