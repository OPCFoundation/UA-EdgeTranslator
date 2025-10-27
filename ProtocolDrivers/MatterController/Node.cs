using Matter.Core.Fabrics;
using Matter.Core.Sessions;
using Matter.Core.TLV;
using Org.BouncyCastle.Math;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Matter.Core
{
    public class Node
    {
        public ISession _secureSession;

        public BigInteger NodeId { get; set; }

        public string NodeName => BitConverter.ToString(NodeId.ToByteArray().Reverse().ToArray()).Replace("-", "");

        public IPAddress LastKnownIpAddress { get; set; }

        public ushort LastKnownPort { get; set; }

        public Fabric Fabric { get; set; }

        public bool IsConnected { get; set; }

        public List<Endpoint> Endpoints { get; set; } = [];

        public async Task Connect()
        {
            try
            {
                IPAddress ipAddress = LastKnownIpAddress;
                ushort port = LastKnownPort;

                var connection = new UdpConnection(ipAddress, port);

                var unsecureSession = new UnsecureSession(connection);

                CASEClient client = new CASEClient(this, Fabric, unsecureSession);

                _secureSession = await client.EstablishSessionAsync();

                IsConnected = true;

                Console.WriteLine($"Established secure session to node {NodeId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to establish connection to node {NodeId}: {ex.Message}");
                IsConnected = false;
            }
        }

        public async Task FetchDescriptionsAsync()
        {
            if (!IsConnected)
            {
                return;
            }

            Endpoints = [];

            var exchange = _secureSession.CreateExchange();

            var readCluster = new MatterTLV();
            readCluster.AddStructure();

            readCluster.AddArray(tagNumber: 0);

            // Request the DeviceTypeList Attribute from the Description Cluster.
            //
            readCluster.AddList();
            readCluster.AddUInt32(tagNumber: 3, 0x1D); // ClusterId 0x1D - Description
            readCluster.AddUInt32(tagNumber: 4, 0x00); // Attribute 0x00 - DeviceTypeList
            readCluster.EndContainer(); // Close the list

            readCluster.EndContainer(); // Close the array

            readCluster.AddBool(tagNumber: 3, false);

            // Add the InteractionModelRevision number.
            //
            readCluster.AddUInt8(255, 12);

            readCluster.EndContainer();

            var readClusterMessagePayload = new MessagePayload(readCluster);

            readClusterMessagePayload.ExchangeFlags |= ExchangeFlags.Initiator;

            // Table 14. Protocol IDs for the Matter Standard Vendor ID
            readClusterMessagePayload.ProtocolId = 0x01; // IM Protocol Messages
            // From Table 18. Secure Channel Protocol Opcodes
            readClusterMessagePayload.ProtocolOpCode = 0x2; // ReadRequest

            var readClusterMessageFrame = new MessageFrame(readClusterMessagePayload);

            readClusterMessageFrame.MessageFlags |= MessageFlags.S;
            readClusterMessageFrame.SecurityFlags = 0x00;

            await exchange.SendAsync(readClusterMessageFrame);

            var readClusterResponseMessageFrame = await exchange.WaitForNextMessageAsync();

            await exchange.AcknowledgeMessageAsync(readClusterMessageFrame.MessageCounter);

            var resultPayload = readClusterResponseMessageFrame.MessagePayload;

            // Parse this into a set of endpoints.
            //
            var tlv = resultPayload.ApplicationPayload!;

            Console.WriteLine(tlv);

            //var reportData = new ReportDataAction(tlv);

            //foreach (var attributeReport in reportData.AttributeReports)
            //{
            //    Endpoint endpoint = new Endpoint(attributeReport.AttributeData.Path.EndpointId);

            //    var data = attributeReport.AttributeData.Data as List<object>;

            //    if (data is not null)
            //    {
            //        var deviceTypeList = data[0] as List<object>;

            //        if (deviceTypeList is not null)
            //        {
            //            endpoint.DeviceType = (ulong)deviceTypeList[0];
            //        }
            //    }

            //    Endpoints.Add(endpoint);
            //}

            exchange.Close();
        }
    }
}
