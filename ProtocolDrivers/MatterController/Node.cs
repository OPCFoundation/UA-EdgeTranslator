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

        public string WriteAttribute(Fabric fabric, byte endpoint, string clusterName, uint attributeId, uint attribute)
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
            payload.AddList();                      // WriteRequests
            payload.AddStructure();                 // AttributeDataIB
            payload.AddList(0);                     // AttributePath list
            payload.AddUInt16(0, endpoint);         // Endpoint
            payload.AddUInt64(1, clusterId);        // Cluster
            payload.AddUInt32(2, attributeId);      // Attribute
            payload.EndContainer();                 // End AttributePath list
            payload.AddUInt32(1, attribute);        // Data
            payload.EndContainer();                 // End AttributeDataIB
            payload.EndContainer();                 // End WriteRequest
            payload.AddUInt8(255, 12);              // IM revision
            payload.EndContainer();
            MessageFrame attributeResponse = secureExchange.SendAndReceiveMessageAsync(payload, 1, ProtocolOpCode.ReadRequest).GetAwaiter().GetResult();
            if (MessageFrame.IsError(attributeResponse))
            {
                Console.WriteLine("Received error status in response to write attribute message!");
                return "Write Attribute failed.";
            }

            secureExchange.AcknowledgeMessageAsync(attributeResponse.MessageCounter).GetAwaiter().GetResult();
            secureExchange.Close();

            // simply print the reponse payload
            if (attributeResponse.MessagePayload.ApplicationPayload != null)
            {
                Console.WriteLine("Attribute write response: 0x" + Convert.ToHexString(attributeResponse.MessagePayload.ApplicationPayload.Serialize()));
            }

            return "success";
        }

        static bool IsInteger(byte t) =>
            t == (byte)ElementType.SByte ||
            t == (byte)ElementType.Short ||
            t == (byte)ElementType.Int ||
            t == (byte)ElementType.Long ||
            t == (byte)ElementType.Byte ||
            t == (byte)ElementType.UShort ||
            t == (byte)ElementType.UInt ||
            t == (byte)ElementType.ULong;

        static ushort ReadU16(MatterTLV tlv, int? tagOrNull) => (ushort)tlv.GetUnsignedInt(tagOrNull);

        bool TryOpenAsListOrArray(MatterTLV tlv, int? tag, out bool openedList, out bool openedArray)
        {
            openedList = openedArray = false;
            byte t = tlv.PeekElementType();

            // Only attempt to open if the next element is a container
            if (t == (byte)ElementType.List)
            {
                try { tlv.OpenList(tag); openedList = true; return true; } catch { }
            }
            else if (t == (byte)ElementType.Array)
            {
                try { tlv.OpenArray(tag); openedArray = true; return true; } catch { }
            }

            return false; // not a container or open failed
        }

        bool TryOpenFieldsContainer(MatterTLV tlv, out bool openedFields)
        {
            openedFields = false;
            byte t = tlv.PeekElementType();

            if (t == (byte)ElementType.Structure)
            { try { tlv.OpenStructure(1); openedFields = true; } catch { } }
            else if (t == (byte)ElementType.List)
            { try { tlv.OpenList(1); openedFields = true; } catch { } }
            else if (t == (byte)ElementType.Array)
            { try { tlv.OpenArray(1); openedFields = true; } catch { } }

            return openedFields;
        }

        public async Task<string> DiscoverCommandsAsync(Fabric fabric, KeyValuePair<uint, ulong> cluster, bool accepted)
        {
            MessageExchange secureExchange = null;

            try
            {
                secureExchange = _secureSession.CreateExchange(fabric.RootNodeId, NodeId);

                var payload = new MatterTLV();
                payload.AddStructure();                     // top-level InvokeRequest
                payload.AddBool(0, false);                  // SuppressResponse
                payload.AddBool(1, false);                  // TimedRequest = false
                payload.AddArray(2);                        // InvokeRequests
                payload.AddStructure();                     // CommandDataIB
                payload.AddList(0);                         // CommandPath (list with three entries: endpoint, cluster, commandId)
                payload.AddUInt16(0, (ushort)cluster.Key);  // path[0] endpoint
                payload.AddUInt64(1, cluster.Value);        // path[1] cluster
                payload.AddUInt16(2, 0x12);                 // path[2] DiscoverCommandsRequest (IM-defined)
                payload.EndContainer();                     // end CommandPath
                payload.AddStructure(1);                    // CommandFields (structure)
                payload.AddBool(0, accepted);               // [0] isDiscoverAcceptedCommands
                payload.EndContainer();                     // end CommandFields
                payload.EndContainer();                     // end CommandDataIB
                payload.EndContainer();                     // end InvokeRequests
                payload.AddUInt8(255, 12);                  // IM revision (commonly 12)
                payload.EndContainer();                     // end top-level InvokeRequest
                MessageFrame response = await secureExchange.SendAndReceiveMessageAsync(payload, 1, ProtocolOpCode.InvokeRequest).ConfigureAwait(false);
                if (MessageFrame.IsError(response))
                {
                    Console.WriteLine("Received error status in response to discover commands message!");
                    return "Discover Commands failed.";
                }

                secureExchange.AcknowledgeMessageAsync(response.MessageCounter).GetAwaiter().GetResult();
                secureExchange.Close();

                var results = new List<DiscoverCommandsResult>();

                MatterTLV tlv = response.MessagePayload.ApplicationPayload;
                tlv.OpenStructure(); // InvokeResponse

                while (!tlv.IsEndContainerNext())
                {
                    int? topTag = tlv.PeekTagNumber();
                    byte topType = tlv.PeekElementType();

                    // Locate InvokeResponses container (List OR Array). Skip non-target top-level fields.
                    if (!TryOpenAsListOrArray(tlv, topTag, out var openedList, out var openedArray))
                    {
                        tlv.GetObject(topTag);
                        continue;
                    }

                    // Walk InvokeResponses items
                    while (!tlv.IsEndContainerNext())
                    {
                        tlv.OpenStructure(); // CommandDataIB

                        var cmdIds = new List<ushort>();

                        while (!tlv.IsEndContainerNext())
                        {
                            int? innerTag = tlv.PeekTagNumber();
                            byte innerType = tlv.PeekElementType();

                            if (innerTag == 0) // CommandPath
                            {
                                bool openedPath = false;
                                // CommandPath is usually a LIST; some stacks use STRUCTURE. Try both safely.
                                try { tlv.OpenList(0); openedPath = true; }
                                catch
                                {
                                    try { tlv.OpenStructure(0); openedPath = true; }
                                    catch { /* neither -> consume */ }
                                }

                                if (!openedPath)
                                {
                                    tlv.GetObject(0);
                                }
                                else
                                {
                                    while (!tlv.IsEndContainerNext())
                                    {
                                        int? pathTag = tlv.PeekTagNumber();
                                        if (pathTag == 0) tlv.GetUnsignedInt(0);
                                        else if (pathTag == 1) tlv.GetUnsignedInt(1);
                                        else tlv.GetObject(pathTag); // command (2) or vendor fields
                                    }

                                    // Only close if we really opened it
                                    if (tlv.IsEndContainerNext()) tlv.CloseContainer();
                                }
                            }
                            else if (innerTag == 1) // CommandFields: DiscoverCommands payload lives here
                            {
                                // Try to open as container; if not, consume primitive/empty and continue
                                if (!TryOpenFieldsContainer(tlv, out var openedFields))
                                {
                                    tlv.GetObject(1);
                                    continue;
                                }

                                // Inside CommandFields, there are two common shapes:
                                // (A) a child List/Array (often tag 0) holding anonymous/context-tagged integers (IDs)
                                // (B) repeated integer fields directly under CommandFields (often tag 0), no child container

                                bool openedIdsContainer = false;

                                if (!tlv.IsEndContainerNext())
                                {
                                    int? maybeIdsTag = tlv.PeekTagNumber();
                                    byte maybeIdsType = tlv.PeekElementType();

                                    // If first child is a container, open it and read IDs from it
                                    if (maybeIdsType == (byte)ElementType.List || maybeIdsType == (byte)ElementType.Array)
                                    {
                                        openedIdsContainer = TryOpenAsListOrArray(tlv, maybeIdsTag, out var idsList, out var idsArray);
                                        if (openedIdsContainer)
                                        {
                                            while (!tlv.IsEndContainerNext())
                                            {
                                                // Items can be anonymous or context-tagged ints
                                                ushort cmdId = ReadU16(tlv, null);
                                                cmdIds.Add(cmdId);
                                            }
                                            if (tlv.IsEndContainerNext()) tlv.CloseContainer(); // child IDs container
                                        }
                                    }
                                }

                                if (!openedIdsContainer)
                                {
                                    // No child container → consume integer primitives directly under CommandFields
                                    while (!tlv.IsEndContainerNext())
                                    {
                                        int? fTag = tlv.PeekTagNumber();
                                        byte fType = tlv.PeekElementType();

                                        if (!IsInteger(fType))
                                        {
                                            tlv.GetObject(fTag); // skip non-integer field (vendor extras)
                                            continue;
                                        }

                                        // Honour tag 0 if present; anonymous also works with null
                                        ushort cmdId = ReadU16(tlv, fTag == 0 ? 0 : (int?)null);
                                        cmdIds.Add(cmdId);
                                    }
                                }

                                // Close CommandFields ONLY if we opened it and EndOfContainer is indeed next
                                if (tlv.IsEndContainerNext()) tlv.CloseContainer();
                            }
                            else
                            {
                                // Unknown field inside CommandDataIB → consume safely
                                tlv.GetObject(innerTag);
                            }
                        }

                        // Close CommandDataIB
                        if (tlv.IsEndContainerNext()) tlv.CloseContainer();

                        if (cmdIds.Count > 0)
                        {
                            results.Add(new DiscoverCommandsResult((byte)cluster.Key, (ushort)cluster.Value, cmdIds));
                        }
                    }

                    // Close InvokeResponses container
                    if (tlv.IsEndContainerNext()) tlv.CloseContainer();
                }

                // Close top-level InvokeResponse
                if (tlv.IsEndContainerNext()) tlv.CloseContainer();

                foreach (var r in results)
                {
                    Console.WriteLine($"Cluster 0x{r.ClusterId:X4} @ endpoint {r.Endpoint} supports (accepted: {accepted}):");
                    foreach (var id in r.CommandIds)
                    {
                        Console.WriteLine($"  - 0x{id:X4}");
                    }
                }

                return "success";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to discover commands from node {NodeId:X16}: {ex.Message}");

                if (secureExchange != null)
                {
                    secureExchange.Close();
                }

                return ex.Message;
            }
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
                    ParseDescriptions(fabric, deviceTypeList, false);

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
                    ParseDescriptions(fabric, serverList, true);

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

                foreach (var clusterId in _supportedClusters)
                {
                    string result = await DiscoverCommandsAsync(fabric, clusterId, true).ConfigureAwait(false);
                    if (result != "success")
                    {
                        return result;
                    }

                    result = await DiscoverCommandsAsync(fabric, clusterId, false).ConfigureAwait(false);
                    if (result != "success")
                    {
                        return result;
                    }
                }

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

        private void ParseDescriptions(Fabric fabric, MatterTLV list, bool isServerList)
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

                    uint endpointId = 0;
                    while (!list.IsEndContainerNext())
                    {
                        list.OpenStructure(); // attribute report

                        if (list.IsTagNext(0))
                        {
                            list.OpenStructure(0); // attribute paths

                            if (list.IsTagNext(0))
                            {
                                list.OpenList(0); // attribute path list

                                endpointId = GetEndpointClusterAttributes(list);

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

                            endpointId = GetEndpointClusterAttributes(list);

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

        private uint GetEndpointClusterAttributes(MatterTLV list)
        {
            uint endpointId = 0;

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
                    endpointId = (uint)list.GetUnsignedInt(2);
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

            return endpointId;
        }
    }
}
