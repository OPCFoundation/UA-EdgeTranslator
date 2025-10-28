using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
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

        public byte[] OperationalIPK { get; set; }

        public string CompressedFabricId { get; set; }

        public Fabric()
        {
            IPK = RandomNumberGenerator.GetBytes(16);
            FabricId = BinaryPrimitives.ReadUInt64BigEndian(FabricName.ToByteArray());
            CA = new CertificateAuthority(FabricId);
            RootNodeId = BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(CA.RootCertSubjectKeyIdentifier, 0, 8));
            CompressedFabricId = ComputeCompressedFabricId(FabricId, CA.RootCertificate.GetPublicKey());
        }

        private string ComputeCompressedFabricId(ulong fabricId, byte[] rootPublicKey)
        {
            // Convert Fabric ID to big-endian bytes
            byte[] fabricIdBytes = BitConverter.GetBytes(fabricId);
            Array.Reverse(fabricIdBytes);

            // Salt = SHA1(rootPublicKey)
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            byte[] salt = SHA1.HashData(rootPublicKey);
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

            // Info = "CompressedFabric"
            byte[] info = Encoding.UTF8.GetBytes("CompressedFabric");

            // HKDF Extract
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            using var hmac = new HMACSHA1(salt);
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

            byte[] prk = hmac.ComputeHash(fabricIdBytes);

            // HKDF Expand
            byte[] okm = new byte[8]; // We only need 8 bytes
            byte[] t = Array.Empty<byte>();
            byte counter = 1;

#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            using var expandHmac = new HMACSHA1(prk);
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

            expandHmac.TransformBlock(info, 0, info.Length, null, 0);
            expandHmac.TransformBlock(new byte[] { counter }, 0, 1, null, 0);
            expandHmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            t = expandHmac.Hash;

            Array.Copy(t, okm, okm.Length);
            string compressedFabricId = BitConverter.ToString(okm).Replace("-", "");

            Console.WriteLine($"CompressedFabricId: {compressedFabricId}");

            return compressedFabricId;
        }

        public void AddNode(string id, string address, ushort port)
        {
            ulong nodeId = ulong.Parse(id, NumberStyles.HexNumber);
            bool success = Nodes.TryAdd(nodeId, new Node() {
                NodeId = nodeId,
                LastKnownIpAddress = IPAddress.Parse(address),
                LastKnownPort = port,
            });

            if (!success)
            {
                Console.WriteLine($"Node {id} already exists in fabric {FabricName}");
            }
        }
    }
}
