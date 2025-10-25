
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

        private const string _fabricName = "FAB000000000001D"; // must be 16 hex characters

        public FabricManager()
        {
            // TODO: Re-enable loading existing fabric
            //if (_storageProvider.DoesFabricExist())
            //{
            //    Fabric = _storageProvider.LoadFabricAsync(_fabricName).GetAwaiter().GetResult();
            //}
            //else
            {
                // create a new fabric

                var ipk = RandomNumberGenerator.GetBytes(16); // also called the EpochKey

                byte[] compressedFabricInfo = Encoding.ASCII.GetBytes("CompressedFabric");

                // Generate the CompressedFabricIdentifier using HKDF.
                var keyBytes = (CertificateAuthority.RootKeyPair.Public as ECPublicKeyParameters).Q.GetEncoded().AsSpan().Slice(1).ToArray();

                var hkdf = new HkdfBytesGenerator(new Sha256Digest());
                hkdf.Init(new HkdfParameters(keyBytes, _fabricName.ToByteArray(), compressedFabricInfo));

                var compressedFabricIdentifier = new byte[8];
                hkdf.GenerateBytes(compressedFabricIdentifier, 0, 8);

                // Generate the OperationalGroupKey(OperationalIPK) using HKDF.
                byte[] groupKey = Encoding.ASCII.GetBytes("GroupKey v1.0");
                hkdf.Init(new HkdfParameters(ipk, compressedFabricIdentifier, groupKey));

                var operationalIPK = new byte[16];
                hkdf.GenerateBytes(operationalIPK, 0, 16);

                byte[] rootNodeId = CertificateAuthority.RootCertSubjectKeyIdentifier.AsSpan().Slice(0, 8).ToArray();

                Fabric = new Fabric() {
                    FabricId = new BigInteger(_fabricName.ToByteArray(), true),
                    FabricName = _fabricName,
                    RootNodeId = new BigInteger(rootNodeId, true),
                    AdminVendorId = 0xFFF1, // Default value from Matter specification
                    RootCAKeyPair = CertificateAuthority.RootKeyPair,
                    RootCACertificateId = new BigInteger(CertificateAuthority.RootCertSubjectKeyIdentifier, true),
                    RootCACertificate = CertificateAuthority.GenerateRootCertificate(_fabricName),
                    RootKeyIdentifier = CertificateAuthority.RootCertSubjectKeyIdentifier,
                    IPK = ipk,
                    OperationalIPK = operationalIPK,
                    OperationalCertificate = CertificateAuthority.GenerateOperationalCertificate(Convert.ToHexString(rootNodeId), _fabricName),
                    OperationalCertificateKeyPair = CertificateAuthority.OperationalKeyPair,
                    CompressedFabricId = BitConverter.ToString(compressedFabricIdentifier).Replace("-", "")
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
