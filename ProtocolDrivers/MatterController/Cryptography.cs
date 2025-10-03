using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Matter.Core.Cryptography
{
    public class CryptographyMethods
    {
        private static Org.BouncyCastle.Math.EC.ECPoint M;
        private static Org.BouncyCastle.Math.EC.ECPoint N;

        public static (BigInteger w0, BigInteger w1, BigInteger x, Org.BouncyCastle.Math.EC.ECPoint X) Crypto_PAKEValues_Initiator(uint passcode, ushort iterations, byte[] salt)
        {
            // https://datatracker.ietf.org/doc/rfc9383/
            //
            var GROUP_SIZE_BYTES = 32;
            var CRYPTO_W_SIZE_BYTES = GROUP_SIZE_BYTES + 8;
            var CRYPTO_W_SIZE_BITS = CRYPTO_W_SIZE_BYTES * 8;

            var passcodeBytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(passcodeBytes, passcode);

            X9ECParameters curve = ECNamedCurveTable.GetByName("Secp256r1");

            if (curve == null)
            {
                throw new Exception("Couldn't find a curve");
            }

            var domainParameters = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());

            var pbkdf = Rfc2898DeriveBytes.Pbkdf2(passcodeBytes, salt, iterations, HashAlgorithmName.SHA256, 2 * CRYPTO_W_SIZE_BYTES);

            //Console.WriteLine("PBKDF2: {0}", Convert.ToBase64String(pbkdf));

            var w0s = new BigInteger(1, pbkdf.AsSpan().Slice(0, CRYPTO_W_SIZE_BYTES).ToArray(), true);
            var w1s = new BigInteger(1, pbkdf.AsSpan().Slice(CRYPTO_W_SIZE_BYTES, CRYPTO_W_SIZE_BYTES).ToArray(), true);

            var w0 = w0s.Mod(curve.N);
            var w1 = w1s.Mod(curve.N);

            BigInteger x = new BigInteger(1, RandomNumberGenerator.GetBytes(GROUP_SIZE_BYTES), true);

            M = curve.Curve.DecodePoint(Convert.FromHexString("02886e2f97ace46e55ba9dd7242579f2993b64e16ef3dcab95afd497333d8fa12f"));

            var X = curve.G.Multiply(x).Add(M.Multiply(w0)).Normalize();

            return (w0, w1, x, X);
        }

        internal static (byte[] Ke, byte[] hAY, byte[] hBX) Crypto_P2(byte[] contextHash, BigInteger w0, BigInteger w1, BigInteger x, Org.BouncyCastle.Math.EC.ECPoint X, byte[] Y)
        {
            X9ECParameters ecP = ECNamedCurveTable.GetByName("Secp256r1");

            var YPoint = ecP.Curve.DecodePoint(Y).Normalize();

            if (!YPoint.IsValid())
            {
                throw new InvalidOperationException("pC is not on the curve");
            }

            N = ecP.Curve.DecodePoint(Convert.FromHexString("03d8bbd6c639c62937b04d997f38c3770719c629d7014d49a24b4f98baa1292b49"));

            var yNwo = YPoint.Add(N.Multiply(w0).Negate());
            var Z = yNwo.Multiply(x);
            var V = yNwo.Multiply(w1);

            var Zs = BitConverter.ToString(Z.GetEncoded(false)).Replace("-", "");
            var Vs = BitConverter.ToString(V.GetEncoded(false)).Replace("-", "");

            //Console.WriteLine("Z: {0}", Zs);
            //Console.WriteLine("V: {0}", Vs);

            return ComputeSecretAndVerifiers(contextHash, w0, X, Y, Z, V);
        }

        private static (byte[] Ke, byte[] hAY, byte[] hBX) ComputeSecretAndVerifiers(byte[] contextHash, BigInteger w0, Org.BouncyCastle.Math.EC.ECPoint X, byte[] Y, Org.BouncyCastle.Math.EC.ECPoint Z, Org.BouncyCastle.Math.EC.ECPoint V)
        {
            var TT_HASH = ComputeTranscriptHash(contextHash, w0, X, Y, Z, V);

            var Ka = TT_HASH.AsSpan().Slice(0, 16).ToArray();
            var Ke = TT_HASH.AsSpan().Slice(16, 16).ToArray();

            byte[] salt = Array.Empty<byte>(); // Empty salt (Uint8Array(0))
            byte[] info = Encoding.ASCII.GetBytes("ConfirmationKeys");

            var hkdf = new HkdfBytesGenerator(new Sha256Digest());
            hkdf.Init(new HkdfParameters(Ka, salt, info));

            var KcAB = new byte[32];
            hkdf.GenerateBytes(KcAB, 0, 32);

            //Console.WriteLine("KcAB: {0}", BitConverter.ToString(KcAB));

            var KcA = KcAB.AsSpan().Slice(0, 16).ToArray();
            var KcB = KcAB.AsSpan().Slice(16, 16).ToArray();

            var hmac = new HMACSHA256(KcA);
            byte[] hAY = hmac.ComputeHash(Y);

            hmac = new HMACSHA256(KcB);
            byte[] hBX = hmac.ComputeHash(X.GetEncoded(false));

            //Console.WriteLine("hAY: {0}", BitConverter.ToString(hAY));
            //Console.WriteLine("hBX: {0}", BitConverter.ToString(hBX));

            return (Ke, hAY, hBX);
        }

        private static byte[] ComputeTranscriptHash(byte[] contextHash, BigInteger w0, Org.BouncyCastle.Math.EC.ECPoint X, byte[] Y, Org.BouncyCastle.Math.EC.ECPoint Z, Org.BouncyCastle.Math.EC.ECPoint V)
        {
            var memoryStream = new MemoryStream();
            var TTwriter = new BinaryWriter(memoryStream);

            AddToContext(TTwriter, contextHash);
            AddToContext(TTwriter, BitConverter.GetBytes((ulong)0), BitConverter.GetBytes((ulong)0));
            AddToContext(TTwriter, M.GetEncoded(false));
            AddToContext(TTwriter, N.GetEncoded(false));
            AddToContext(TTwriter, X.GetEncoded(false));
            AddToContext(TTwriter, Y);
            AddToContext(TTwriter, Z.GetEncoded(false));
            AddToContext(TTwriter, V.GetEncoded(false));
            AddToContext(TTwriter, w0.ToByteArrayUnsigned());

            TTwriter.Flush();

            var bytes = memoryStream.ToArray();

            //Console.WriteLine("Transcript: {0}", BitConverter.ToString(bytes));

            return SHA256.HashData(bytes);
        }

        private static void AddToContext(BinaryWriter TTwriter, byte[] data)
        {
            var lengthBytes = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(lengthBytes, (ulong)data.Length);

            TTwriter.Write(lengthBytes);
            TTwriter.Write(data);
        }

        private static void AddToContext(BinaryWriter TTwriter, byte[] length, byte[] data)
        {
            TTwriter.Write(length);
            TTwriter.Write(data);
        }
    }
}
