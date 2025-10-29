using System;
using System.Formats.Asn1;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Matter.Core
{
    public class CertificateAuthority
    {
        public string CommonName { get; private set; } = "UA-EdgeTranslator";

        public ECDsa RootKeyPair { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public ECDsa OperationalKeyPair { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public X509Certificate2 RootCertificate { get; private set; }

        public byte[] RootCertSubjectKeyIdentifier { get; private set; }

        public ulong RCACIdentifier { get; private set; } = Math.Max(1, (ulong)Random.Shared.NextInt64());

        public CertificateAuthority(ulong fabricId)
        {
            // Extract the public key (EC P-256)
            ECParameters pubParams = RootKeyPair.ExportParameters(false);

            // Convert EC point to uncompressed format: 0x04 || X || Y
            byte[] publicKeyBytes = new byte[1 + pubParams.Q.X.Length + pubParams.Q.Y.Length];
            publicKeyBytes[0] = 0x04;
            Buffer.BlockCopy(pubParams.Q.X, 0, publicKeyBytes, 1, pubParams.Q.X.Length);
            Buffer.BlockCopy(pubParams.Q.Y, 0, publicKeyBytes, 1 + pubParams.Q.X.Length, pubParams.Q.Y.Length);

            // Compute SHA-1 hash
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            RootCertSubjectKeyIdentifier = SHA1.HashData(publicKeyBytes);
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

            RootCertificate = CreateRootCert(fabricId);
        }

        public X509Certificate2 CreateRootCert(ulong fabricId)
        {
            X500DistinguishedNameBuilder builder = new X500DistinguishedNameBuilder();

            builder.Add("2.5.4.3", CommonName, UniversalTagNumber.UTF8String);
            builder.Add("1.3.6.1.4.1.37244.1.4", $"{RCACIdentifier:X16}", UniversalTagNumber.UTF8String);
            builder.Add("1.3.6.1.4.1.37244.1.5", $"{fabricId:X16}", UniversalTagNumber.UTF8String);

            CertificateRequest req = new CertificateRequest(builder.Build(), RootKeyPair, HashAlgorithmName.SHA256);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

            X509SubjectKeyIdentifierExtension subjectKeyIdentifier = new X509SubjectKeyIdentifierExtension(RootCertSubjectKeyIdentifier, false);
            req.CertificateExtensions.Add(subjectKeyIdentifier);
            req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(subjectKeyIdentifier));

            return req.CreateSelfSigned(DateTime.Now.Subtract(TimeSpan.FromDays(1)), DateTime.Now.AddYears(10));
        }

        public X509Certificate2 SignCertRequest(CertificateRequest nocsr, ulong nodeId, ulong fabricId)
        {
            X500DistinguishedNameBuilder builder = new X500DistinguishedNameBuilder();

            builder.Add("1.3.6.1.4.1.37244.1.1", $"{nodeId:X16}", UniversalTagNumber.UTF8String);
            builder.Add("1.3.6.1.4.1.37244.1.5", $"{fabricId:X16}", UniversalTagNumber.UTF8String);

            CertificateRequest signingCSR = new CertificateRequest(builder.Build(), nocsr.PublicKey, HashAlgorithmName.SHA256);
            signingCSR.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            signingCSR.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            signingCSR.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], true));
            signingCSR.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(nocsr.PublicKey, false));
            signingCSR.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(RootCertificate, true, false));

            byte[] serial = new byte[19];
            Random.Shared.NextBytes(serial);

            return signingCSR.Create(RootCertificate, DateTime.Now.Subtract(TimeSpan.FromDays(1)), DateTime.Now.AddYears(1), serial);
        }

        public byte[] GenerateCertMessage(X509Certificate2 cert)
        {
            MatterTLV encodedCert = new();

            encodedCert.AddStructure();

            encodedCert.AddOctetString(1, cert.SerialNumberBytes.ToArray());

            encodedCert.AddUInt8(2, 1); // signature-algorithm

            encodedCert.AddList(3); // Issuer
            foreach (var dn in cert.IssuerName.EnumerateRelativeDistinguishedNames(false))
            {
                WriteDN(encodedCert, dn);
            }
            encodedCert.EndContainer();

            encodedCert.AddUInt32(4, new DateTimeOffset(cert.NotBefore).ToEpochTime());

            encodedCert.AddUInt32(5, new DateTimeOffset(cert.NotAfter).ToEpochTime());

            encodedCert.AddList(6); // Subject
            foreach (var dn in cert.SubjectName.EnumerateRelativeDistinguishedNames(false))
            {
                WriteDN(encodedCert, dn);
            }
            encodedCert.EndContainer();

            encodedCert.AddUInt8(7, 1); // public-key-algorithm

            encodedCert.AddUInt8(8, 1); // elliptic-curve-id

            encodedCert.AddOctetString(9, cert.GetPublicKey());

            encodedCert.AddList(10); // Extensions
            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext is X509BasicConstraintsExtension basic)
                {
                    encodedCert.AddStructure(1); // Basic Constraints
                    encodedCert.AddBool(1, basic.CertificateAuthority);
                    if (basic.HasPathLengthConstraint)
                    {
                        encodedCert.AddUInt8(2, (byte)basic.PathLengthConstraint);
                    }
                    encodedCert.EndContainer();
                }
                else if (ext is X509KeyUsageExtension keyUsage)
                {
                    encodedCert.AddUInt16(2, GetKeyUsage(keyUsage.KeyUsages));
                }
                else if (ext is X509EnhancedKeyUsageExtension extended)
                {
                    encodedCert.AddArray(3); // Extended Key Usage
                    foreach (Oid oid in extended.EnhancedKeyUsages)
                    {
                        switch (oid.Value)
                        {
                            case "1.3.6.1.5.5.7.3.1": // server auth
                                encodedCert.AddUInt8(1);
                                break;
                            case "1.3.6.1.5.5.7.3.2": // client auth
                                encodedCert.AddUInt8(2);
                                break;
                        }
                    }
                    encodedCert.EndContainer();
                }
                else if (ext is X509SubjectKeyIdentifierExtension subKey)
                {
                    encodedCert.AddOctetString(4, subKey.SubjectKeyIdentifierBytes.ToArray());
                }
                else if (ext is X509AuthorityKeyIdentifierExtension authKey)
                {
                    encodedCert.AddOctetString(5, authKey.KeyIdentifier.Value.ToArray());
                }
            }
            encodedCert.EndContainer();

            encodedCert.AddOctetString(11, CalculateCertSignature(cert));

            encodedCert.EndContainer();

            return encodedCert.GetBytes();
        }

        private ushort GetKeyUsage(X509KeyUsageFlags keyUsage)
        {
            ushort ret = 0x0;

            if ((keyUsage & X509KeyUsageFlags.DigitalSignature) != 0)
            {
                ret |= 0x1;
            }

            if ((keyUsage & X509KeyUsageFlags.KeyCertSign) != 0)
            {
                ret |= 0x20;
            }

            if ((keyUsage & X509KeyUsageFlags.CrlSign) != 0)
            {
                ret |= 0x40;
            }

            return ret;
        }

        private byte[] CalculateCertSignature(X509Certificate2 cert)
        {
            var signedData = cert.RawDataMemory;
            AsnDecoder.ReadSequence(signedData.Span, AsnEncodingRules.DER, out var offset, out var length, out _);

            var certificateSpan = signedData.Span.Slice(offset, length);
            AsnDecoder.ReadSequence(certificateSpan, AsnEncodingRules.DER, out var tbsOffset, out var tbsLength, out _);

            var algorithmSpan = certificateSpan.Slice(tbsOffset + tbsLength);
            AsnDecoder.ReadSequence(algorithmSpan, AsnEncodingRules.DER, out var algOffset, out var algLength, out _);

            byte[] signatureSequence = AsnDecoder.ReadBitString(algorithmSpan.Slice(algOffset + algLength), AsnEncodingRules.DER, out _, out _);
            AsnDecoder.ReadSequence(signatureSequence, AsnEncodingRules.DER, out var sigOffset, out int sigLength, out _);
            BigInteger r = AsnDecoder.ReadInteger(signatureSequence.AsSpan(sigOffset, sigLength), AsnEncodingRules.DER, out var intLen);
            BigInteger s = AsnDecoder.ReadInteger(signatureSequence.AsSpan(sigOffset + intLen), AsnEncodingRules.DER, out _);

            byte[] signature = new byte[64];
            byte[] part1bytes = r.ToByteArray(true, true);
            Array.Copy(part1bytes, 0, signature, 32 - part1bytes.Length, part1bytes.Length);

            byte[] part2bytes = s.ToByteArray(true, true);
            Array.Copy(part2bytes, 0, signature, 64 - part2bytes.Length, part2bytes.Length);

            return signature;
        }

        private void WriteDN(MatterTLV encodedCert, X500RelativeDistinguishedName dn)
        {
            switch (dn.GetSingleElementType().Value)
            {
                case "2.5.4.3": // CommonName
                    encodedCert.AddUTF8String(1, dn.GetSingleElementValue());
                    break;

                case "1.3.6.1.4.1.37244.1.1": // NodeId
                    if (ulong.TryParse(dn.GetSingleElementValue(), NumberStyles.HexNumber, null, out ulong id))
                    {
                        encodedCert.AddUInt64(17, id);
                    }
                    break;

                case "1.3.6.1.4.1.37244.1.4": // RCAC Identifier
                    if (ulong.TryParse(dn.GetSingleElementValue(), NumberStyles.HexNumber, null, out ulong rcac))
                    {
                        encodedCert.AddUInt64(20, rcac);
                    }
                    break;

                case "1.3.6.1.4.1.37244.1.5": // FabricId
                    if (ulong.TryParse(dn.GetSingleElementValue(), NumberStyles.HexNumber, null, out ulong fabricId))
                    {
                        encodedCert.AddUInt64(21, fabricId);
                    }
                    break;
            }
        }

        public byte[] GenerateOperationalIPK(byte[] sharedSecret, byte[] salt)
        {
            string info = "OperationalIdentityProtectionKey";
            int keyLen = 32;

            // Extract step
            using var hmac = new HMACSHA256(salt);
            byte[] prk = hmac.ComputeHash(sharedSecret);

            // Expand step
            byte[] okm = new byte[keyLen];
            byte[] previousBlock = Array.Empty<byte>();
            int generated = 0;
            int counter = 1;

            while (generated < keyLen)
            {
                using var hmacExpand = new HMACSHA256(prk);
                hmacExpand.TransformBlock(previousBlock, 0, previousBlock.Length, null, 0);
                hmacExpand.TransformBlock(Encoding.UTF8.GetBytes(info), 0, info.Length, null, 0);
                hmacExpand.TransformFinalBlock([(byte)counter], 0, 1);

                byte[] block = hmacExpand.Hash;
                int toCopy = Math.Min(block.Length, keyLen - generated);
                Array.Copy(block, 0, okm, generated, toCopy);

                generated += toCopy;
                previousBlock = block;
                counter++;
            }

            return okm;
        }
    }
}
