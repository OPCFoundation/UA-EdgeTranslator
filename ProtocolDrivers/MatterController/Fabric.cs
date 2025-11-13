using Newtonsoft.Json;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Matter.Core
{
    public class Fabric
    {
        public string FabricName { get; set; } = "FAB000000000001D";

        public ushort VendorId { get; set; } = 0xFFF1; // Default value from Matter specification

        public ConcurrentDictionary<ulong, Node> Nodes { get; set; }

        // Also called the EpochKey
        public byte[] IPK { get; set; }

        public byte[] OperationalIPK { get; set; }

        public ulong FabricId { get; set; }

        public byte[] CompressedFabricId { get; set; }

        public CertificateAuthority CA { get; set; }

        public ulong RootNodeId { get; set; }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Converters = {
                new X509Certificate2JsonConverter(),
                new ECDsaJsonConverter()
            },
            Formatting = Formatting.Indented
        };

        private Fabric()
        {
            // Empty private constructor for JSON serialization and to enforce use of Load method
        }

        private void Initialize()
        {
            IPK = RandomNumberGenerator.GetBytes(16);
            FabricId = BinaryPrimitives.ReadUInt64BigEndian(FabricName.ToByteArray());
            CA = new();
            CA.Initialize(FabricId);
            CompressedFabricId = CA.GenerateCompressedFabricId(FabricId);
            RootNodeId = BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(CA.RootCertSubjectKeyIdentifier, 0, 8));
            OperationalIPK = CA.KeyDerivationFunctionHMACSHA256(IPK, CompressedFabricId, Encoding.ASCII.GetBytes("GroupKey v1.0"), 16);
            Nodes = new();
        }

        public static Fabric Load()
        {
            Fabric fabric = null;
            try
            {
                fabric = JsonConvert.DeserializeObject<Fabric>(
                    File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric/settings.json")),
                    JsonSettings
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading fabric settings: {ex.Message}");
            }

            if (fabric == null)
            {
                Console.WriteLine("No existing fabric found, creating a new one.");
                fabric = new Fabric();
                fabric.Initialize();
                fabric.Save();
            }

            return fabric;
        }

        public void Save()
        {
            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric")))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric"));
            }

            File.WriteAllTextAsync(
                Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric/settings.json"),
                JsonConvert.SerializeObject(this, JsonSettings)
            );
        }

        public void AddNode(string id, string setupCode, string discriminator, byte[] operationalNOCAsTLV, ECDsa subjectPublicKey)
        {
            if (!ulong.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong nodeId))
            {
                Console.WriteLine($"Invalid node ID: {id.ToUpper()}!");
                return;
            }

            // remove any existing node
            if (Nodes.ContainsKey(nodeId))
            {
                Nodes.TryRemove(nodeId, out _);
            }

            Nodes.TryAdd(nodeId, new Node()
            {
                NodeId = nodeId,
                SetupCode = setupCode,
                Discriminator = discriminator,
                OperationalNOCAsTLV = operationalNOCAsTLV,
                SubjectPublicKey = subjectPublicKey
            });
        }

        public void UpdateNodeWithIPAddress(string id, string address, ushort port)
        {
            if (!ulong.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong nodeId))
            {
                Console.WriteLine($"Invalid node ID: {id.ToUpper()}!");
                return;
            }

            // check if the node exists
            if (!Nodes.ContainsKey(nodeId))
            {
                Console.WriteLine($"Node with ID: {id.ToUpper()} not found in Fabric!");
                return;
            }

            if (!string.IsNullOrEmpty(address))
            {
                Nodes[nodeId].LastKnownIpAddress = address;
            }

            if (port != 0)
            {
                Nodes[nodeId].LastKnownPort = port;
            }

            Console.WriteLine($"Added IP address {address} to existing Matter node {id.ToUpper()}.");
        }
    }
}
