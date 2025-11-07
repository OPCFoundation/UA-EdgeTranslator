using System;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Matter.Core
{
    public class Node
    {
        public ISession _secureSession;

        public ulong NodeId { get; set; }

        public byte[] OperationalNOCAsTLV { get; set; }

        public ECDsa SubjectPublicKey { get; set; }

        public string SetupCode { get; set; }

        public string Discriminator { get; set; }

        public IPAddress LastKnownIpAddress { get; set; }

        public ushort LastKnownPort { get; set; }

        public bool IsConnected { get; set; }

        public void Connect(Fabric fabric)
        {
            try
            {
                IPAddress ipAddress = LastKnownIpAddress;
                ushort port = LastKnownPort;

                CASEClient client = new CASEClient(this, fabric, ipAddress, port);
                _secureSession = client.EstablishSession();

                IsConnected = true;

                Console.WriteLine($"Established secure session to node {NodeId:X16}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to establish connection to node {NodeId:X16}: {ex.Message}");
                IsConnected = false;
            }
        }

        public async Task FetchDescriptionsAsync(Fabric fabric)
        {
            if (!IsConnected || (_secureSession == null))
            {
                return;
            }

            MessageExchange secureExchange = _secureSession.CreateExchange(fabric.RootNodeId, NodeId);

            // Request the DeviceTypeList Attribute from the Description Cluster.
            var readCluster = new MatterTLV();
            readCluster.AddStructure();
            readCluster.AddArray(tagNumber: 0);
            readCluster.AddList();
            readCluster.AddUInt32(tagNumber: 3, 0x1D); // ClusterId 0x1D - Description
            readCluster.AddUInt32(tagNumber: 4, 0x00); // Attribute 0x00 - DeviceTypeList
            readCluster.EndContainer();
            readCluster.EndContainer();
            readCluster.AddBool(tagNumber: 3, false);
            readCluster.AddUInt8(255, 12); // interactionModelRevision
            readCluster.EndContainer();
            MessageFrame deviceTypeListResponse = secureExchange.SendAndReceiveMessageAsync(readCluster, 1, ProtocolOpCode.ReadRequest).GetAwaiter().GetResult();
            if (MessageFrame.IsStatusReport(deviceTypeListResponse))
            {
                Console.WriteLine("Received error status report in response to DeviceTypeList message, abandoning FetchDescriptions!");
                return;
            }

            Console.WriteLine("Received DeviceTypeList response from node. Supported Clusters:");
            MatterTLV deviceTypeList = deviceTypeListResponse.MessagePayload.ApplicationPayload;
            deviceTypeList.OpenStructure();
            //ParseDescriptions(deviceTypeList);

            await secureExchange.AcknowledgeMessageAsync(deviceTypeListResponse.MessageCounter).ConfigureAwait(false);

            // Request the ServerList Attribute from the Description Cluster.
            readCluster = new MatterTLV();
            readCluster.AddStructure();
            readCluster.AddArray(tagNumber: 0);
            readCluster.AddList();
            readCluster.AddUInt32(tagNumber: 3, 0x1D); // ClusterId 0x1D - Description
            readCluster.AddUInt32(tagNumber: 4, 0x01); // Attribute 0x01 - ServerList
            readCluster.EndContainer();
            readCluster.EndContainer();
            readCluster.AddBool(tagNumber: 3, false);
            readCluster.AddUInt8(255, 12); // interactionModelRevision
            readCluster.EndContainer();
            MessageFrame serverListResponse = secureExchange.SendAndReceiveMessageAsync(readCluster, 1, ProtocolOpCode.ReadRequest).GetAwaiter().GetResult();
            if (MessageFrame.IsStatusReport(serverListResponse))
            {
                Console.WriteLine("Received error status report in response to ServerList message, abandoning FetchDescriptions!");
                return;
            }

            Console.WriteLine("Received ServerList response from node. Supported Clusters:");
            MatterTLV serverList = serverListResponse.MessagePayload.ApplicationPayload;
            serverList.OpenStructure();
            //ParseDescriptions(serverList);

            await secureExchange.AcknowledgeMessageAsync(serverListResponse.MessageCounter).ConfigureAwait(false);
            secureExchange.Close();
        }

        private void ParseDescriptions(MatterTLV deviceTypeList)
        {
            if (deviceTypeList.IsNextTag(0))
            {
                byte elementType = deviceTypeList.PeekElementType();
                switch (elementType)
                {
                    case 0x08: // Boolean false
                    case 0x09: // Boolean true
                        bool flag = deviceTypeList.GetBoolean(0);
                        // No active subscription; flag can be ignored.
                        break;
                    case 0x04: // Unsigned int 1 byte
                    case 0x05: // Unsigned int 2 bytes
                    case 0x06: // Unsigned int 4 bytes
                    case 0x07: // Unsigned int 8 bytes
                        ulong subscriptionId = deviceTypeList.GetUnsignedInt(0);
                        break;
                    default:
                        throw new Exception($"Unsupported element type {elementType:X2} for tag 0 in DeviceTypeList response.");
                }
            }

            if (deviceTypeList.IsNextTag(1))
            {
                deviceTypeList.OpenArray(1); // attribute reports

                while (!deviceTypeList.IsEndContainerNext())
                {
                    deviceTypeList.OpenStructure(); // attribute report

                    if (deviceTypeList.IsNextTag(0))
                    {
                        deviceTypeList.OpenStructure(0); // attribute paths

                        if (deviceTypeList.IsNextTag(0))
                        {
                            deviceTypeList.OpenList(0); // attribute path list

                            PrintEndpointClusterAttributes(deviceTypeList);

                            deviceTypeList.CloseContainer();
                        }

                        if (deviceTypeList.IsNextTag(1))
                        {
                            deviceTypeList.OpenStructure(1); // attribute status

                            byte status = deviceTypeList.GetUnsignedInt8(0);
                            byte clusterStatus = deviceTypeList.GetUnsignedInt8(1);

                            deviceTypeList.CloseContainer();
                        }
                    }

                    if (deviceTypeList.IsNextTag(1))
                    {
                        deviceTypeList.OpenStructure(1); // attribute data

                        uint dataVersion = deviceTypeList.GetUnsignedInt32(0);

                        deviceTypeList.OpenList(1); // attribute data list

                        PrintEndpointClusterAttributes(deviceTypeList);

                        deviceTypeList.CloseContainer();

                        object data = deviceTypeList.GetOctetString(2);

                        deviceTypeList.CloseContainer();
                    }

                    deviceTypeList.CloseContainer();
                }
            }
        }

        private void PrintEndpointClusterAttributes(MatterTLV deviceTypeList)
        {
            while (!deviceTypeList.IsEndContainerNext())
            {
                if (deviceTypeList.IsNextTag(0))
                {
                    byte elementType = deviceTypeList.PeekElementType();
                    switch (elementType)
                    {
                        case 0x08: // Boolean false
                        case 0x09: // Boolean true
                            bool enableTagCompression = deviceTypeList.GetBoolean(0);
                            // No tag compression; flag can be ignored.
                            break;
                        case 0x04: // Unsigned int 1 byte
                        case 0x05: // Unsigned int 2 bytes
                        case 0x06: // Unsigned int 4 bytes
                        case 0x07: // Unsigned int 8 bytes
                            ulong something = deviceTypeList.GetUnsignedInt(0);
                            break;
                        default:
                            throw new Exception($"Unsupported element type {elementType:X2} for tag 0 in EndpointClusterAttributes response.");
                    }
                }

                if (deviceTypeList.IsNextTag(1))
                {
                    ulong nodeId = deviceTypeList.GetUnsignedInt(1);
                }

                if (deviceTypeList.IsNextTag(2))
                {
                    uint endpointId = (uint)deviceTypeList.GetUnsignedInt(2);
                    Console.WriteLine($"- Endpoint ID: 0x{endpointId:X4}");
                }

                if (deviceTypeList.IsNextTag(3))
                {
                    uint clusterId = (uint)deviceTypeList.GetUnsignedInt(3);
                    Console.WriteLine($" - Cluster ID: 0x{clusterId:X4}");
                }

                if (deviceTypeList.IsNextTag(4))
                {
                    uint attributeId = (uint)deviceTypeList.GetUnsignedInt(4);
                    Console.WriteLine($" - Attribute ID: 0x{attributeId:X4}");
                }

                if (deviceTypeList.IsNextTag(5))
                {
                    ushort listIndex = (ushort)deviceTypeList.GetUnsignedInt(5);
                }

                if (deviceTypeList.IsNextTag(6))
                {
                    uint wildcardPathFlags = (uint)deviceTypeList.GetUnsignedInt(6);
                }
            }
        }
    }
}
