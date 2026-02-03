using LoRaWANContainer.LoRaWan.NetworkServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

public class DeviceTimeAnswer : MacCommand
{
    public override int Length => 5;

    public uint GpsTimeSeconds { get; set; }

    public byte FractionalSeconds { get; set; }

    public DeviceTimeAnswer()
    {
        Cid = Cid.DeviceTimeCmd;

        // Default values based on current UTC time
        var gpsEpoch = new DateTime(1980, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var totalSeconds = (DateTime.UtcNow - gpsEpoch).TotalSeconds;

        GpsTimeSeconds = (uint)totalSeconds;
        FractionalSeconds = (byte)((totalSeconds - Math.Floor(totalSeconds)) * 256);
    }

    public override IEnumerable<byte> ToBytes()
    {
        List<byte> result = new();
        result.AddRange(BitConverter.GetBytes(GpsTimeSeconds).ToList());
        result.Add(FractionalSeconds);
        return result.ToArray();
    }

    public DateTime ToUtcDateTime(int leapSeconds = 18)
    {
        // GPS epoch: January 6, 1980
        DateTime gpsEpoch = new DateTime(1980, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        double totalSeconds = GpsTimeSeconds + (FractionalSeconds / 256.0);
        return gpsEpoch.AddSeconds(totalSeconds - leapSeconds);
    }

    public DeviceTimeAnswer FromBytes(byte[] payload)
    {
        if (payload.Length != 5)
        {
            throw new ArgumentException("DeviceTimeAns payload must be exactly 5 bytes.");
        }

        uint gpsSeconds = BitConverter.ToUInt32(payload, 0);
        byte fractional = payload[4];

        return new DeviceTimeAnswer
        {
            GpsTimeSeconds = gpsSeconds,
            FractionalSeconds = fractional
        };
    }

    public override string ToString()
    {
        return $"GPS Time: {GpsTimeSeconds} s, Fractional: {FractionalSeconds}/256, UTC: {ToUtcDateTime():O}";
    }
}
