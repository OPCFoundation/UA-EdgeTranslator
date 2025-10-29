using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Matter.Core
{
    public class Fabric
    {
        public string FabricName { get; private set; } = "FAB000000000001D";

        public ushort VendorId { get; private set; } = 0xFFF1; // Default value from Matter specification

        public ConcurrentDictionary<ulong, Node> Nodes { get; private set; } = new();

        // Also called the EpochKey
        public byte[] IPK { get; private set; }

        public ulong FabricId { get; private set; }

        public CertificateAuthority CA { get; private set; }

        public ulong RootNodeId { get; private set; }

        public byte[] OperationalIPK { get; private set; }

        public X509Certificate2 OperationalCertificate { get; private set; }

        public Fabric()
        {
            IPK = RandomNumberGenerator.GetBytes(16);
            FabricId = BinaryPrimitives.ReadUInt64BigEndian(FabricName.ToByteArray());
            CA = new CertificateAuthority(FabricId);
            RootNodeId = BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(CA.RootCertSubjectKeyIdentifier, 0, 8));
            OperationalIPK = CA.GenerateOperationalIPK(IPK, BitConverter.GetBytes(FabricId).Reverse().ToArray());
            OperationalCertificate = CA.SignCertRequest(new CertificateRequest($"CN={RootNodeId:X16}", CA.OperationalKeyPair, HashAlgorithmName.SHA256), RootNodeId, FabricId);
        }

        public void AddOrUpdateNode(string id, string setupCode, string discriminator, string address, ushort port)
        {
            if (!ulong.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong nodeId))
            {
                Console.WriteLine($"Invalid node ID: {id}");
                return;
            }

            if (Nodes.ContainsKey(nodeId))
            {
                Nodes[nodeId].NodeId = nodeId;

                if (!string.IsNullOrEmpty(setupCode))
                {
                    Nodes[nodeId].SetupCode = setupCode;
                }

                if (!string.IsNullOrEmpty(discriminator)) {
                    Nodes[nodeId].Discriminator = discriminator;
                }

                if (!string.IsNullOrEmpty(address))
                {
                    Nodes[nodeId].LastKnownIpAddress = address != null ? IPAddress.Parse(address) : null;
                }

                if (port != 0)
                {
                    Nodes[nodeId].LastKnownPort = port;
                }

                Console.WriteLine($"Updated existing node with ID: {id}");
            }
            else
            {
                Nodes.TryAdd(nodeId, new Node()
                {
                    NodeId = nodeId,
                    SetupCode = setupCode,
                    Discriminator = discriminator,
                    LastKnownIpAddress = address != null ? IPAddress.Parse(address) : null,
                    LastKnownPort = port
                });

                Console.WriteLine($"Added new node with ID: {id}");
            }
        }
    }
}
