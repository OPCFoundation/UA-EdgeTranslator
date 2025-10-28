
using Matter.Core.Certificates;
using MatterDotNet.Protocol.Cryptography;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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

            // Generate the CompressedFabricIdentifier using HKDF.
            byte[] compressedFabricInfo = Encoding.ASCII.GetBytes("CompressedFabric");
            var keyBytes = new BigIntegerPoint(CertificateAuthority.RootKeyPair.ExportParameters(false).Q).ToBytes(false).AsSpan().Slice(1).ToArray();

            var hkdf = new HkdfBytesGenerator(new Sha256Digest());
            hkdf.Init(new HkdfParameters(keyBytes, FabricName.ToByteArray(), compressedFabricInfo));

            var compressedFabricIdentifier = new byte[8];
            hkdf.GenerateBytes(compressedFabricIdentifier, 0, 8);

            // Generate the OperationalGroupKey(OperationalIPK) using HKDF.
            byte[] groupKey = Encoding.ASCII.GetBytes("GroupKey v1.0");
            hkdf.Init(new HkdfParameters(IPK, compressedFabricIdentifier, groupKey));

            OperationalIPK = new byte[16];
            hkdf.GenerateBytes(OperationalIPK, 0, 16);

            CompressedFabricId = BitConverter.ToString(compressedFabricIdentifier).Replace("-", "");
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
