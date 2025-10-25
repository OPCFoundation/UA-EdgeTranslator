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

using System;
using System.Numerics;

namespace MatterDotNet.Protocol.Cryptography
{
    internal class SecP256
    {
        public static BigInteger a = new BigInteger(Convert.FromHexString("ffffffff00000001000000000000000000000000fffffffffffffffffffffffc"), true, true);
        public static BigInteger b = new BigInteger(Convert.FromHexString("5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b"), true, true);
        public static BigInteger p = new BigInteger(Convert.FromHexString("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF"), true, true);
        public static BigInteger n = new BigInteger(Convert.FromHexString("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"), true, true);
        static BigIntegerPoint ZERO = new BigIntegerPoint();
        public static BigIntegerPoint GeneratorP = new BigIntegerPoint(
                    Convert.FromHexString("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"),
                    Convert.FromHexString("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5")
                );

        public static BigInteger Invert(BigInteger number, BigInteger modulo)
        {
            return BigInteger.ModPow(number, modulo - 2, modulo);
        }
        public static BigIntegerPoint Double(BigIntegerPoint point)
        {
            if (point.Y == 0)
                return ZERO;
            BigInteger slope = (3 * BigInteger.ModPow(point.X, 2, p) + a) * Invert(2 * point.Y, p) % p;
            BigInteger x = (BigInteger.ModPow(slope, 2, p) - 2 * point.X) % p;
            BigInteger y = (slope * (point.X - x) - point.Y) % p;
            if (x < 0)
                x += p;
            if (y < 0)
                y += p;
            return new BigIntegerPoint(x, y);
        }
        public static BigIntegerPoint Add(BigIntegerPoint point1, BigIntegerPoint point2)
        {
            if (point1.Y == 0)
                return point2;
            if (point2.Y == 0)
                return point1;
            if (point1.X == point2.X)
            {
                if (point1.Y == point2.Y)
                    return Double(point1);
                return ZERO;
            }
            BigInteger slope = (point2.Y - point1.Y) % p * Invert(point2.X - point1.X, p) % p;
            BigInteger x = (BigInteger.ModPow(slope, 2, p) - point1.X - point2.X) % p;
            BigInteger y = (slope * (point1.X - x) - point1.Y) % p;
            if (x < 0)
                x += p;
            if (y < 0)
                y += p;
            return new BigIntegerPoint(x, y);
        }

        public static BigIntegerPoint Multiply(BigInteger k, BigIntegerPoint point)
        {
            BigIntegerPoint temp = point;
            BigIntegerPoint result = ZERO;
            while (k > 0)
            {
                if ((k & 1) == 1)
                    result = Add(result, temp);
                k >>= 1;
                temp = Double(temp);
            }
            return result;
        }
        public static bool IsOnCurve(BigIntegerPoint point)
        {
            BigInteger x = point.X % p;
            if (x < 0)
                x += p;
            BigInteger y = point.Y % p;
            if (y < 0)
                y += p;
            return BigInteger.ModPow(y, 2, p) == (BigInteger.ModPow(x, 3, p) + a * x + b) % p;
        }
    }
}
