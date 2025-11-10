using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using System;
using System.Buffers.Binary;
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

        public ECDsa RootKeyPair { get; }

        public X509Certificate2 RootCertificate { get; private set; }

        public byte[] RootCertSubjectKeyIdentifier { get; private set; }

        public ulong RCACIdentifier { get; private set; }

        public CertificateAuthority(ulong fabricId)
        {
            RootKeyPair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            RCACIdentifier = Math.Max(1, (ulong)Random.Shared.NextInt64());
            RootCertSubjectKeyIdentifier = GenerateUncompressedSHA1Hash(RootKeyPair);
            RootCertificate = GenerateRootCert(fabricId);
        }

        public X509Certificate2 GenerateRootCert(ulong fabricId)
        {
            X500DistinguishedNameBuilder builder = new X500DistinguishedNameBuilder();
            builder.Add("2.5.4.3", CommonName, UniversalTagNumber.UTF8String);
            builder.Add("1.3.6.1.4.1.37244.1.4", $"{RCACIdentifier:X16}", UniversalTagNumber.UTF8String);
            builder.Add("1.3.6.1.4.1.37244.1.5", $"{fabricId:X16}", UniversalTagNumber.UTF8String);

            CertificateRequest req = new CertificateRequest(builder.Build(), RootKeyPair, HashAlgorithmName.SHA256);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

            X509SubjectKeyIdentifierExtension subjectKeyIdentifierExtension = new X509SubjectKeyIdentifierExtension(RootCertSubjectKeyIdentifier, false);
            req.CertificateExtensions.Add(subjectKeyIdentifierExtension);
            req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(subjectKeyIdentifierExtension));

            return req.CreateSelfSigned(DateTime.Now.Subtract(TimeSpan.FromDays(1)), DateTime.Now.AddYears(10));
        }

        // workaround since .Net's CertificateRequest doesn't support empty extensions in CSRs
        public CertificateRequest ConvertCSR(Pkcs10CertificationRequest bcCertRequest)
        {
            if (!bcCertRequest.Verify())
            {
                Console.WriteLine("CSR signature invalid.");
                return null;
            }

            var bcPub = bcCertRequest.GetPublicKey();
            if (bcPub is not ECPublicKeyParameters ecPub)
            {
                Console.WriteLine("Expected EC key.");
                return null;
            }

            var q = ecPub.Q.Normalize();
            var ecParams = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = q.XCoord.GetEncoded(), Y = q.YCoord.GetEncoded() }
            };

            ECDsa ecdsaPub = ECDsa.Create(ecParams);
            var subjectDn = new X500DistinguishedName(bcCertRequest.GetCertificationRequestInfo().Subject.ToString());

            return new CertificateRequest(subjectDn, ecdsaPub, HashAlgorithmName.SHA256);
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

        public X509Certificate2 GenerateOperationalCert(ECDsa operationalKeyPair, ulong nodeId, ulong fabricId)
        {
            X500DistinguishedNameBuilder builder = new X500DistinguishedNameBuilder();
            builder.Add("1.3.6.1.4.1.37244.1.1", $"{nodeId:X16}", UniversalTagNumber.UTF8String);
            builder.Add("1.3.6.1.4.1.37244.1.5", $"{fabricId:X16}", UniversalTagNumber.UTF8String);

            CertificateRequest signingCSR = new CertificateRequest(builder.Build(), operationalKeyPair, HashAlgorithmName.SHA256);
            signingCSR.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            signingCSR.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            signingCSR.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], true));

#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            signingCSR.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(SHA1.HashData(GenerateUncompressed65ByteKey(operationalKeyPair)), false));
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms

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

            return encodedCert.Serialize();
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

        /// <summary>
        /// Generate the Compressed Fabric Identifier (CFI) per Matter:
        /// CFI = HKDF-Expand(PRK, info="CompressedFabric", L=8)
        /// Returns the 64-bit CFI as an unsigned integer.
        /// </summary>
        public byte[] GenerateCompressedFabricId(ulong fabricId)
        {
            byte[] rootPubKey65 = GenerateUncompressed65ByteKey(RootKeyPair);
            if (rootPubKey65 is null || rootPubKey65.Length != 65 || rootPubKey65[0] != 0x04)
            {
                throw new ArgumentException("Root public key must be 65 bytes uncompressed (0x04||X||Y).", nameof(rootPubKey65));
            }

            // IKM = X||Y (64 bytes), i.e., drop the 0x04 prefix.
            var ikm = new ReadOnlySpan<byte>(rootPubKey65, 1, 64);

            // salt = fabricId as 8-byte big-endian
            Span<byte> salt = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(salt, fabricId);

            return KeyDerivationFunctionHMACSHA256(ikm.ToArray(), salt.ToArray(), Encoding.ASCII.GetBytes("CompressedFabric"), 8);
        }

        // DestinationID = HMAC-SHA256(IPK, rand32 || rcac65 || fabricLE8 || nodeLE8)
        public byte[] GenerateDestinationId(byte[] ipk16, byte[] rand32, byte[] rcac65, ulong fabricId, ulong nodeId)
        {
            if (ipk16.Length != 16)
            {
                throw new ArgumentException("IPK must be 16 bytes");
            }

            if (rand32.Length != 32)
            {
                throw new ArgumentException("InitiatorRandom must be 32 bytes");
            }

            if (rcac65.Length != 65 || rcac65[0] != 0x04)
            {
                throw new ArgumentException("RCAC must be 65B uncompressed");
            }

            Span<byte> msg = stackalloc byte[32 + 65 + 8 + 8]; // random || root || fabricId || nodeId
            int i = 0;
            rand32.CopyTo(msg[i..]); i += 32;
            rcac65.CopyTo(msg[i..]); i += 65;
            BinaryPrimitives.WriteUInt64LittleEndian(msg[i..], fabricId); i += 8;
            BinaryPrimitives.WriteUInt64LittleEndian(msg[i..], nodeId);

            using var h = new HMACSHA256(ipk16);
            return h.ComputeHash(msg.ToArray()); // 32 bytes
        }

        public byte[] GenerateUncompressedSHA1Hash(ECDsa key)
        {
            // Extract the public key (EC P-256)
            ECParameters pubParams = key.ExportParameters(false);

            // Convert EC point to uncompressed format: 0x04 || X || Y
            byte[] publicKeyBytes = new byte[1 + pubParams.Q.X.Length + pubParams.Q.Y.Length];
            publicKeyBytes[0] = 0x04;
            Buffer.BlockCopy(pubParams.Q.X, 0, publicKeyBytes, 1, pubParams.Q.X.Length);
            Buffer.BlockCopy(pubParams.Q.Y, 0, publicKeyBytes, 1 + pubParams.Q.X.Length, pubParams.Q.Y.Length);

            // Compute SHA-1 hash
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            return SHA1.HashData(publicKeyBytes);
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms
        }

        public byte[] GenerateUncompressed65ByteKey(ECDsa key)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            // Export public parameters (no private material)
            ECParameters p = key.ExportParameters(false);

            // Validate we are on NIST P-256 (secp256r1)
            // .NET identifies named curves via Oid
            // secp256r1 is 1.2.840.10045.3.1.7
            if (p.Curve.Oid?.Value != "1.2.840.10045.3.1.7")
            {
                throw new NotSupportedException("Expected a P-256 public key (secp256r1).");
            }

            if (p.Q.X is null || p.Q.Y is null || p.Q.X.Length != 32 || p.Q.Y.Length != 32)
            {
                throw new InvalidOperationException("Unexpected P-256 public key coordinate lengths.");
            }

            // Assemble SEC1 uncompressed: 0x04 || X || Y
            byte[] uncompressed = new byte[65];
            uncompressed[0] = 0x04;
            Buffer.BlockCopy(p.Q.X, 0, uncompressed, 1, 32);
            Buffer.BlockCopy(p.Q.Y, 0, uncompressed, 33, 32);

            return uncompressed;
        }

        public ECDiffieHellman ImportEcdhPublic(byte[] pub65)
        {
            var x = new byte[32]; var y = new byte[32];
            Buffer.BlockCopy(pub65, 1, x, 0, 32);
            Buffer.BlockCopy(pub65, 33, y, 0, 32);

            var p = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } };
            var ecdh = ECDiffieHellman.Create();
            ecdh.ImportParameters(p);

            return ecdh;
        }

        public enum SigmaSaltVariant
        {
            // Salt = IPK || ResponderRandom || ResponderEphPub || SHA256(Sigma1) → used for Sigma2 key
            IpkConcat_TranscriptHash_S1,

            // Salt = IPK || SHA256(Sigma1 || Sigma2) → used for Sigma3 key
            IpkConcat_TranscriptHash_S1S2,

            // Salt = IPK || SHA256(Sigma1 || Sigma2 || Sigma3) → used for final session keys
            IpkConcat_TranscriptHash_S1S2S3
        }

        /// <summary>
        /// Builds the HKDF salt for CASE Sigma derivations.
        ///
        /// For RandomsConcat_* variants:
        ///   - initiatorRandom32 (Sigma1 tag-1) and responderRandom32 (Sigma2 tag-1) must be 32 bytes each.
        /// For IpkConcat_* variants:
        ///   - ipk16 must be 16 bytes; transcript segments MUST be the exact message bytes you (and peer) used.
        /// </summary>
        public byte[] SigmaSalt(
            SigmaSaltVariant variant,
            ReadOnlySpan<byte> responderRandom = default,
            ReadOnlySpan<byte> responderEphPub65 = default,
            ReadOnlySpan<byte> ipk16 = default,
            ReadOnlySpan<byte> sigma1Payload = default,
            ReadOnlySpan<byte> sigma2Payload = default,
            ReadOnlySpan<byte> sigma3Payload = default)
        {
            switch (variant)
            {
                case SigmaSaltVariant.IpkConcat_TranscriptHash_S1:
                    {
                        if (ipk16.Length != 16)
                        {
                            throw new ArgumentException("IPK must be 16 bytes.");
                        }

                        byte[] s1Hash = SHA256.HashData(sigma1Payload);
                        byte[] salt = new byte[ipk16.Length + responderRandom.Length + responderEphPub65.Length + s1Hash.Length];

                        int i = 0;
                        ipk16.CopyTo(salt.AsSpan(i)); i += ipk16.Length;
                        responderRandom.CopyTo(salt.AsSpan(i)); i += responderRandom.Length;
                        responderEphPub65.CopyTo(salt.AsSpan(i)); i += responderEphPub65.Length;
                        s1Hash.AsSpan().CopyTo(salt.AsSpan(i));

                        CryptographicOperations.ZeroMemory(s1Hash);

                        return salt;
                    }

                case SigmaSaltVariant.IpkConcat_TranscriptHash_S1S2:
                    {
                        if (ipk16.Length != 16)
                        {
                            throw new ArgumentException("IPK must be 16 bytes.");
                        }

                        byte[] th = Sha256Concat(sigma1Payload, sigma2Payload);

                        return Concat(ipk16, th);
                    }

                case SigmaSaltVariant.IpkConcat_TranscriptHash_S1S2S3:
                    {
                        if (ipk16.Length != 16)
                        {
                            throw new ArgumentException("IPK must be 16 bytes.");
                        }

                        byte[] th = Sha256Concat(sigma1Payload, sigma2Payload, sigma3Payload);

                        return Concat(ipk16, th);
                    }

                default:
                    throw new NotSupportedException($"Unknown salt variant: {variant}");
            }
        }

        public byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            byte[] r = new byte[a.Length + b.Length];

            a.CopyTo(r.AsSpan(0, a.Length));
            b.CopyTo(r.AsSpan(a.Length));

            return r;
        }

        private byte[] Sha256Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            using var sha = SHA256.Create();

            sha.TransformBlock(a.ToArray(), 0, a.Length, null, 0);
            sha.TransformFinalBlock(b.ToArray(), 0, b.Length);

            return sha.Hash;
        }

        private byte[] Sha256Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, ReadOnlySpan<byte> c)
        {
            using var sha = SHA256.Create();

            sha.TransformBlock(a.ToArray(), 0, a.Length, null, 0);
            sha.TransformBlock(b.ToArray(), 0, b.Length, null, 0);
            sha.TransformFinalBlock(c.ToArray(), 0, c.Length);

            return sha.Hash;
        }

        public byte[] KeyDerivationFunctionHMACSHA256(byte[] inputKeyMaterial, byte[] salt, byte[] info, int length)
        {
            byte[] pseudoRandomKey = new byte[HMACSHA256.HashSizeInBytes];
            HKDF.Extract(HashAlgorithmName.SHA256, inputKeyMaterial, salt, pseudoRandomKey);

            byte[] result = new byte[length];
            HKDF.Expand(HashAlgorithmName.SHA256, pseudoRandomKey, result, info);

            return result;
        }
    }
}
