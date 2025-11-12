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
                if (_secureSession != null)
                {
                    IsConnected = true;
                    Console.WriteLine($"Established secure session to node {NodeId:X16}.");
                }
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
                ParseDescriptions(deviceTypeList, false);

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
                ParseDescriptions(serverList, true);

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

        private void ParseDescriptions(MatterTLV list, bool isServerList)
        {
            try
            {
                if (list.IsTagNext(0))
                {
                    byte elementType = list.PeekElementType();
                    switch (elementType)
                    {
                        case (byte)ElementType.False:
                        case (byte)ElementType.True:
                            bool flag = list.GetBoolean(0);
                            break;

                        case (byte)ElementType.Byte:
                        case (byte)ElementType.UShort:
                        case (byte)ElementType.UInt:
                        case (byte)ElementType.ULong:
                            ulong subscriptionId = list.GetUnsignedInt(0);
                            break;

                        default:
                            throw new Exception($"Unsupported element type {elementType:X} for tag 0 in DeviceTypeList response.");
                    }
                }

                if (list.IsTagNext(1))
                {
                    list.OpenArray(1); // attribute reports

                    while (!list.IsEndContainerNext())
                    {
                        list.OpenStructure(); // attribute report

                        if (list.IsTagNext(0))
                        {
                            list.OpenStructure(0); // attribute paths

                            if (list.IsTagNext(0))
                            {
                                list.OpenList(0); // attribute path list

                                PrintEndpointClusterAttributes(list);

                                list.CloseContainer(); // attribute path list
                            }

                            if (list.IsTagNext(1))
                            {
                                list.OpenStructure(1); // attribute status

                                ulong status = list.GetUnsignedInt(0);

                                if (!list.IsEndContainerNext())
                                {
                                    string clusterStatus = list.GetUTF8String(1);
                                }

                                list.CloseContainer(); // attribute status
                            }

                            list.CloseContainer(); // attribute paths
                        }

                        if (list.IsTagNext(1))
                        {
                            list.OpenStructure(1); // attribute data

                            ulong dataVersion = list.GetUnsignedInt(0);

                            list.OpenList(1); // attribute data list

                            PrintEndpointClusterAttributes(list);

                            list.CloseContainer(); // attribute data list

                            object data = list.GetObject(2);
                            if (data is List<object> dataList)
                            {
                                Console.WriteLine(" Data List:");
                                foreach (var item in dataList)
                                {
                                    if (item is List<object> innerDataList)
                                    {
                                        Console.WriteLine(" Data List:");
                                        foreach (var innerItem in innerDataList)
                                        {
                                            object dataName = innerItem;
                                            if (isServerList)
                                            {
                                                if (MatterV13Clusters.IdToName.ContainsKey((ulong)innerItem))
                                                {
                                                    dataName = MatterV13Clusters.IdToName[(ulong)innerItem];
                                                }
                                            }
                                            else
                                            {
                                                if (MatterV13DeviceTypes.IdToName.ContainsKey((ulong)innerItem))
                                                {
                                                    dataName = MatterV13DeviceTypes.IdToName[(ulong)innerItem];
                                                }
                                            }

                                            Console.WriteLine($"  {dataName}");
                                        }
                                    }
                                    else
                                    {
                                        object dataName = item;
                                        if (isServerList)
                                        {
                                            if (MatterV13Clusters.IdToName.ContainsKey((ulong)item))
                                            {
                                                dataName = MatterV13Clusters.IdToName[(ulong)item];
                                            }
                                        }
                                        else
                                        {
                                            if (MatterV13DeviceTypes.IdToName.ContainsKey((ulong)item))
                                            {
                                                dataName = MatterV13DeviceTypes.IdToName[(ulong)item];
                                            }
                                        }

                                        Console.WriteLine($"  {dataName}");
                                    }
                                }
                            }
                            else
                            {
                                object dataName = data;
                                if (isServerList)
                                {
                                    if (MatterV13Clusters.IdToName.ContainsKey((ulong)data))
                                    {
                                        dataName = MatterV13Clusters.IdToName[(ulong)data];
                                    }
                                }
                                else
                                {
                                    if (MatterV13DeviceTypes.IdToName.ContainsKey((ulong)data))
                                    {
                                        dataName = MatterV13DeviceTypes.IdToName[(ulong)data];
                                    }
                                }

                                Console.WriteLine($"  {dataName}");
                            }

                            list.CloseContainer(); // attribute data
                        }

                        list.CloseContainer(); // attribute report
                    }

                    list.CloseContainer(); // attribute reports
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing descriptions: {ex.Message}");
            }
        }

        private void PrintEndpointClusterAttributes(MatterTLV list)
        {
            while (!list.IsEndContainerNext())
            {
                if (list.IsTagNext(0))
                {
                    byte elementType = list.PeekElementType();
                    switch (elementType)
                    {
                        case (byte)ElementType.False:
                        case (byte)ElementType.True:
                            bool enableTagCompression = list.GetBoolean(0);
                            break;

                        case (byte)ElementType.Byte:
                        case (byte)ElementType.UShort:
                        case (byte)ElementType.UInt:
                        case (byte)ElementType.ULong:
                            ulong id = list.GetUnsignedInt(0);
                            Console.WriteLine($"ID: 0x{id:X}");
                            break;

                        default:
                            throw new Exception($"Unexpected element type {elementType:X} for tag 0 in EndpointClusterAttributes response.");
                    }
                }

                if (list.IsTagNext(1))
                {
                    ulong nodeId = list.GetUnsignedInt(1);
                    Console.WriteLine($"Node ID: 0x{nodeId:X}");
                }

                if (list.IsTagNext(2))
                {
                    uint endpointId = (uint)list.GetUnsignedInt(2);
                    Console.WriteLine($"Endpoint ID: 0x{endpointId:X}");
                }

                if (list.IsTagNext(3))
                {
                    uint clusterId = (uint)list.GetUnsignedInt(3);
                    Console.WriteLine($" Cluster ID: 0x{clusterId:X}");
                }

                if (list.IsTagNext(4))
                {
                    uint attributeId = (uint)list.GetUnsignedInt(4);
                    Console.WriteLine($" Attribute ID: 0x{attributeId:X}");
                }

                if (list.IsTagNext(5))
                {
                    ushort listIndex = (ushort)list.GetUnsignedInt(5);
                    Console.WriteLine($" List Index: {listIndex}");
                }

                if (list.IsTagNext(6))
                {
                    uint wildcardPathFlags = (uint)list.GetUnsignedInt(6);
                    Console.WriteLine($" Wildcard Path Flags: 0x{wildcardPathFlags:X}");
                }
            }
        }
    }
}
