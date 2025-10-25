using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.X509;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;

namespace Matter.Core.Fabrics
{
    public class Fabric
    {
        public AsymmetricCipherKeyPair RootCAKeyPair { get; set; }

        public BigInteger RootCACertificateId { get; set; }

        public X509Certificate RootCACertificate { get; set; }

        public byte[] IPK { get; set; }

        public byte[] OperationalIPK { get; set; }

        public BigInteger RootNodeId { get; set; }

        public ushort AdminVendorId { get; set; }

        public byte[] RootKeyIdentifier { get; set; }

        public BigInteger FabricId { get; set; }

        public string FabricName { get; set; }

        public X509Certificate OperationalCertificate { get; set; }

        public AsymmetricCipherKeyPair OperationalCertificateKeyPair { get; set; }

        public List<Node> Nodes { get; } = new List<Node>();

        public string CompressedFabricId { get; set; }

        public byte[] RootPublicKeyBytes
        {
            get
            {
                var publicKey = RootCAKeyPair.Public as ECPublicKeyParameters;
                return publicKey!.Q.GetEncoded(false);
            }
        }

        internal void AddNodeAsync(string id, string address, ushort port)
        {
            Nodes.Add(new Node()
            {
                NodeId = BigInteger.ValueOf(int.Parse(id)),
                LastKnownIpAddress = IPAddress.Parse(address),
                LastKnownPort = port,
            });
        }

        internal Node CreateNode()
        {
            var nodeIdBytes = RandomNumberGenerator.GetBytes(8);
            var nodeId = new BigInteger(nodeIdBytes, true);

            return new Node()
            {
                NodeId = nodeId
            };
        }

        internal void AddNode(Node node)
        {
            node.Fabric = this;
            Nodes.Add(node);
        }
    }
}
