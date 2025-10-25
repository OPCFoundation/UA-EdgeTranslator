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
//
// WARNING: This file was auto-generated. Do not edit.

namespace MatterDotNet.Util
{
    using System;

    public static class TimeUtil
    {
        const long EPOCH_S = 946684800;
        const long EPOCH_MS = 946684800000;

        /// <summary>
        /// Returns the Epoch Zero time
        /// </summary>
        public static DateTime EPOCH { get { return FromEpochSeconds(0)!.Value; } }

        /// <summary>
        /// Returns the latest date
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static DateTime Max(DateTime a, DateTime b)
        {
            if (a < b)
                return b;
            else
                return a;
        }

        /// <summary>
        /// Returns the earliest date
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static DateTime Min(DateTime a, DateTime b)
        {
            if (a < b)
                return a;
            else
                return b;
        }

        /// <summary>
        /// Convert a DateTime to Epoch Seconds
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static uint ToEpochSeconds(DateTime date)
        {
            return (uint)(((DateTimeOffset)date).ToUnixTimeSeconds() - EPOCH_S);
        }

        /// <summary>
        /// Convert a DateTime to Epoch uS
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static ulong ToEpochUS(DateTime date)
        {
            return (ulong)(((DateTimeOffset)date).ToUnixTimeMilliseconds() - EPOCH_MS) * 1000;
        }

        /// <summary>
        /// Convert an Epoch Seconds to DateTime
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static DateTime? FromEpochSeconds(uint? date)
        {
            if (date == null)
                return null;
            long unix = (long)date + EPOCH_S;
            return DateTimeOffset.FromUnixTimeSeconds(unix).DateTime;
        }

        /// <summary>
        /// Convert an Epoch uS to DateTime
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static DateTime? FromEpochUS(ulong? date)
        {
            if (date == null)
                return null;
            long unix = (long)(date / 1000) + EPOCH_S;
            return DateTimeOffset.FromUnixTimeSeconds(unix).DateTime;
        }

        /// <summary>
        /// Convert a TimeSpan ulong in ms to a TimeSpan
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static TimeSpan? FromMillis(ulong? timespan)
        {
            if (timespan == null)
                return null;
            return TimeSpan.FromMilliseconds(timespan.Value);
        }

        /// <summary>
        /// Convert a TimeSpan ulong in us to a TimeSpan
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static TimeSpan? FromMicros(ulong? timespan)
        {
            if (timespan == null)
                return null;
            return TimeSpan.FromMilliseconds(timespan.Value);
        }

        /// <summary>
        /// Converts a TimeSpan uint in sec to TimeSpan
        /// </summary>
        /// <param name="timespan"></param>
        /// <returns></returns>
        public static TimeSpan? FromSeconds(uint? timespan)
        {
            if (timespan == null)
                return null;
            return TimeSpan.FromSeconds(timespan.Value);
        }
    }
}
