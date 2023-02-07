
namespace Opc.Ua.Edge.Translator
{
    class ByteSwapper
    {
        public static byte[] Swap(byte[] value)
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
                byte[] swappedBytes = new byte[4];
                swappedBytes[0] = value[3];
                swappedBytes[1] = value[2];
                swappedBytes[2] = value[1];
                swappedBytes[3] = value[0];
                return swappedBytes;
            }

            if (value.Length == 8)
            {
                byte[] swappedBytes = new byte[8];
                swappedBytes[4] = value[7];
                swappedBytes[5] = value[6];
                swappedBytes[6] = value[5];
                swappedBytes[7] = value[4];
                swappedBytes[0] = value[3];
                swappedBytes[1] = value[2];
                swappedBytes[2] = value[1];
                swappedBytes[3] = value[0];
                return swappedBytes;
            }

            // don't swap anything my default
            return value;
        }
    }
}
