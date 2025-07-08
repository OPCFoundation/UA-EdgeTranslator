
namespace Opc.Ua.Edge.Translator
{
    class ByteSwapper
    {
        public static byte[] Swap(byte[] value, bool swapPerRegister = false)
        {
            if (value.Length == 2)
            {
                byte[] swappedBytes = new byte[2];
                swappedBytes[0] = value[1];
                swappedBytes[1] = value[0];
                return swappedBytes;
            }

            if (value.Length == 4)
            {
                if (swapPerRegister)
                {
                    byte[] swappedBytes = new byte[4];
                    swappedBytes[2] = value[3];
                    swappedBytes[3] = value[2];
                    swappedBytes[0] = value[1];
                    swappedBytes[1] = value[0];
                    return swappedBytes;
                }
                else
                {
                    byte[] swappedBytes = new byte[4];
                    swappedBytes[0] = value[3];
                    swappedBytes[1] = value[2];
                    swappedBytes[2] = value[1];
                    swappedBytes[3] = value[0];
                    return swappedBytes;
                }
            }

            if (value.Length == 8)
            {
                if (swapPerRegister)
                {
                    byte[] swappedBytes = new byte[8];
                    swappedBytes[6] = value[7];
                    swappedBytes[7] = value[6];
                    swappedBytes[4] = value[5];
                    swappedBytes[5] = value[4];
                    swappedBytes[2] = value[3];
                    swappedBytes[3] = value[2];
                    swappedBytes[0] = value[1];
                    swappedBytes[1] = value[0];
                    return swappedBytes;
                }
                else
                {
                    byte[] swappedBytes = new byte[8];
                    swappedBytes[0] = value[7];
                    swappedBytes[1] = value[6];
                    swappedBytes[2] = value[5];
                    swappedBytes[3] = value[4];
                    swappedBytes[4] = value[3];
                    swappedBytes[5] = value[2];
                    swappedBytes[6] = value[1];
                    swappedBytes[7] = value[0];
                    return swappedBytes;
                }
            }

            // don't swap anything by default
            return value;
        }

        public static ushort Swap(ushort value)
        {
            return (ushort)(((value & 0x00FF) << 8) |
                            ((value & 0xFF00) >> 8));
        }

        public static uint Swap(uint value)
        {
            return ((value & 0x000000FF) << 24) |
                   ((value & 0x0000FF00) << 8) |
                   ((value & 0x00FF0000) >> 8) |
                   ((value & 0xFF000000) >> 24);
        }

        public static ulong Swap(ulong value)
        {
            return ((value & 0x00000000000000FFUL) << 56) |
                   ((value & 0x000000000000FF00UL) << 40) |
                   ((value & 0x0000000000FF0000UL) << 24) |
                   ((value & 0x00000000FF000000UL) << 8) |
                   ((value & 0x000000FF00000000UL) >> 8) |
                   ((value & 0x0000FF0000000000UL) >> 24) |
                   ((value & 0x00FF000000000000UL) >> 40) |
                   ((value & 0xFF00000000000000UL) >> 56);
        }
    }
}
