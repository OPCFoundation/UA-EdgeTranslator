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

                Console.WriteLine($"Established secure session to node {NodeId:X16}");
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
            MessageFrame readClusterMessageFrame = secureExchange.SendAndReceiveMessageAsync(readCluster, 1, 2).GetAwaiter().GetResult();
            if (MessageFrame.IsStatusReport(readClusterMessageFrame))
            {
                Console.WriteLine("Received error status report in response to ReadCluster message, abandoning FetchDescriptions!");
                return;
            }

            await secureExchange.AcknowledgeMessageAsync(readClusterMessageFrame.MessageCounter).ConfigureAwait(false);

            MatterTLV tlv = readClusterMessageFrame.MessagePayload.ApplicationPayload;
            Console.WriteLine($"Fetched DeviceTypeList attribute from node {NodeId:X16}: {BitConverter.ToString(tlv.GetBytes())}");

            secureExchange.Close();
        }
    }
}
