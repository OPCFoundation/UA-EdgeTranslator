using Matter.Core.Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;

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

        public delegate void NodeAddedToFabric(object sender, NodeAddedToFabricEventArgs args);
        public event NodeAddedToFabric NodeAdded;

        internal static (X509Certificate, AsymmetricCipherKeyPair) GenerateNOC(AsymmetricCipherKeyPair rootKeyPair, byte[] rootKeyIdentifier)
        {
            var keyPair = CertificateAuthority.GenerateKeyPair();

            var nocPublicKey = keyPair.Public as ECPublicKeyParameters;
            var nocPublicKeyBytes = nocPublicKey!.Q.GetEncoded(false);
            var nocKeyIdentifier = SHA1.HashData(nocPublicKeyBytes).AsSpan().Slice(0, 20).ToArray();

            var certGenerator = new X509V3CertificateGenerator();
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);

            var operationalId = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);

            certGenerator.SetSerialNumber(serialNumber);

            var subjectOids = new List<DerObjectIdentifier>();
            var subjectValues = new List<string>();

            subjectOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.1")); // NodeId
            subjectOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.5")); // FabricId
            subjectValues.Add($"CACACACA00000001");
            subjectValues.Add($"FAB000000000001D");

            X509Name subjectDN = new X509Name(subjectOids, subjectValues);

            certGenerator.SetSubjectDN(subjectDN);

            var issuerOids = new List<DerObjectIdentifier>();
            var issuerValues = new List<string>();

            issuerOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.4"));
            issuerValues.Add($"CACACACA00000001");

            X509Name issuerDN = new X509Name(issuerOids, issuerValues);

            certGenerator.SetIssuerDN(issuerDN); // The root certificate is the issuer.

            certGenerator.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            certGenerator.SetNotAfter(DateTime.UtcNow.AddYears(10));

            certGenerator.SetPublicKey(keyPair.Public as ECPublicKeyParameters);

            // Add the BasicConstraints and SubjectKeyIdentifier extensions
            //
            certGenerator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
            certGenerator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature));
            certGenerator.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(KeyPurposeID.id_kp_clientAuth, KeyPurposeID.id_kp_serverAuth));
            certGenerator.AddExtension(X509Extensions.SubjectKeyIdentifier, false, new SubjectKeyIdentifier(nocKeyIdentifier));
            certGenerator.AddExtension(X509Extensions.AuthorityKeyIdentifier, false, new AuthorityKeyIdentifier(rootKeyIdentifier));

            // Create a signature factory for the specified algorithm. Sign the cert with the RootCertificate PrivateyKey
            //
            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA256WITHECDSA", rootKeyPair.Private as ECPrivateKeyParameters);
            var noc = certGenerator.Generate(signatureFactory);

            return (noc, keyPair);
        }

        internal void AddCommissionedNodeAsync(BigInteger peerNodeId, System.Net.IPAddress address, ushort port)
        {
            Nodes.Add(new Node()
            {
                NodeId = peerNodeId,
                LastKnownIpAddress = address,
                LastKnownPort = port,
            });

            NodeAdded?.Invoke(this, new NodeAddedToFabricEventArgs()
            {
                NodeId = peerNodeId,
            });
        }

        internal Node CreateNode()
        {
            var nodeIdBytes = RandomNumberGenerator.GetBytes(8);
            var nodeId = new BigInteger(nodeIdBytes, false);

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

        public string GetFullNodeName(Node node)
        {
            // Specification 1.4 - 4.3.2.1.Operational Instance Name
            //
            return $"{CompressedFabricId}-{node.NodeId}";
        }
    }
}
