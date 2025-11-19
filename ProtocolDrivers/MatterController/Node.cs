using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Matter.Core
{
    public class Node
    {
        public string Name { get; set; }

        public ulong NodeId { get; set; }

        public byte[] OperationalNOCAsTLV { get; set; }

        public ECDsa SubjectPublicKey { get; set; }

        public string SetupCode { get; set; }

        public string Discriminator { get; set; }

        public string LastKnownIpAddress { get; set; }

        public ushort LastKnownPort { get; set; }

        private ISession _secureSession;


        public bool Connect(Fabric fabric)
        {
            try
            {
                if (_secureSession != null)
                {
                    Console.WriteLine($"Already connected to node {NodeId:X16}.");
                    return true;
                }

                IPAddress ipAddress = IPAddress.Parse(LastKnownIpAddress);
                ushort port = LastKnownPort;

                CASEClient client = new CASEClient(this, fabric, ipAddress, port);

                _secureSession = client.EstablishSession();
                if (_secureSession != null)
                {
                    Console.WriteLine($"Established secure session to node {NodeId:X16}.");
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to establish connection to node {NodeId:X16}: {ex.Message}");
                return false;
            }
        }

        public string ExecuteCommand(Fabric fabric, string clusterName, string commandName, object[] parameters, bool timed = false)
        {
            // find our cluster ID
            ulong clusterId = MatterV13Clusters.IdToName.FirstOrDefault(x => x.Value.Equals(clusterName, StringComparison.OrdinalIgnoreCase)).Key;
            if (clusterId == 0)
            {
                return $"Cluster name '{clusterName}' not found.";
            }
            else
            {
                if (_secureSession == null)
                {
                    return "Could not connect to Matter device!";
                }

                MessageExchange secureExchange = _secureSession.CreateExchange(fabric.RootNodeId, NodeId);

                // map booleans for command
                if ((commandName == "True") || (commandName == "False"))
                {
                    commandName = (commandName == "True") ? "1" : "0";
                }

                MessageFrame commandResponseMessageFrame;
                if (timed)
                {
                    // 10s timeout
                    commandResponseMessageFrame = secureExchange.SendTimedCommandAsync(10000, 1, (byte)clusterId, byte.Parse(commandName), parameters).GetAwaiter().GetResult();
                }
                else
                {
                    commandResponseMessageFrame = secureExchange.SendCommandAsync(1, (byte)clusterId, byte.Parse(commandName), parameters).GetAwaiter().GetResult();
                }

                if (MessageFrame.IsError(commandResponseMessageFrame))
                {
                    Console.WriteLine($"Received error status in response to command {commandName}!");
                    return "Command failed.";
                }
                else
                {
                    // parse status response
                    if ((commandResponseMessageFrame.MessagePayload.ApplicationPayload != null) && (commandResponseMessageFrame.MessagePayload.OpCode == ProtocolOpCode.StatusResponse))
                    {
                        var status = StatusResponseResult.Parse(commandResponseMessageFrame.MessagePayload.ApplicationPayload);
                        if (!status.IsSuccess)
                        {
                            string diag = $"Command failed: {status.ImStatus}"
                                        + (status.ClusterStatus.HasValue ? $", ClusterStatus=0x{status.ClusterStatus.Value:X4}" : string.Empty);
                            Console.WriteLine(diag);
                            return diag;
                        }
                    }
                    else
                    {
                        if ((commandResponseMessageFrame.MessagePayload.ApplicationPayload != null) && (commandResponseMessageFrame.MessagePayload.OpCode == ProtocolOpCode.InvokeResponse))
                        {
                            // simply print the reponse payload
                            Console.WriteLine("Command response: " + Convert.ToHexString(commandResponseMessageFrame.MessagePayload.ApplicationPayload.Serialize()));
                        }
                        else
                        {
                            Console.WriteLine($"Unexpected OpCode {commandResponseMessageFrame.MessagePayload.OpCode} in command response.");
                            return $"Unexpected OpCode {commandResponseMessageFrame.MessagePayload.OpCode} in command response.";
                        }
                    }
                }

                secureExchange.AcknowledgeMessageAsync(commandResponseMessageFrame.MessageCounter).GetAwaiter().GetResult();

                secureExchange.Close();
            }

            return "success";
        }

        public string WriteACLAttribute(Fabric fabric, string clusterName, string attributeName, object[] parameters)
        {
            if (parameters.Length < 1)
            {
                return "Must provide target cluster!";
            }

            // find our cluster ID
            ulong clusterId = MatterV13Clusters.IdToName.FirstOrDefault(x => x.Value.Equals(clusterName, StringComparison.OrdinalIgnoreCase)).Key;
            if (clusterId == 0)
            {
                return $"Cluster name '{clusterName}' not found.";
            }
            else
            {
                if (_secureSession == null)
                {
                    return "Could not connect to Matter device!";
                }

                MessageExchange secureExchange = _secureSession.CreateExchange(fabric.RootNodeId, NodeId);

                // Request the DeviceTypeList Attribute from the Description Cluster.
                var readCluster = new MatterTLV();
                var payload = new MatterTLV();
                payload.AddStructure(); // WriteRequest
                payload.AddList(0);     // AttributePath list
                payload.AddStructure();
                payload.AddUInt16(0, 0);  // Root endpoint
                payload.AddUInt32(1, 31); // Cluster
                payload.AddUInt16(2, 0);  // Attribute
                payload.EndContainer();
                payload.EndContainer();
                payload.AddArray(1); // Data (ACL list)
                payload.AddStructure(); // New ACL entry
                payload.AddUInt8(0, 1); // FabricIndex
                payload.AddUInt8(1, 3); // Privilege = Operate
                payload.AddUInt8(2, 2); // AuthMode = CASE
                payload.AddArray(3);    // Subjects
                payload.AddUInt64(0, fabric.RootNodeId); // Node ID
                payload.EndContainer();
                payload.AddArray(4);    // Targets
                payload.AddStructure();
                payload.AddUInt32(0, uint.Parse(parameters[0].ToString())); // target cluster
                payload.EndContainer();
                payload.EndContainer(); // Targets
                payload.EndContainer(); // ACL entry
                payload.EndContainer(); // ACL list
                payload.EndContainer(); // WriteRequest
                MessageFrame attributeResponse = secureExchange.SendAndReceiveMessageAsync(readCluster, 1, ProtocolOpCode.ReadRequest).GetAwaiter().GetResult();
                if (MessageFrame.IsError(attributeResponse))
                {
                    Console.WriteLine("Received error status in response to write attribute message!");
                    return "Write Attribute failed.";
                }

                secureExchange.AcknowledgeMessageAsync(attributeResponse.MessageCounter).GetAwaiter().GetResult();

                secureExchange.Close();
            }

            return "success";
        }

        public async Task<bool> FetchDescriptionsAsync(Fabric fabric)
        {
            try
            {
                if (_secureSession == null)
                {
                    return false;
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
                if (MessageFrame.IsError(deviceTypeListResponse))
                {
                    Console.WriteLine("Received error status in response to DeviceTypeList message, abandoning FetchDescriptions!");
                    return false;
                }

                await secureExchange.AcknowledgeMessageAsync(deviceTypeListResponse.MessageCounter).ConfigureAwait(false);

                bool moreChunkedMessages = false;
                do
                {
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
                        if (MessageFrame.IsError(deviceTypeListResponse))
                        {
                            Console.WriteLine("Received error status in response to DeviceTypeList chunked message, abandoning FetchDescriptions!");
                            return false;
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
                if (MessageFrame.IsError(serverListResponse))
                {
                    Console.WriteLine("Received error status in response to ServerList message, abandoning FetchDescriptions!");
                    return false;
                }

                await secureExchange.AcknowledgeMessageAsync(serverListResponse.MessageCounter).ConfigureAwait(false);

                moreChunkedMessages = false;
                do
                {
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
                        if (MessageFrame.IsError(serverListResponse))
                        {
                            Console.WriteLine("Received error status in response to ServerList chunked message, abandoning FetchDescriptions!");
                            return false;
                        }
                    }

                    await secureExchange.AcknowledgeMessageAsync(serverListResponse.MessageCounter).ConfigureAwait(false);
                }
                while (moreChunkedMessages);

                secureExchange.Close();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch descriptions from node {NodeId:X16}: {ex.Message}");
                return false;
            }
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

                                GetEndpointClusterAttributes(list);

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

                            GetEndpointClusterAttributes(list);

                            list.CloseContainer(); // attribute data list

                            object data = list.GetObject(2);
                            if (data is List<object> dataList)
                            {
                                foreach (var item in dataList)
                                {
                                    if (item is List<object> innerDataList)
                                    {
                                        foreach (var innerItem in innerDataList)
                                        {
                                            string dataName = $"{innerItem:X}";
                                            if (isServerList)
                                            {
                                                if (MatterV13Clusters.IdToName.ContainsKey((ulong)innerItem))
                                                {
                                                    dataName = $"Supported Matter Cluster: {MatterV13Clusters.IdToName[(ulong)innerItem]}";
                                                }
                                            }
                                            else
                                            {
                                                if (MatterV13DeviceTypes.IdToName.ContainsKey((ulong)innerItem))
                                                {
                                                    dataName = $"Supported Matter Device Type: {MatterV13DeviceTypes.IdToName[(ulong)innerItem]}";
                                                }
                                            }

                                            Console.WriteLine(dataName);
                                        }
                                    }
                                    else
                                    {
                                        string dataName = $"{item:X}";
                                        if (isServerList)
                                        {
                                            if (MatterV13Clusters.IdToName.ContainsKey((ulong)item))
                                            {
                                                dataName = $"Supported Matter Cluster: {MatterV13Clusters.IdToName[(ulong)item]}";
                                            }
                                        }
                                        else
                                        {
                                            if (MatterV13DeviceTypes.IdToName.ContainsKey((ulong)item))
                                            {
                                                dataName = $"Supported Matter Device Type: {MatterV13DeviceTypes.IdToName[(ulong)item]}";
                                            }
                                        }

                                        Console.WriteLine(dataName);
                                    }
                                }
                            }
                            else
                            {
                                string dataName = $"{data:X}";
                                if (isServerList)
                                {
                                    if (MatterV13Clusters.IdToName.ContainsKey((ulong)data))
                                    {
                                        dataName = $"Supported Matter Cluster: {MatterV13Clusters.IdToName[(ulong)data]}";
                                    }
                                }
                                else
                                {
                                    if (MatterV13DeviceTypes.IdToName.ContainsKey((ulong)data))
                                    {
                                        dataName = $"Supported Matter Device Type: {MatterV13DeviceTypes.IdToName[(ulong)data]}";
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

        private void GetEndpointClusterAttributes(MatterTLV list)
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
                            break;

                        default:
                            throw new Exception($"Unexpected element type {elementType:X} for tag 0 in EndpointClusterAttributes response.");
                    }
                }

                if (list.IsTagNext(1))
                {
                    ulong nodeId = list.GetUnsignedInt(1);
                }

                if (list.IsTagNext(2))
                {
                    uint endpointId = (uint)list.GetUnsignedInt(2);
                }

                if (list.IsTagNext(3))
                {
                    uint clusterId = (uint)list.GetUnsignedInt(3);
                }

                if (list.IsTagNext(4))
                {
                    uint attributeId = (uint)list.GetUnsignedInt(4);
                }

                if (list.IsTagNext(5))
                {
                    ushort listIndex = (ushort)list.GetUnsignedInt(5);
                }

                if (list.IsTagNext(6))
                {
                    uint wildcardPathFlags = (uint)list.GetUnsignedInt(6);
                }
            }
        }
    }
}
