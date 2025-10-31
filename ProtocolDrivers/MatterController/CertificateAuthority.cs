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

        /// <summary>
        /// Generate the Compressed Fabric Identifier (CFI) per Matter:
        /// CFI = HKDF-Expand(PRK, info="CompressedFabric", L=8)
        /// where PRK = HMAC-SHA256(salt=fabricId_be8, IKM=rootPubXY64).
        /// rootPubKey65 must be 0x04 || X(32) || Y(32).
        /// Returns the 64-bit CFI as an unsigned integer.
        /// </summary>
        public ulong GenerateCompressedFabricId(ulong fabricId)
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

            // HKDF-Extract: PRK = HMAC_SHA256(salt, IKM)
            byte[] prk;
            using (var hmac = new HMACSHA256(salt.ToArray()))
            {
                prk = hmac.ComputeHash(ikm.ToArray());
            }

            // HKDF-Expand to 8 bytes with info="CompressedFabric"
            byte[] info = Encoding.ASCII.GetBytes("CompressedFabric");
            Span<byte> t = stackalloc byte[32];  // T(1) buffer (HMAC-SHA256 output)
            using (var hmacExpand = new HMACSHA256(prk))
            {
                // T(1) = HMAC(PRK, T(0)||info||0x01)  (T(0) is empty)
                hmacExpand.TransformBlock(info, 0, info.Length, null, 0);
                hmacExpand.TransformFinalBlock([0x01], 0, 1);
                hmacExpand.Hash.CopyTo(t);
            }

            // CFI = first 8 bytes of T(1), treated as big-endian number
            return BinaryPrimitives.ReadUInt64BigEndian(t[..8]);
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

        public sealed record TrafficKeys(byte[] I2R, byte[] R2I, byte[] NoncePrefix);

        /// <summary>
        /// Derive the two unidirectional AES-CCM(128) traffic keys and the 8-byte nonce prefix
        /// for a CASE secure session.
        ///
        /// Inputs:
        ///   - Z:            ECDH shared secret (from initiator eph priv × responder eph pub)
        ///   - initRand32:   Sigma1 InitiatorRandom (32 bytes)
        ///   - respRand32:   Sigma2 ResponderRandom (32 bytes)
        ///   - initSID:      InitiatorSessionId (u16)
        ///   - respSID:      ResponderSessionId (u16)
        ///
        /// Output:
        ///   - (I2R 16B, R2I 16B, NoncePrefix 8B)
        /// </summary>
        public TrafficKeys DeriveCaseTrafficKeys(
            ReadOnlySpan<byte> Z,
            ReadOnlySpan<byte> initiatorRandom,
            ReadOnlySpan<byte> responderRandom,
            ushort initiatorSessionId,
            ushort responderSessionId)
        {
            if (Z.Length == 0)
            {
                throw new ArgumentException("ECDH shared secret (Z) is empty");
            }

            if ((initiatorRandom.Length != 32) || (responderRandom.Length != 32))
            {
                throw new ArgumentException("Handshake randoms must be 32 bytes each");
            }

            // HKDF-Extract: prk = HMAC_SHA256(salt, Z)
            // Salt ties the transcript's two 32B nonces together.
            // Many stacks use init||resp; some use resp||init; we compute both and prefer A.
            byte[] prkA = HkdfExtract(initiatorRandom, responderRandom, Z); // salt = init||resp
            byte[] prkB = HkdfExtract(responderRandom, initiatorRandom, Z); // salt = resp||init (fallback)

            // HKDF-Expand to get the three outputs (labels keep directions disjoint).
            // Labels are ASCII and include the two session IDs (LE) to namespace by session.
            Span<byte> sid4 = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(sid4, initiatorSessionId);
            BinaryPrimitives.WriteUInt16LittleEndian(sid4[2..], responderSessionId);

            byte[] iInfo = BuildInfo("SessionKeys v1 | I2R", sid4);
            byte[] rInfo = BuildInfo("SessionKeys v1 | R2I", sid4);
            byte[] nInfo = BuildInfo("SessionKeys v1 | NONCE", sid4);

            // First try with PRK-A; if you later see the peer dropping your first encrypted message,
            // flip to PRK-B via the toggle below.
            bool useAltSaltOrder = false; // set true if your device expects resp||init as salt
            ReadOnlySpan<byte> prk = useAltSaltOrder ? prkB : prkA;

            byte[] i2r = HkdfExpand(prk, iInfo, 16);
            byte[] r2i = HkdfExpand(prk, rInfo, 16);
            byte[] noncePrefix = HkdfExpand(prk, nInfo, 8);

            // Wipe temporary buffers that held PRKs
            CryptographicOperations.ZeroMemory(prkA);
            CryptographicOperations.ZeroMemory(prkB);

            return new TrafficKeys(i2r, r2i, noncePrefix);
        }

        public enum SigmaSaltVariant
        {
            // Salt = InitiatorRandom || ResponderRandom
            RandomsConcat_InitThenResp,

            // Salt = ResponderRandom || InitiatorRandom (some vendor forks)
            RandomsConcat_RespThenInit,

            // Salt = IPK || ResponderRandom || ResponderEphPub || SHA256(Sigma1)
            IpkConcat_TranscriptHash_S1,

            // Salt = IPK || SHA256(Sigma1 || Sigma2) → often used for Sigma3 key
            IpkConcat_TranscriptHash_S1S2,

            // Salt = IPK || SHA256(Sigma1 || Sigma2 || Sigma3) → often used for final session keys
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
            ReadOnlySpan<byte> initiatorRandom = default,
            ReadOnlySpan<byte> responderRandom = default,
            ReadOnlySpan<byte> responderEphPub65 = default,
            ReadOnlySpan<byte> ipk16 = default,
            ReadOnlySpan<byte> sigma1Payload = default,
            ReadOnlySpan<byte> sigma2Payload = default,
            ReadOnlySpan<byte> sigma3OuterTlv = default)
        {
            switch (variant)
            {
                case SigmaSaltVariant.RandomsConcat_InitThenResp:
                    {
                        if (initiatorRandom.Length != 32 || responderRandom.Length != 32)
                        {
                            throw new ArgumentException("Handshake randoms must be 32 bytes each.");
                        }

                        byte[] salt = new byte[64];
                        initiatorRandom.CopyTo(salt.AsSpan(0));
                        responderRandom.CopyTo(salt.AsSpan(32));

                        return salt;
                    }

                case SigmaSaltVariant.RandomsConcat_RespThenInit:
                    {
                        if (initiatorRandom.Length != 32 || responderRandom.Length != 32)
                        {
                            throw new ArgumentException("Handshake randoms must be 32 bytes each.");
                        }

                        byte[] salt = new byte[64];
                        responderRandom.CopyTo(salt.AsSpan(0));
                        initiatorRandom.CopyTo(salt.AsSpan(32));

                        return salt;
                    }

                case SigmaSaltVariant.IpkConcat_TranscriptHash_S1:
                    {
                        if (ipk16.Length != 16)
                        {
                            throw new ArgumentException("IPK must be 16 bytes.");
                        }

                        byte[] s1Hash = Sha256(sigma1Payload);
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

                        byte[] th = Sha256Concat(sigma1Payload, sigma2Payload, sigma3OuterTlv);

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

        private byte[] Sha256(ReadOnlySpan<byte> a)
        {
            using var sha = SHA256.Create();

            sha.TransformFinalBlock(a.ToArray(), 0, a.Length);

            return sha.Hash;
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

        /// <summary>
        /// Derive the 13-byte AES-CCM nonce for Sigma3's Encrypted3 (TBEData).
        /// Output: 13-byte nonce for AES-CCM (L = 2 → 15-L = 13).
        /// </summary>
        public byte[] BuildSigmaNonce(
            byte[] salt,
            ushort initiatorSessionId,
            ushort responderSessionId,
            string label,
            ReadOnlySpan<byte> Z)
        {
            if (salt.Length == 0)
            {
                throw new ArgumentException("Salt is empty.");
            }

            if (Z.Length == 0)
            {
                throw new ArgumentException("ECDH shared secret (Z) is empty.");
            }

            // HKDF-Extract: prk = HMAC_SHA256(salt, Z)
            byte[] prk;
            using (var h = new HMACSHA256(salt))
            {
                prk = h.ComputeHash(Z.ToArray());
            }

            // HKDF-Expand to 13 bytes with a Sigma2/3-specific label + both SIDs.
            byte[] info = BuildInfoLabel(label, initiatorSessionId, responderSessionId);
            byte[] nonce13 = HkdfExpand(prk, info, 13);

            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(prk);

            return nonce13;
        }

        public byte[] BuildInfoLabel(string asciiLabel, ushort initSID, ushort respSID)
        {
            byte[] label = Encoding.ASCII.GetBytes(asciiLabel);
            byte[] sid4 = new byte[4];

            BinaryPrimitives.WriteUInt16LittleEndian(sid4.AsSpan(0, 2), initSID);
            BinaryPrimitives.WriteUInt16LittleEndian(sid4.AsSpan(2, 2), respSID);

            byte[] info = new byte[label.Length + sid4.Length];
            Buffer.BlockCopy(label, 0, info, 0, label.Length);
            Buffer.BlockCopy(sid4, 0, info, label.Length, sid4.Length);

            return info;
        }

        public byte[] HkdfExtract(byte[] salt, byte[] ikm)
        {
            using var h = new HMACSHA256(salt);
            return h.ComputeHash(ikm);
        }

        public byte[] HkdfExtract(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y, ReadOnlySpan<byte> ikm)
        {
            byte[] salt = new byte[x.Length + y.Length];

            x.CopyTo(salt);
            y.CopyTo(salt.AsSpan(x.Length));

            using var hmac = new HMACSHA256(salt);
            return hmac.ComputeHash(ikm.ToArray());
        }

        public byte[] HkdfExpand(ReadOnlySpan<byte> prk32, ReadOnlySpan<byte> info, int len)
        {
            using var hmac = new HMACSHA256(prk32.ToArray());

            // single-block expand (len <= 32) is enough here
            hmac.TransformBlock(info.ToArray(), 0, info.Length, null, 0);
            hmac.TransformFinalBlock([0x01], 0, 1);

            byte[] T = hmac.Hash;

            var result = new byte[len];
            Buffer.BlockCopy(T, 0, result, 0, len);

            CryptographicOperations.ZeroMemory(T);

            return result;
        }

        public byte[] BuildInfo(string label, ReadOnlySpan<byte> sid4)
        {
            byte[] a = Encoding.ASCII.GetBytes(label);
            byte[] info = new byte[a.Length + sid4.Length];

            Buffer.BlockCopy(a, 0, info, 0, a.Length);
            sid4.CopyTo(info.AsSpan(a.Length));

            return info;
        }
    }
}
