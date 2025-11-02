namespace Matter.Core
{
    using System;

    [Flags]
    public enum SecurityFlags : byte
    {
        UnicastSession = 0x0,
        GroupSession = 0x1,
        SessionMask = 0x3,
        MessageExtensions = 0x20,
        ControlMessage = 0x40,
        Privacy = 0x80,
    }
}
