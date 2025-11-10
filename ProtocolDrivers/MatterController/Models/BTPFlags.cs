
namespace Matter.Core
{
    using System;

    [Flags]
    public enum BTPFlags : byte
    {
        Beginning = 0x1,
        Continuing = 0x2,
        Ending = 0x4,
        Acknowledge = 0x8,
        Reserved = 0x10,
        Management = 0x20,
        Handshake = 0x40
    }
}
