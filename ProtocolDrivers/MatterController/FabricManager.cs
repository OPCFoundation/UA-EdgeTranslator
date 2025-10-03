
using Matter.Core.Certificates;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Matter.Core.Fabrics
{
    internal class FabricManager
    {
        public Fabric Fabric { get; private set; }

        private readonly FabricDiskStorage _storageProvider = new();

        public FabricManager()
        {
            if (_storageProvider.DoesFabricExist())
            {
                Fabric = _storageProvider.LoadFabricAsync().GetAwaiter().GetResult();
            }
            else
            {
                // create a new fabric

                var fabricIdBytes = "FAB000000000001D".ToByteArray();
                var fabricId = new BigInteger(fabricIdBytes, false);

                var rootCertificateIdBytes = "CACACACA00000001".ToByteArray();
                var rootCertificateId = new BigInteger(rootCertificateIdBytes, false);
                var rootNodeId = new BigInteger(rootCertificateIdBytes, false);

                var keyPair = CertificateAuthority.GenerateKeyPair();
                var rootCertificate = CertificateAuthority.GenerateRootCertificate(rootCertificateId, keyPair);

                var publicKey = rootCertificate.GetPublicKey() as ECPublicKeyParameters;
                var publicKeyBytes = publicKey.Q.GetEncoded(false);
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
                var rootKeyIdentifier = SHA1.HashData(publicKeyBytes).AsSpan().Slice(0, 20).ToArray();
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

                // Also called the EpochKey
                var ipk = RandomNumberGenerator.GetBytes(16);

                byte[] compressedFabricInfo = Encoding.ASCII.GetBytes("CompressedFabric");

                // Generate the CompressedFabricIdentifier using HKDF.
                var keyBytes = publicKey.Q.GetEncoded().AsSpan().Slice(1).ToArray();

                var hkdf = new HkdfBytesGenerator(new Sha256Digest());
                hkdf.Init(new HkdfParameters(keyBytes, fabricIdBytes, compressedFabricInfo));

                var compressedFabricIdentifier = new byte[8];
                hkdf.GenerateBytes(compressedFabricIdentifier, 0, 8);

                // Generate the OperationalGroupKey(OperationalIPK) using HKDF.
                byte[] groupKey = Encoding.ASCII.GetBytes("GroupKey v1.0");
                hkdf.Init(new HkdfParameters(ipk, compressedFabricIdentifier, groupKey));

                var operationalIPK = new byte[16];
                hkdf.GenerateBytes(operationalIPK, 0, 16);

                Console.WriteLine($"_fabric ID: {fabricId}");
                Console.WriteLine($"IPK: {BitConverter.ToString(ipk).Replace("-", "")}");

                var compressedFabicIdentifier = BitConverter.ToString(compressedFabricIdentifier).Replace("-", "");

                Console.WriteLine($"CompressedFabricIdentifier: {compressedFabicIdentifier}");
                Console.WriteLine($"OperationalIPK: {BitConverter.ToString(operationalIPK).Replace("-", "")}");

                var (noc, nocKeyPair) = Fabric.GenerateNOC(keyPair, rootKeyIdentifier);

                Fabric = new Fabric() {
                    FabricId = fabricId,
                    FabricName = "Fabric",
                    RootNodeId = rootNodeId,
                    AdminVendorId = 0xFFF1, // Default value from Matter specification
                    RootCAKeyPair = keyPair,
                    RootCACertificateId = rootCertificateId,
                    RootCACertificate = rootCertificate,
                    RootKeyIdentifier = rootKeyIdentifier,
                    IPK = ipk,
                    OperationalIPK = operationalIPK,
                    OperationalCertificate = noc,
                    OperationalCertificateKeyPair = nocKeyPair,
                    CompressedFabricId = compressedFabicIdentifier,
                };

                _storageProvider.SaveFabricAsync(Fabric).GetAwaiter().GetResult();
            }
        }

        internal void Save()
        {
            _storageProvider.SaveFabricAsync(Fabric).GetAwaiter().GetResult();
        }
    }
}
