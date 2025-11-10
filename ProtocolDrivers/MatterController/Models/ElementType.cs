
namespace Matter.Core
{
    using System;

    [Flags]
    public enum ElementType : byte
    {
        SByte   = 0x0,
        Short   = 0x1,
        Int     = 0x2,
        Long    = 0x3,
        Byte    = 0x4,
        UShort  = 0x5,
        UInt    = 0x6,
        ULong   = 0x7,
        False   = 0x8,
        True    = 0x9,
        Float   = 0xA,
        Double  = 0xB,
        String8 = 0xC,
        String16 = 0xD,
        String32 = 0xE,
        String64 = 0xF,
        Bytes8  = 0x10,
        Bytes16 = 0x11,
        Bytes32 = 0x12,
        Bytes64 = 0x13,
        Null    = 0x14,
        Structure = 0x15,
        Array   = 0x16,
        List    = 0x17,
        EndOfContainer = 0x18,
        ContextSpecific = 0x20,
        ElementTypeMask = 0x1F,
        None = 0xFF
    }
}
