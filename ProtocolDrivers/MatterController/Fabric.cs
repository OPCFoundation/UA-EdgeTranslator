using Newtonsoft.Json;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
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

        public byte[] OperationalIPK { get; private set; }

        public ulong FabricId { get; private set; }

        public byte[] CompressedFabricId { get; private set; }

        public CertificateAuthority CA { get; private set; }

        public ulong RootNodeId { get; private set; }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Converters = {
                new X509Certificate2JsonConverter(),
                new ECDsaJsonConverter()
            }
        };

        private Fabric()
        {
            IPK = RandomNumberGenerator.GetBytes(16);
            FabricId = BinaryPrimitives.ReadUInt64BigEndian(FabricName.ToByteArray());
            CA = new CertificateAuthority(FabricId);
            CompressedFabricId = CA.GenerateCompressedFabricId(FabricId);
            RootNodeId = BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(CA.RootCertSubjectKeyIdentifier, 0, 8));
            OperationalIPK = CA.KeyDerivationFunctionHMACSHA256(IPK, CompressedFabricId, Encoding.ASCII.GetBytes("GroupKey v1.0"), 16);
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
                fabric.Save();
            }

            return fabric;
        }

        public void Save()
        {
            File.WriteAllTextAsync(
                Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric/settings.json"),
                JsonConvert.SerializeObject(this, JsonSettings)
            );
        }

        public void AddOrUpdateNode(string id, string setupCode, string discriminator, byte[] operationalNOCAsTLV, ECDsa subjectPublicKey, string address, ushort port)
        {
            if (!ulong.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong nodeId))
            {
                Console.WriteLine($"Invalid node ID: {id.ToUpper()}");
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

                if ((operationalNOCAsTLV != null) && (operationalNOCAsTLV.Length > 0))
                {
                    Nodes[nodeId].OperationalNOCAsTLV = operationalNOCAsTLV;
                }

                if (subjectPublicKey != null)
                {
                    Nodes[nodeId].SubjectPublicKey = subjectPublicKey;
                }

                if (!string.IsNullOrEmpty(address))
                {
                    Nodes[nodeId].LastKnownIpAddress = address != null ? IPAddress.Parse(address) : null;
                }

                if (port != 0)
                {
                    Nodes[nodeId].LastKnownPort = port;
                }

                Console.WriteLine($"Updated existing node with ID: {id.ToUpper()}");
            }
            else
            {
                Nodes.TryAdd(nodeId, new Node()
                {
                    NodeId = nodeId,
                    SetupCode = setupCode,
                    Discriminator = discriminator,
                    OperationalNOCAsTLV = operationalNOCAsTLV,
                    SubjectPublicKey = subjectPublicKey,
                    LastKnownIpAddress = address != null ? IPAddress.Parse(address) : null,
                    LastKnownPort = port
                });

                Console.WriteLine($"Added new node with ID: {id.ToUpper()}");
            }
        }
    }
}
