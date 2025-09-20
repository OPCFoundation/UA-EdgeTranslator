namespace Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models
{
    using System;

    [Flags]
    public enum MessageFlags : byte
    {
        DSIZ0 = 0x00,
        DSIZ1 = 0x01,
        DSIZ2 = 0x02,
        DSIZ3 = 0x03,
        S = 0x04,
    }
}
