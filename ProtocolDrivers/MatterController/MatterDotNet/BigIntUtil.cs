/* 
*  Copyright (c) Microsoft Corporation. All rights reserved.
*  Licensed under the MIT License.
*/

using MatterDotNet.Protocol.Cryptography;
using System.Numerics;

namespace MatterDotNet.Security
{
    /// <summary>
    /// Big Integer Math
    /// </summary>
    public class BigIntUtil
    {
        private static BigIntegerPoint Mul(BigIntegerPoint a, BigIntegerPoint b, BigInteger p, BigInteger w2)
        {
            return new BigIntegerPoint((a.X * b.X + a.Y * b.Y * w2) % p, (a.X * b.Y + b.X * a.Y) % p);
        }

        /// <summary>
        /// Impplements Cipolla's algorithm
        /// </summary>
        /// <param name="N"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        public static BigInteger ModSqrt(BigInteger N, BigInteger p)
        {
            BigInteger a = 0,
                        w2 = 0;
            BigInteger ls = 0;

            // Pick up any value 'a' such that 'w^2 = a^2 - N' is a non-quadratic residue in Fp
            do
            {
                ++a;
                w2 = (a * a + p - N) % p;
                ls = BigInteger.ModPow(w2, (p - 1) / 2, p);
            } while (ls != p - 1);

            // In Fp^2, compute '(a + w)^((p + 1)/2) (mod p)'
            var r = new BigIntegerPoint(1, 0);
            var s = new BigIntegerPoint(a, 1);
            for (var n = (p + 1) / 2 % p; n > 0; n >>= 1)
            {
                if (!n.IsEven)
                    r = Mul(r, s, p, w2);
                s = Mul(s, s, p, w2);
            }

            // Check for errors
            if (r.X * r.X % p != N)
                return 0;
            if (r.Y != 0)
                return 0;

            return r.X;
        }

    }
}
