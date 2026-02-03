
namespace Opc.Ua.Edge.Translator
{
    public class ByteSwapper
    {
        public static byte[] Swap(byte[] value, bool swapPerWord = false)
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
                if (swapPerWord)
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
                if (swapPerWord)
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
    }
}
