using Matter.Core.Certificates;
using Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Matter.Core.Fabrics
{
    internal class FabricManager
    {
        private readonly IFabricStorageProvider _storageProvider;

        public FabricManager(IFabricStorageProvider storageProvider)
        {
            _storageProvider = storageProvider;
        }

        public async Task<Fabric> GetAsync(string fabricName)
        {
            Fabric fabric = null;

            if (_storageProvider.DoesFabricExist(fabricName))
            {
                fabric = await _storageProvider.LoadFabricAsync(fabricName);
            }
            else
            {
                var fabricIdBytes = "FAB000000000001D".ToByteArray();
                var fabricId = new BigInteger(fabricIdBytes, false);

                var rootCertificateIdBytes = "CACACACA00000001".ToByteArray();
                var rootCertificateId = new BigInteger(rootCertificateIdBytes, false);
                var rootNodeId = new BigInteger(rootCertificateIdBytes, false);

                var keyPair = CertificateAuthority.GenerateKeyPair();
                var rootCertificate = CertificateAuthority.GenerateRootCertificate(rootCertificateId, keyPair);

                // TODO I'm doing this twice; here and in GenerateRootCertificate()
                //
                var publicKey = rootCertificate.GetPublicKey() as ECPublicKeyParameters;
                var publicKeyBytes = publicKey.Q.GetEncoded(false);
                var rootKeyIdentifier = SHA1.HashData(publicKeyBytes).AsSpan().Slice(0, 20).ToArray();

                // Also called the EpochKey
                //
                var ipk = RandomNumberGenerator.GetBytes(16);

                byte[] compressedFabricInfo = Encoding.ASCII.GetBytes("CompressedFabric");

                // Generate the CompressedFabricIdentifier using HKDF.
                //
                var keyBytes = publicKey.Q.GetEncoded().AsSpan().Slice(1).ToArray();

                var hkdf = new HkdfBytesGenerator(new Sha256Digest());
                hkdf.Init(new HkdfParameters(keyBytes, fabricIdBytes, compressedFabricInfo));

                var compressedFabricIdentifier = new byte[8];
                hkdf.GenerateBytes(compressedFabricIdentifier, 0, 8);

                // Generate the OperationalGroupKey(OperationalIPK) using HKDF.
                //
                byte[] groupKey = Encoding.ASCII.GetBytes("GroupKey v1.0");
                hkdf.Init(new HkdfParameters(ipk, compressedFabricIdentifier, groupKey));

                var operationalIPK = new byte[16];
                hkdf.GenerateBytes(operationalIPK, 0, 16);

                Console.WriteLine($"Fabric ID: {fabricId}");
                Console.WriteLine($"IPK: {BitConverter.ToString(ipk).Replace("-", "")}");

                var compressedFabicIdentifier = BitConverter.ToString(compressedFabricIdentifier).Replace("-", "");

                Console.WriteLine($"CompressedFabricIdentifier: {compressedFabicIdentifier}");
                Console.WriteLine($"OperationalIPK: {BitConverter.ToString(operationalIPK).Replace("-", "")}");

                var (noc, nocKeyPair) = Fabric.GenerateNOC(keyPair, rootKeyIdentifier);

                fabric = new Fabric()
                {
                    FabricId = fabricId,
                    FabricName = fabricName,
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

                await _storageProvider.SaveFabricAsync(fabric);
            }

            fabric.NodeAdded += Fabric_NodeAdded;

            return fabric!;
        }

        private async void Fabric_NodeAdded(object sender, NodeAddedToFabricEventArgs args)
        {
            await _storageProvider.SaveFabricAsync(sender as Fabric);
        }
    }
}
