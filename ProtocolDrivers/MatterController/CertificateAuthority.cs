using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Matter.Core.Certificates
{
    public class CertificateAuthority
    {
        public static X509Certificate GenerateRootCertificate(BigInteger rootCertificateId, AsymmetricCipherKeyPair keyPair)
        {
            var privateKey = keyPair.Private as ECPrivateKeyParameters;
            var publicKey = keyPair.Public as ECPublicKeyParameters;

            var rootCertId = new BigInteger("6479173750095827996");

#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            var rootKeyIdentifier = SHA1.HashData(publicKey.Q.GetEncoded(false)).AsSpan().Slice(0, 20).ToArray();
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

            var subjectOids = new List<DerObjectIdentifier>();
            var subjectValues = new List<string>();

            subjectOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.4"));
            subjectValues.Add($"CACACACA00000001");

            X509Name subjectDN = new X509Name(subjectOids, subjectValues);

            var issuerOids = new List<DerObjectIdentifier>();
            var issuerValues = new List<string>();

            issuerOids.Add(new DerObjectIdentifier("1.3.6.1.4.1.37244.1.4"));
            issuerValues.Add($"CACACACA00000001");

            X509Name issuerDN = new X509Name(issuerOids, issuerValues);

            var certificateGenerator = new X509V3CertificateGenerator();
            certificateGenerator.SetSerialNumber(rootCertId);
            certificateGenerator.SetPublicKey(publicKey);
            certificateGenerator.SetSubjectDN(subjectDN);
            certificateGenerator.SetIssuerDN(issuerDN);
            certificateGenerator.SetNotBefore(DateTime.UtcNow.AddYears(-1));
            certificateGenerator.SetNotAfter(DateTime.UtcNow.AddYears(10));
            certificateGenerator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(true));
            certificateGenerator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign));
            certificateGenerator.AddExtension(X509Extensions.SubjectKeyIdentifier, false, new SubjectKeyIdentifier(rootKeyIdentifier));
            certificateGenerator.AddExtension(X509Extensions.AuthorityKeyIdentifier, false, new AuthorityKeyIdentifier(rootKeyIdentifier));

            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA256WITHECDSA", privateKey);

            var rootCertificate = certificateGenerator.Generate(signatureFactory);

            return rootCertificate;
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
