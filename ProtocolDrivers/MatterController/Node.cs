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
            MessageFrame deviceTypeListResponse = secureExchange.SendAndReceiveMessageAsync(readCluster, 1, 2).GetAwaiter().GetResult();
            if (MessageFrame.IsStatusReport(deviceTypeListResponse))
            {
                Console.WriteLine("Received error status report in response to DeviceTypeList message, abandoning FetchDescriptions!");
                return;
            }

            await secureExchange.AcknowledgeMessageAsync(deviceTypeListResponse.MessageCounter).ConfigureAwait(false);

            Console.WriteLine("Received DeviceTypeList response from node. Supported Clusters:");

            MatterTLV deviceTypeList = deviceTypeListResponse.MessagePayload.ApplicationPayload;
            deviceTypeList.OpenStructure();

            if (deviceTypeList.IsNextTag(0))
            {
                uint SubscriptionId = deviceTypeList.GetUnsignedInt32(0);
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

                            while (!deviceTypeList.IsEndContainerNext())
                            {
                                if (deviceTypeList.IsNextTag(0))
                                {
                                    bool EnableTagCompression = deviceTypeList.GetBoolean(0);
                                }

                                if (deviceTypeList.IsNextTag(1))
                                {
                                    ulong NodeId = deviceTypeList.GetUnsignedInt(1);
                                }

                                if (deviceTypeList.IsNextTag(2))
                                {
                                    uint EndpointId = (uint)deviceTypeList.GetUnsignedInt(2);
                                }

                                if (deviceTypeList.IsNextTag(3))
                                {
                                    uint ClusterId = (uint)deviceTypeList.GetUnsignedInt(3);
                                }

                                if (deviceTypeList.IsNextTag(4))
                                {
                                    uint AttributeId = (uint)deviceTypeList.GetUnsignedInt(4);
                                }

                                if (deviceTypeList.IsNextTag(5))
                                {
                                    ushort ListIndex = (ushort)deviceTypeList.GetUnsignedInt(5);
                                }

                                if (deviceTypeList.IsNextTag(6))
                                {
                                    uint WildcardPathFlags = (uint)deviceTypeList.GetUnsignedInt(6);
                                }
                            }

                            deviceTypeList.CloseContainer();
                        }

                        if (deviceTypeList.IsNextTag(1))
                        {
                            deviceTypeList.OpenStructure(1); // attribute status

                            byte Status = deviceTypeList.GetUnsignedInt8(0);
                            byte ClusterStatus = deviceTypeList.GetUnsignedInt8(1);

                            deviceTypeList.CloseContainer();
                        }
                    }

                    if (deviceTypeList.IsNextTag(1))
                    {
                        deviceTypeList.OpenStructure(1); // attribute data

                        uint DataVersion = deviceTypeList.GetUnsignedInt32(0);

                        deviceTypeList.OpenList(1); // attribute data list

                        while (!deviceTypeList.IsEndContainerNext())
                        {
                            if (deviceTypeList.IsNextTag(0))
                            {
                                bool EnableTagCompression = deviceTypeList.GetBoolean(0);
                            }

                            if (deviceTypeList.IsNextTag(1))
                            {
                                ulong NodeId = deviceTypeList.GetUnsignedInt(1);
                            }

                            if (deviceTypeList.IsNextTag(2))
                            {
                                uint EndpointId = (uint)deviceTypeList.GetUnsignedInt(2);
                                Console.WriteLine($"- Endpoint ID: 0x{EndpointId:X4}");
                            }

                            if (deviceTypeList.IsNextTag(3))
                            {
                                uint ClusterId = (uint)deviceTypeList.GetUnsignedInt(3);
                                Console.WriteLine($" - Cluster ID: 0x{ClusterId:X4}");
                            }

                            if (deviceTypeList.IsNextTag(4))
                            {
                                uint AttributeId = (uint)deviceTypeList.GetUnsignedInt(4);
                                Console.WriteLine($" - Attribute ID: 0x{AttributeId:X4}");
                            }

                            if (deviceTypeList.IsNextTag(5))
                            {
                                ushort ListIndex = (ushort)deviceTypeList.GetUnsignedInt(5);
                            }

                            if (deviceTypeList.IsNextTag(6))
                            {
                                uint WildcardPathFlags = (uint)deviceTypeList.GetUnsignedInt(6);
                            }
                        }

                        deviceTypeList.CloseContainer();

                        object Data = deviceTypeList.GetData(2);

                        deviceTypeList.CloseContainer();
                    }

                    deviceTypeList.CloseContainer();
                }
            }

            secureExchange.Close();
        }
    }
}
