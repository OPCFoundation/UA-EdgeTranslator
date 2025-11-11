using System;
using System.Collections.Generic;
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

        public string LastKnownIpAddress { get; set; }

        public ushort LastKnownPort { get; set; }

        public bool IsConnected { get; set; }

        public void Connect(Fabric fabric)
        {
            try
            {
                IPAddress ipAddress = IPAddress.Parse(LastKnownIpAddress);
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
            MessageFrame deviceTypeListResponse = await secureExchange.SendAndReceiveMessageAsync(readCluster, 1, ProtocolOpCode.ReadRequest).ConfigureAwait(false);
            if (MessageFrame.IsStatusReport(deviceTypeListResponse))
            {
                Console.WriteLine("Received error status report in response to DeviceTypeList message, abandoning FetchDescriptions!");
                return;
            }

            await secureExchange.AcknowledgeMessageAsync(deviceTypeListResponse.MessageCounter).ConfigureAwait(false);

            bool moreChunkedMessages = false;
            do
            {
                Console.WriteLine("Received DeviceTypeList response from node. Supported Clusters:");
                MatterTLV deviceTypeList = deviceTypeListResponse.MessagePayload.ApplicationPayload;
                deviceTypeList.OpenStructure();
                ParseDescriptions(deviceTypeList);

                if (deviceTypeList.IsTagNext(3))
                {
                    moreChunkedMessages = deviceTypeList.GetBoolean(3);
                }

                if (moreChunkedMessages)
                {
                    deviceTypeListResponse = await secureExchange.WaitForNextMessageAsync().ConfigureAwait(false);
                    if (MessageFrame.IsStatusReport(deviceTypeListResponse))
                    {
                        Console.WriteLine("Received error status report in response to DeviceTypeList chunked message, abandoning FetchDescriptions!");
                        return;
                    }

                    await secureExchange.AcknowledgeMessageAsync(deviceTypeListResponse.MessageCounter).ConfigureAwait(false);
                }
            }
            while (moreChunkedMessages);

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
            MessageFrame serverListResponse = await secureExchange.SendAndReceiveMessageAsync(readCluster, 1, ProtocolOpCode.ReadRequest).ConfigureAwait(false);
            if (MessageFrame.IsStatusReport(serverListResponse))
            {
                Console.WriteLine("Received error status report in response to ServerList message, abandoning FetchDescriptions!");
                return;
            }

            await secureExchange.AcknowledgeMessageAsync(serverListResponse.MessageCounter).ConfigureAwait(false);

            moreChunkedMessages = false;
            do
            {
                Console.WriteLine("Received ServerList response from node. Supported Clusters:");
                MatterTLV serverList = serverListResponse.MessagePayload.ApplicationPayload;
                serverList.OpenStructure();
                ParseDescriptions(serverList);

                if (serverList.IsTagNext(3))
                {
                    moreChunkedMessages = serverList.GetBoolean(3);
                }

                if (moreChunkedMessages)
                {
                    serverListResponse = await secureExchange.WaitForNextMessageAsync().ConfigureAwait(false);
                    if (MessageFrame.IsStatusReport(serverListResponse))
                    {
                        Console.WriteLine("Received error status report in response to ServerList chunked message, abandoning FetchDescriptions!");
                        return;
                    }
                }

                await secureExchange.AcknowledgeMessageAsync(serverListResponse.MessageCounter).ConfigureAwait(false);
            }
            while (moreChunkedMessages);

            secureExchange.Close();
        }

        private void ParseDescriptions(MatterTLV deviceTypeList)
        {
            try
            {
                if (deviceTypeList.IsTagNext(0))
                {
                    byte elementType = deviceTypeList.PeekElementType();
                    switch (elementType)
                    {
                        case (byte)ElementType.False:
                        case (byte)ElementType.True:
                            bool flag = deviceTypeList.GetBoolean(0);
                            break;

                        case (byte)ElementType.Byte:
                        case (byte)ElementType.UShort:
                        case (byte)ElementType.UInt:
                        case (byte)ElementType.ULong:
                            ulong subscriptionId = deviceTypeList.GetUnsignedInt(0);
                            break;

                        default:
                            throw new Exception($"Unsupported element type {elementType:X} for tag 0 in DeviceTypeList response.");
                    }
                }

                if (deviceTypeList.IsTagNext(1))
                {
                    deviceTypeList.OpenArray(1); // attribute reports

                    while (!deviceTypeList.IsEndContainerNext())
                    {
                        deviceTypeList.OpenStructure(); // attribute report

                        if (deviceTypeList.IsTagNext(0))
                        {
                            deviceTypeList.OpenStructure(0); // attribute paths

                            if (deviceTypeList.IsTagNext(0))
                            {
                                deviceTypeList.OpenList(0); // attribute path list

                                PrintEndpointClusterAttributes(deviceTypeList);

                                deviceTypeList.CloseContainer(); // attribute path list
                            }

                            if (deviceTypeList.IsTagNext(1))
                            {
                                deviceTypeList.OpenStructure(1); // attribute status

                                ulong status = deviceTypeList.GetUnsignedInt(0);

                                if (!deviceTypeList.IsEndContainerNext())
                                {
                                    ulong clusterStatus = deviceTypeList.GetUnsignedInt(1);
                                }

                                deviceTypeList.CloseContainer(); // attribute status
                            }

                            deviceTypeList.CloseContainer(); // attribute paths
                        }

                        if (deviceTypeList.IsTagNext(1))
                        {
                            deviceTypeList.OpenStructure(1); // attribute data

                            ulong dataVersion = deviceTypeList.GetUnsignedInt(0);

                            deviceTypeList.OpenList(1); // attribute data list

                            PrintEndpointClusterAttributes(deviceTypeList);

                            deviceTypeList.CloseContainer(); // attribute data list

                            object data = deviceTypeList.GetObject(2);
                            if (data is List<object> dataList)
                            {
                                Console.WriteLine(" - Data List:");
                                foreach (var item in dataList)
                                {
                                    if (item is List<object> innerDataList)
                                    {
                                        Console.WriteLine(" - Data List:");
                                        foreach (var innerItem in innerDataList)
                                        {
                                            Console.WriteLine($"   - {innerItem:X}");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($" - Data: {item:X}");
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine($" - Data: {data:X}");
                            }

                            deviceTypeList.CloseContainer(); // attribute data
                        }

                        deviceTypeList.CloseContainer(); // attribute report
                    }

                    deviceTypeList.CloseContainer(); // attribute reports
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing descriptions: {ex.Message}");
            }
        }

        private void PrintEndpointClusterAttributes(MatterTLV deviceTypeList)
        {
            while (!deviceTypeList.IsEndContainerNext())
            {
                if (deviceTypeList.IsTagNext(0))
                {
                    byte elementType = deviceTypeList.PeekElementType();
                    switch (elementType)
                    {
                        case (byte)ElementType.False:
                        case (byte)ElementType.True:
                            bool enableTagCompression = deviceTypeList.GetBoolean(0);
                            break;
                        default:
                            throw new Exception($"Unexpected element type {elementType:X} for tag 0 in EndpointClusterAttributes response.");
                    }
                }

                if (deviceTypeList.IsTagNext(1))
                {
                    ulong nodeId = deviceTypeList.GetUnsignedInt(1);
                }

                if (deviceTypeList.IsTagNext(2))
                {
                    uint endpointId = (uint)deviceTypeList.GetUnsignedInt(2);
                    Console.WriteLine($"- Endpoint ID: 0x{endpointId:X}");
                }

                if (deviceTypeList.IsTagNext(3))
                {
                    uint clusterId = (uint)deviceTypeList.GetUnsignedInt(3);
                    Console.WriteLine($" - Cluster ID: 0x{clusterId:X}");
                }

                if (deviceTypeList.IsTagNext(4))
                {
                    uint attributeId = (uint)deviceTypeList.GetUnsignedInt(4);
                    Console.WriteLine($" - Attribute ID: 0x{attributeId:X}");
                }

                if (deviceTypeList.IsTagNext(5))
                {
                    ushort listIndex = (ushort)deviceTypeList.GetUnsignedInt(5);
                }

                if (deviceTypeList.IsTagNext(6))
                {
                    uint wildcardPathFlags = (uint)deviceTypeList.GetUnsignedInt(6);
                }
            }
        }
    }
}
