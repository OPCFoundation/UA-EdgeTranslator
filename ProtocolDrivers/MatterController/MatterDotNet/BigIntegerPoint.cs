// MatterDotNet Copyright (C) 2025
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or any later version.
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Affero General Public License for more details.
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using MatterDotNet.Security;
using System;
using System.Numerics;
using System.Security.Cryptography;

namespace MatterDotNet.Protocol.Cryptography
{
    internal struct BigIntegerPoint : IEquatable<BigIntegerPoint>
    {
        public BigIntegerPoint() { }
        public BigIntegerPoint(BigInteger x, BigInteger y)
        {
            X = x;
            Y = y;
        }
        public BigIntegerPoint(byte[] x, byte[] y)
        {
            ArgumentNullException.ThrowIfNull(x, nameof(x));
            ArgumentNullException.ThrowIfNull(y, nameof(y));
            X = new BigInteger(x, true, true);
            Y = new BigInteger(y, true, true);
        }

        /// <summary>
        /// Create a BigIntegerPoint from a .Net ECPoint
        /// </summary>
        /// <param name="ec"></param>
        public BigIntegerPoint(ECPoint ec) : this(ec.X!, ec.Y!) { }

        /// <summary>
        /// Create a big integer point from an ASN encoded byte array
        /// </summary>
        /// <param name="point"></param>
        /// <exception cref="ArgumentException"></exception>
        public BigIntegerPoint(byte[] point)
        {
            switch (point[0])
            {
                case 2:
                case 3:
                    X = new BigInteger(point.AsSpan(1), true, true);
                    BigInteger Y2 = (((BigInteger.ModPow(X, 3, SecP256.p) + ((SecP256.a * X) % SecP256.p)) % SecP256.p) + SecP256.b) % SecP256.p;
                    Y = BigIntUtil.ModSqrt(Y2, SecP256.p);
                    if (point[0] == 0x3)
                        Y = SecP256.p - Y;
                    break;
                case 4:
                    int len = (point.Length - 1) / 2;
                    X = new BigInteger(point.AsSpan(1, len), true, true);
                    Y = new BigInteger(point.AsSpan(len + 1, len), true, true);
                    break;
                default:
                    throw new ArgumentException("Invalid Point Type: " + point[0], nameof(point));
            }
        }

        public BigInteger X { get; set; }
        public BigInteger Y { get; set; }

        public byte[] ToBytes(bool compressed)
        {
            if (compressed)
            {
                byte[] ret = new byte[33];
                ret[0] = (byte)(0x2 + (Y % SecP256.p) % 2);
                X.TryWriteBytes(ret.AsSpan(1 + (32 - X.GetByteCount(true))), out _, true, true);
                return ret;
            }
            else
            {
                byte[] ret = new byte[65];
                ret[0] = 0x4;
                X.TryWriteBytes(ret.AsSpan(1 + (32 - X.GetByteCount(true))), out _, true, true);
                Y.TryWriteBytes(ret.AsSpan(33 + (32 - Y.GetByteCount(true))), out _, true, true);
                return ret;
            }
        }

        public ECPoint ToECPoint()
        {
            ECPoint p = new ECPoint();
            byte[] x = new byte[32];
            X.TryWriteBytes(x.AsSpan(32 - X.GetByteCount(true)), out _, true, true);
            p.X = x;
            byte[] y = new byte[32];
            Y.TryWriteBytes(y.AsSpan(32 - Y.GetByteCount(true)), out _, true, true);
            p.Y = y;
            return p;
        }

        public void Negate()
        {
            Y *= -1;
        }

        public bool Equals(BigIntegerPoint other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override string ToString()
        {
            return $"X: {X}, Y: {Y}";
        }
    }
}
