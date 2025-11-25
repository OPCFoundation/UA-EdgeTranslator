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
        public sealed record DiscoverCommandsResult(byte Endpoint, ushort ClusterId, List<ushort> CommandIds);

        public string Name { get; set; }

        public ulong NodeId { get; set; }

        public byte[] OperationalNOCAsTLV { get; set; }

        public ECDsa SubjectPublicKey { get; set; }

        public string SetupCode { get; set; }

        public string Discriminator { get; set; }

        public string LastKnownIpAddress { get; set; }

        public ushort LastKnownPort { get; set; }

        private ISession _secureSession;

        private List<KeyValuePair<uint, ulong>> _supportedClusters = new();

        public bool Connect(Fabric fabric)
        {
            try
            {
                if (_secureSession != null)
                {
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
                    commandResponseMessageFrame = secureExchange.SendTimedCommandAsync(10000, 1, (uint)clusterId, ushort.Parse(commandName), parameters).GetAwaiter().GetResult();
                }
                else
                {
                    commandResponseMessageFrame = secureExchange.SendCommandAsync(1, (uint)clusterId, ushort.Parse(commandName), parameters).GetAwaiter().GetResult();
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
                            Console.WriteLine("Command response: 0x" + Convert.ToHexString(commandResponseMessageFrame.MessagePayload.ApplicationPayload.Serialize()));
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

        public string ReadAttribute(Fabric fabric, byte endpoint, string clusterName, uint attributeId)
        {
            // find our cluster ID
            ulong clusterId = MatterV13Clusters.IdToName.FirstOrDefault(x => x.Value.Equals(clusterName, StringComparison.OrdinalIgnoreCase)).Key;
            if (clusterId == 0)
            {
                return $"Cluster name '{clusterName}' not found.";
            }

            if (_secureSession == null)
            {
                return "Could not connect to Matter device!";
            }

            MessageExchange secureExchange = _secureSession.CreateExchange(fabric.RootNodeId, NodeId);

            var payload = new MatterTLV();
            payload.AddStructure();             // ReadRequestIB
            payload.AddArray(0);                // AttributePathList (tag 0)
            payload.AddList();                  // AttributePathIB
            payload.AddUInt16(2, endpoint);     // EndpointId
            payload.AddUInt64(3, clusterId);    // ClusterId
            payload.AddUInt32(4, attributeId);  // AttributeId
            payload.EndContainer();             // AttributePathIB
            payload.EndContainer();             // AttributePathList
            payload.AddBool(3, false);          // DataVersionFilterPresent
            payload.AddUInt8(255, 12);          // IM Revision
            payload.EndContainer();             // ReadRequestIB
            MessageFrame attributeReadResponse = secureExchange.SendAndReceiveMessageAsync(payload, 1, ProtocolOpCode.ReadRequest).GetAwaiter().GetResult();
            if (MessageFrame.IsError(attributeReadResponse) || (attributeReadResponse.MessagePayload.OpCode != ProtocolOpCode.ReportData))
            {
                Console.WriteLine("Received error status in response to read attribute message!");
                return "Read Attribute failed.";
            }

            secureExchange.AcknowledgeMessageAsync(attributeReadResponse.MessageCounter).GetAwaiter().GetResult();
            secureExchange.Close();

            object attributeValue = null;
            MatterTLV dataReport = attributeReadResponse.MessagePayload.ApplicationPayload;
            if (dataReport != null)
            {
                dataReport.OpenStructure();
                attributeValue = ReadDataReport(dataReport, false, false);
            }
            else
            {
                return "No payload returned.";
            }

            return attributeValue.ToString();
        }

        public string WriteAttribute(Fabric fabric, byte endpoint, string clusterName, uint attributeId, object attribute)
        {
            // find our cluster ID
            ulong clusterId = MatterV13Clusters.IdToName.FirstOrDefault(x => x.Value.Equals(clusterName, StringComparison.OrdinalIgnoreCase)).Key;
            if (clusterId == 0)
            {
                return $"Cluster name '{clusterName}' not found.";
            }

            if (_secureSession == null)
            {
                return "Could not connect to Matter device!";
            }

            MessageExchange secureExchange = _secureSession.CreateExchange(fabric.RootNodeId, NodeId);

            var payload = new MatterTLV();
            payload.AddStructure();
            payload.AddBool(0, false);              // SuppressResponse
            payload.AddBool(1, false);              // TimedRequest
            payload.AddArray(2);                    // WriteRequests
            payload.AddStructure();                 // AttributeDataIB
            payload.AddList(1);                     // AttributePath list
            payload.AddUInt16(2, endpoint);         // Endpoint
            payload.AddUInt64(3, clusterId);        // Cluster
            payload.AddUInt32(4, attributeId);      // Attribute
            payload.EndContainer();                 // End AttributePath list
            payload.AddObject(2, attribute);        // Data
            payload.EndContainer();                 // End AttributeDataIB
            payload.EndContainer();                 // End WriteRequest
            payload.AddUInt8(255, 12);              // IM revision
            payload.EndContainer();
            MessageFrame attributeWriteResponse = secureExchange.SendAndReceiveMessageAsync(payload, 1, ProtocolOpCode.WriteRequest).GetAwaiter().GetResult();
            if (MessageFrame.IsError(attributeWriteResponse))
            {
                Console.WriteLine("Received error status in response to write attribute message!");
                return "Write Attribute failed.";
            }

            secureExchange.AcknowledgeMessageAsync(attributeWriteResponse.MessageCounter).GetAwaiter().GetResult();
            secureExchange.Close();

            // simply print the reponse payload
            if (attributeWriteResponse.MessagePayload.ApplicationPayload != null)
            {
                Console.WriteLine("Attribute write response: 0x" + Convert.ToHexString(attributeWriteResponse.MessagePayload.ApplicationPayload.Serialize()));
            }

            return "success";
        }

        public async Task<string> FetchDescriptionsAsync(Fabric fabric)
        {
            MessageExchange secureExchange = null;

            try
            {
                if (_secureSession == null)
                {
                    return "session is null";
                }

                secureExchange = _secureSession.CreateExchange(fabric.RootNodeId, NodeId);

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
                    return "Send Request DeviceTypeList Attribute failed!";
                }

                await secureExchange.AcknowledgeMessageAsync(deviceTypeListResponse.MessageCounter).ConfigureAwait(false);

                bool moreChunkedMessages = false;
                do
                {
                    MatterTLV deviceTypeList = deviceTypeListResponse.MessagePayload.ApplicationPayload;
                    deviceTypeList.OpenStructure();
                    ReadDataReport(deviceTypeList, false, true);

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
                            return "Send RequestDeviceTypeList Attribute failed!";
                        }

                        await secureExchange.AcknowledgeMessageAsync(deviceTypeListResponse.MessageCounter).ConfigureAwait(false);
                    }
                }
                while (moreChunkedMessages);

                secureExchange.Close();
                secureExchange = _secureSession.CreateExchange(fabric.RootNodeId, NodeId);

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
                    return "Send Request ServerList Attribute failed!";
                }

                await secureExchange.AcknowledgeMessageAsync(serverListResponse.MessageCounter).ConfigureAwait(false);

                moreChunkedMessages = false;
                do
                {
                    MatterTLV serverList = serverListResponse.MessagePayload.ApplicationPayload;
                    serverList.OpenStructure();
                    ReadDataReport(serverList, true, true);

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
                            return "Send Request ServerList Attribute failed!";
                        }
                    }

                    await secureExchange.AcknowledgeMessageAsync(serverListResponse.MessageCounter).ConfigureAwait(false);
                }
                while (moreChunkedMessages);

                secureExchange.Close();

                return "success";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch descriptions from node {NodeId:X16}: {ex.Message}");

                if (secureExchange != null)
                {
                    secureExchange.Close();
                }

                return ex.Message;
            }
        }

        private object ReadDataReport(MatterTLV payload, bool isServerList, bool parseData)
        {
            object data;
            try
            {
                if (payload.IsTagNext(0))
                {
                    payload.GetObject(0); // status
                }

                if (payload.IsTagNext(1))
                {
                    payload.OpenArray(1); // attribute reports

                    uint endpointId = 0;
                    while (!payload.IsEndContainerNext())
                    {
                        payload.OpenStructure(); // attribute report

                        if (payload.IsTagNext(0))
                        {
                            payload.OpenStructure(0); // attribute paths

                            if (payload.IsTagNext(0))
                            {
                                payload.OpenList(0); // attribute path list

                                endpointId = GetEndpoint(payload);

                                payload.CloseContainer(); // attribute path list
                            }

                            if (payload.IsTagNext(1))
                            {
                                payload.GetObject(1); // attribute path wildcard flags
                            }

                            payload.CloseContainer(); // attribute paths
                        }

                        if (payload.IsTagNext(1))
                        {
                            payload.OpenStructure(1); // attribute data

                            ulong dataVersion = payload.GetUnsignedInt(0);

                            payload.OpenList(1); // attribute data list

                            endpointId = GetEndpoint(payload);

                            payload.CloseContainer(); // attribute data list

                            data = payload.GetObject(2);
                            if (parseData)
                            {
                                ParseData(isServerList, endpointId, data);
                            }
                            else
                            {
                                return data;
                            }

                            payload.CloseContainer(); // attribute data
                        }

                        payload.CloseContainer(); // attribute report
                    }

                    payload.CloseContainer(); // attribute reports
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing descriptions: {ex.Message}");
            }

            return null;
        }

        private void ParseData(bool isServerList, uint endpointId, object data)
        {
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
                                    if (!_supportedClusters.Contains(new KeyValuePair<uint, ulong>(endpointId, (ulong)innerItem)))
                                    {
                                        _supportedClusters.Add(new KeyValuePair<uint, ulong>(endpointId, (ulong)innerItem));
                                    }
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
                                if (!_supportedClusters.Contains(new KeyValuePair<uint, ulong>(endpointId, (ulong)item)))
                                {
                                    _supportedClusters.Add(new KeyValuePair<uint, ulong>(endpointId, (ulong)item));
                                }
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
                        if (!_supportedClusters.Contains(new KeyValuePair<uint, ulong>(endpointId, (ulong)data)))
                        {
                            _supportedClusters.Add(new KeyValuePair<uint, ulong>(endpointId, (ulong)data));
                        }
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
        }

        private uint GetEndpoint(MatterTLV payload)
        {
            uint endpointId = 0;

            while (!payload.IsEndContainerNext())
            {
                if (payload.IsTagNext(0))
                {
                    payload.GetObject(0);
                }

                if (payload.IsTagNext(1))
                {
                    ulong nodeId = payload.GetUnsignedInt(1);
                }

                if (payload.IsTagNext(2))
                {
                    endpointId = (uint)payload.GetUnsignedInt(2);
                }

                if (payload.IsTagNext(3))
                {
                    uint clusterId = (uint)payload.GetUnsignedInt(3);
                }

                if (payload.IsTagNext(4))
                {
                    uint attributeId = (uint)payload.GetUnsignedInt(4);
                }

                if (payload.IsTagNext(5))
                {
                    ushort listIndex = (ushort)payload.GetUnsignedInt(5);
                }

                if (payload.IsTagNext(6))
                {
                    uint wildcardPathFlags = (uint)payload.GetUnsignedInt(6);
                }
            }

            return endpointId;
        }
    }
}
