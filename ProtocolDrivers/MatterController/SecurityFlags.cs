namespace Matter.Core
{
    using System;

    [Flags]
    public enum SecurityFlags : byte
    {
        SessionType = 0x0,// Unsecure session type.
        MessageExtensions = 0x20,
        ControlMessage = 0x40,
        Privacy = 0x80,
    }
}
