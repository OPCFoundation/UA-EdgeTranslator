
using Matter.Core.Certificates;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Matter.Core.Fabrics
{
    public class Fabric
    {
        public string FabricName { get; private set; } = "FAB000000000001D";

        public ushort VendorId { get; private set; } = 0xFFF1; // Default value from Matter specification

        public List<Node> Nodes { get; } = new List<Node>();

        // Also called the EpochKey
        public byte[] IPK { get; private set; }

        public ulong FabricId { get; private set; }

        public CertificateAuthority CA { get; private set; }

        public ulong RootNodeId { get; private set; }

        public byte[] OperationalIPK { get; set; }

        public string CompressedFabricId { get; set; }

        public Fabric()
        {
            IPK = RandomNumberGenerator.GetBytes(16);
            FabricId = BinaryPrimitives.ReadUInt64BigEndian(FabricName.ToByteArray());
            CA = new CertificateAuthority(FabricId);
            RootNodeId = BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(CA.RootCertSubjectKeyIdentifier, 0, 8));

            byte[] fabricIdBytes = BitConverter.GetBytes(FabricId).Reverse().ToArray();

            // HKDF with SHA-256
            using var hkdf = new HMACSHA256(CA.RootCertSubjectKeyIdentifier);
            byte[] info = Encoding.ASCII.GetBytes("CompressedFabric");

            // Step 1: Extract (salted)
            byte[] prk = hkdf.ComputeHash(fabricIdBytes);

            // Step 2: Expand
            byte[] t = new byte[prk.Length + info.Length + 1];
            Buffer.BlockCopy(info, 0, t, 0, info.Length);
            t[info.Length] = 0x01;
            byte[] okm = new HMACSHA256(prk).ComputeHash(t);

            // Take first 8 bytes
            byte[] compressedFabricId = new byte[8];
            Buffer.BlockCopy(okm, 0, compressedFabricId, 0, 8);


            CompressedFabricId = BitConverter.ToString(compressedFabricId).Replace("-", "");
        }

        public void AddNodeAsync(string id, string address, ushort port)
        {
            Nodes.Add(new Node()
            {
                NodeId = ulong.Parse(id),
                LastKnownIpAddress = IPAddress.Parse(address),
                LastKnownPort = port,
            });
        }

        public void AddNode(Node node)
        {
            node.Fabric = this;
            Nodes.Add(node);
        }
    }
}
