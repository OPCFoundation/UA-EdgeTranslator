using System;
using System.Linq;

namespace Matter.Core
{
    public static class Extensions
    {
        public static byte[] ToByteArray(this string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        public static uint ToEpochTime(this DateTimeOffset dt)
        {
            var epochStart = 946684800; // 2000-01-01T00:00:00Z
            return (uint)(dt.ToUnixTimeSeconds() - epochStart);
        }
    }
}
