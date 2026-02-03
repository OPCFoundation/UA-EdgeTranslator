namespace Matter.Core
{
    using System;

    [Flags]
    public enum MessageFlags : byte
    {
        Version1 = 0x00,
        DestinationNodeID = 0x01,
        DestinationGroupID = 0x02,
        SourceNodeID = 0x04
    }
}
