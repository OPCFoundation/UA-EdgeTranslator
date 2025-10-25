using Matter.Core.TLV;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Security.Cryptography;

namespace Matter.Core.Certificates
{
    public class CertificateAuthority
    {
        public static AsymmetricCipherKeyPair RootKeyPair {get; private set;} = GenerateKeyPair();

        public static AsymmetricCipherKeyPair OperationalKeyPair { get; private set; } = GenerateKeyPair();

#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
        public static byte[] RootCertSubjectKeyIdentifier { get; private set; } = SHA1.HashData((RootKeyPair.Public as ECPublicKeyParameters).Q.GetEncoded(false)).AsSpan().Slice(0, 20).ToArray();

        public static byte[] OperationalCertSubjectKeyIdentifier { get; private set; } = SHA1.HashData((OperationalKeyPair.Public as ECPublicKeyParameters).Q.GetEncoded(false)).AsSpan().Slice(0, 20).ToArray();
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

        public static byte[] RCACIdentifier { get; private set; } = SHA256.HashData((RootKeyPair.Public as ECPublicKeyParameters).Q.GetEncoded(false)).AsSpan().Slice(0, 8).ToArray();

        public static X509Certificate GenerateRootCertificate(string fabricId)
        {
            var generator = new X509V3CertificateGenerator();

            var subjectOids = new List<DerObjectIdentifier>
            {
                new DerObjectIdentifier("2.5.4.3"), // Common Name
                new DerObjectIdentifier("1.3.6.1.4.1.37244.1.4"), // RCAC
                new DerObjectIdentifier("1.3.6.1.4.1.37244.1.5") // FabricId
            };

            var subjectValues = new List<string>
            {
                "UA-EdgeTranslator",
                Convert.ToHexString(RCACIdentifier),
                $"{fabricId.PadLeft(16, '0').ToUpper():X16}"
            };

            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
            generator.SetSerialNumber(serialNumber);

            generator.SetPublicKey(RootKeyPair.Public);
            generator.SetSubjectDN(new X509Name(subjectOids, subjectValues));
            generator.SetIssuerDN(new X509Name(subjectOids, subjectValues)); // self-signed!

            generator.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            generator.SetNotAfter(DateTime.UtcNow.AddYears(10));

            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(true));
            generator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign));
            generator.AddExtension(X509Extensions.SubjectKeyIdentifier, false, new SubjectKeyIdentifier(RootCertSubjectKeyIdentifier));
            generator.AddExtension(X509Extensions.AuthorityKeyIdentifier, false, new AuthorityKeyIdentifier(RootCertSubjectKeyIdentifier));

            return generator.Generate(new Asn1SignatureFactory("SHA256WITHECDSA", RootKeyPair.Private));
        }

        public static X509Certificate GenerateOperationalCertificate(string nodeId, string fabricId)
        {
            var generator = new X509V3CertificateGenerator();

            var subjectOids = new List<DerObjectIdentifier>
            {
                new DerObjectIdentifier("1.3.6.1.4.1.37244.1.1"), // NodeId
                new DerObjectIdentifier("1.3.6.1.4.1.37244.1.5") // FabricId
            };

            var subjectValues = new List<string>
            {
                $"{nodeId.PadLeft(16, '0').ToUpper():X16}",
                $"{fabricId.PadLeft(16, '0').ToUpper():X16}"
            };

            var issuerOids = new List<DerObjectIdentifier>
            {
                new DerObjectIdentifier("2.5.4.3"), // Common Name
                new DerObjectIdentifier("1.3.6.1.4.1.37244.1.4"), // RCAC
                new DerObjectIdentifier("1.3.6.1.4.1.37244.1.5") // FabricId
            };

            var issuerValues = new List<string>
            {
                "UA-EdgeTranslator",
                Convert.ToHexString(RCACIdentifier),
                $"{fabricId.PadLeft(16, '0').ToUpper():X16}"
            };

            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
            generator.SetSerialNumber(serialNumber);

            generator.SetPublicKey(OperationalKeyPair.Public);
            generator.SetSubjectDN(new X509Name(subjectOids, subjectValues));
            generator.SetIssuerDN(new X509Name(issuerOids, issuerValues));

            generator.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            generator.SetNotAfter(DateTime.UtcNow.AddYears(10));

            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
            generator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature));
            generator.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(KeyPurposeID.id_kp_clientAuth, KeyPurposeID.id_kp_serverAuth));
            generator.AddExtension(X509Extensions.SubjectKeyIdentifier, false, new SubjectKeyIdentifier(OperationalCertSubjectKeyIdentifier));
            generator.AddExtension(X509Extensions.AuthorityKeyIdentifier, false, new AuthorityKeyIdentifier(RootCertSubjectKeyIdentifier));

            return generator.Generate(new Asn1SignatureFactory("SHA256WITHECDSA", RootKeyPair.Private));
        }

        public static X509Certificate SignCSR(string nodeId, string fabricId, Pkcs10CertificationRequest request, byte[] subjectKeyIdentifier)
        {
            var generator = new X509V3CertificateGenerator();

            var subjectOids = new List<DerObjectIdentifier>
            {
                new DerObjectIdentifier("1.3.6.1.4.1.37244.1.1"), // NodeId
                new DerObjectIdentifier("1.3.6.1.4.1.37244.1.5") // FabricId
            };

            var subjectValues = new List<string>
            {
                $"{nodeId.PadLeft(16, '0').ToUpper():X16}",
                $"{fabricId.PadLeft(16, '0').ToUpper():X16}"
            };

            var issuerOids = new List<DerObjectIdentifier>
            {
                new DerObjectIdentifier("2.5.4.3"), // Common Name
                new DerObjectIdentifier("1.3.6.1.4.1.37244.1.4"), // RCAC
                new DerObjectIdentifier("1.3.6.1.4.1.37244.1.5") // FabricId
            };

            var issuerValues = new List<string>
            {
                "UA-EdgeTranslator",
                Convert.ToHexString(RCACIdentifier),
                $"{fabricId.PadLeft(16, '0').ToUpper():X16}"
            };

            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
            generator.SetSerialNumber(serialNumber);

            generator.SetPublicKey(request.GetPublicKey());
            generator.SetSubjectDN(new X509Name(subjectOids, subjectValues));
            generator.SetIssuerDN(new X509Name(issuerOids, issuerValues));

            generator.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            generator.SetNotAfter(DateTime.UtcNow.AddYears(10));

            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
            generator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature));
            generator.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(KeyPurposeID.id_kp_clientAuth, KeyPurposeID.id_kp_serverAuth));
            generator.AddExtension(X509Extensions.SubjectKeyIdentifier, false, new SubjectKeyIdentifier(subjectKeyIdentifier));
            generator.AddExtension(X509Extensions.AuthorityKeyIdentifier, false, new AuthorityKeyIdentifier(RootCertSubjectKeyIdentifier));

            return generator.Generate(new Asn1SignatureFactory("SHA256WITHECDSA", RootKeyPair.Private));
        }

        public static byte[] GenerateCertMessage(ulong nodeId, ulong fabricId, byte[] subjectKeyIdentifier, X509Certificate cert, bool isRootCert)
        {
            MatterTLV encodedCert = new();

            encodedCert.AddStructure();

                encodedCert.AddOctetString(1, cert.SerialNumber.ToByteArrayUnsigned());
                encodedCert.AddUInt8(2, 1); // signature-algorithm

                encodedCert.AddList(3); // Issuer
                    encodedCert.AddUTF8String(1, "UA-EdgeTranslator"); // Common Name
                    encodedCert.AddUInt64(20, BinaryPrimitives.ReadUInt64LittleEndian(RCACIdentifier));
                    encodedCert.AddUInt64(21, fabricId);
                encodedCert.EndContainer();

                encodedCert.AddUInt32(4, new DateTimeOffset(cert.NotBefore).ToEpochTime());
                encodedCert.AddUInt32(5, new DateTimeOffset(cert.NotAfter).ToEpochTime());

                encodedCert.AddList(6); // Subject
            if (isRootCert)
            {
                    encodedCert.AddUTF8String(1, "UA-EdgeTranslator"); // Common Name
                    encodedCert.AddUInt64(20, BinaryPrimitives.ReadUInt64LittleEndian(RCACIdentifier));
                    encodedCert.AddUInt64(21, fabricId);
            }
            else
            {
                    encodedCert.AddUInt64(17, nodeId);
                    encodedCert.AddUInt64(21, fabricId);
            }
                encodedCert.EndContainer();

                encodedCert.AddUInt8(7, 1); // public-key-algorithm
                encodedCert.AddUInt8(8, 1); // elliptic-curve-id
                encodedCert.AddOctetString(9, (cert.GetPublicKey() as ECPublicKeyParameters).Q.GetEncoded(false));

                encodedCert.AddList(10); // Extensions
                    encodedCert.AddStructure(1); // Basic Constraints
                        encodedCert.AddBool(1, isRootCert); // is-ca
                    encodedCert.EndContainer(); // Close Basic Constraints

            if (isRootCert)
            {
                   encodedCert.AddUInt8(2, 0x60); // key usage CrlSign | KeyCertSign
            }
            else
            {
                    encodedCert.AddUInt8(2, 1); // key usage DigitalSignature

                    encodedCert.AddArray(3); // Extended Key Usage
                    encodedCert.AddUInt8(2); // clientAuth
                    encodedCert.AddUInt8(1); // serverAuth
                    encodedCert.EndContainer(); // Close Extended Key Usage
            }

            if (isRootCert)
            {
                    encodedCert.AddOctetString(4, RootCertSubjectKeyIdentifier); // subject-key-id
            }
            else
            {
                    encodedCert.AddOctetString(4, subjectKeyIdentifier); // subject-key-id
            }
                    encodedCert.AddOctetString(5, RootCertSubjectKeyIdentifier); // authority-key-id
                encodedCert.EndContainer();

                var certSignature = cert.GetSignature();
                AsnDecoder.ReadSequence(certSignature.AsSpan(), AsnEncodingRules.DER, out int offset, out int length, out _);
                byte[] source = certSignature.AsSpan().Slice(offset, length).ToArray();

                var r = AsnDecoder.ReadInteger(source, AsnEncodingRules.DER, out int bytesConsumed);
                var s = AsnDecoder.ReadInteger(source.AsSpan().Slice(bytesConsumed), AsnEncodingRules.DER, out _);

                byte[] rBytes = r.ToByteArray(isUnsigned: true, isBigEndian: true);
                byte[] sBytes = s.ToByteArray(isUnsigned: true, isBigEndian: true);

                rBytes = rBytes.Length < 32 ? Enumerable.Repeat((byte)0, 32 - rBytes.Length).Concat(rBytes).ToArray() : rBytes;
                sBytes = sBytes.Length < 32 ? Enumerable.Repeat((byte)0, 32 - sBytes.Length).Concat(sBytes).ToArray() : sBytes;

                byte[] signatureBytes = rBytes.Concat(sBytes).ToArray();

                encodedCert.AddOctetString(11, signatureBytes);

            encodedCert.EndContainer();

            return encodedCert.GetBytes();
        }

        public static AsymmetricCipherKeyPair GenerateKeyPair()
        {
            // Include the curve name in the key parameters (prime256v1)
            var ecParam = new DerObjectIdentifier("1.2.840.10045.3.1.7");

            var secureRandom = new SecureRandom();

            var keyParams = new ECKeyGenerationParameters(ecParam, secureRandom);

            var generator = new ECKeyPairGenerator("ECDSA");
            generator.Init(keyParams);

            var keyPair = generator.GenerateKeyPair();

            return keyPair;
        }
    }
}
