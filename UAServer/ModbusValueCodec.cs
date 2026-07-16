namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Size- and endian-aware conversion between Modbus wire bytes and CLR values.
    /// Shared by the ModbusTCP and ModbusRTU drivers so both honour the width the
    /// Thing Description declares (via <see cref="AssetTag.Type"/>) and the actual
    /// number of wire bytes read, instead of unconditionally treating everything as
    /// a 32-bit integer. Native 16-bit registers can therefore be read at
    /// <c>quantity=1</c> without a zero-padded neighbour register.
    /// </summary>
    public static class ModbusValueCodec
    {
        /// <summary>
        /// Decode raw Modbus wire bytes (big-endian [Hi][Lo] per register) into a CLR
        /// value according to <paramref name="tag"/>.Type. The byte/word order is applied
        /// from <see cref="AssetTag.IsBigEndian"/> / <see cref="AssetTag.SwapPerWord"/>.
        /// Throws (and logs at Error) when the declared type cannot be satisfied by the
        /// number of wire bytes, rather than silently decoding a short buffer.
        /// </summary>
        public static object Decode(AssetTag tag, byte[] wireBytes)
        {
            if (wireBytes == null)
            {
                throw new ArgumentNullException(nameof(wireBytes));
            }

            switch (tag.Type)
            {
                case "Boolean":
                    return IsNonZero(wireBytes);

                case "String":
                    return DecodeString(wireBytes);

                case "Byte":
                    RequireWireLength(tag, wireBytes, 2);
                    return Scale(tag, (byte)(BitConverter.ToUInt16(Ordered(tag, wireBytes), 0) & 0xFF));

                case "Short":
                    RequireWireLength(tag, wireBytes, 2);
                    return Scale(tag, BitConverter.ToInt16(Ordered(tag, wireBytes), 0));

                case "Integer":
                    RequireWireLength(tag, wireBytes, 4);
                    return Scale(tag, BitConverter.ToInt32(Ordered(tag, wireBytes), 0));

                case "Long":
                    RequireWireLength(tag, wireBytes, 8);
                    return Scale(tag, BitConverter.ToInt64(Ordered(tag, wireBytes), 0));

                case "UnsignedLong":
                    RequireWireLength(tag, wireBytes, 8);
                    return Scale(tag, BitConverter.ToUInt64(Ordered(tag, wireBytes), 0));

                case "Float":
                    RequireWireLength(tag, wireBytes, 4);
                    return Scale(tag, BitConverter.ToSingle(Ordered(tag, wireBytes), 0));

                case "Double":
                    RequireWireLength(tag, wireBytes, 8);
                    return Scale(tag, BitConverter.ToDouble(Ordered(tag, wireBytes), 0));

                default:
                    throw UnsupportedType(tag);
            }
        }

        /// <summary>
        /// Encode a CLR value into Modbus wire bytes of the exact width declared by
        /// <paramref name="tag"/>.Type. The returned buffer is in the same convention the
        /// drivers' register-packing expects (host order, then swapped when
        /// <see cref="AssetTag.IsBigEndian"/> is set). A 16-bit type therefore produces a
        /// single register and is never promoted to a 32-bit write.
        /// </summary>
        public static byte[] Encode(AssetTag tag, object value)
        {
            byte[] raw;

            switch (tag.Type)
            {
                case "Boolean":
                    // A whole register (2 bytes) so Boolean holding-register writes address
                    // one register; coil writes only look at the first byte.
                    return BitConverter.GetBytes((ushort)(Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? 1 : 0));

                case "String":
                    return EncodeString(value);

                case "Byte":
                    raw = BitConverter.GetBytes((ushort)Convert.ToByte(value, CultureInfo.InvariantCulture));
                    break;

                case "Short":
                    raw = BitConverter.GetBytes(Convert.ToInt16(value, CultureInfo.InvariantCulture));
                    break;

                case "Integer":
                    raw = BitConverter.GetBytes(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    break;

                case "Long":
                    raw = BitConverter.GetBytes(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    break;

                case "UnsignedLong":
                    raw = BitConverter.GetBytes(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                    break;

                case "Float":
                    raw = BitConverter.GetBytes(Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    break;

                case "Double":
                    raw = BitConverter.GetBytes(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                    break;

                default:
                    throw UnsupportedType(tag);
            }

            return tag.IsBigEndian ? ByteSwapper.Swap(raw, tag.SwapPerWord) : raw;
        }

        /// <summary>
        /// Convert Modbus register values into wire-format bytes: big-endian [Hi][Lo]
        /// per register. Use this to bridge an NModbus register read into the raw byte
        /// buffer that <see cref="Decode"/> consumes.
        /// </summary>
        public static byte[] RegistersToWireBytes(ushort[] registers)
        {
            byte[] bytes = new byte[registers.Length * 2];
            int j = 0;

            for (int i = 0; i < registers.Length; i++)
            {
                bytes[j++] = (byte)(registers[i] >> 8);
                bytes[j++] = (byte)(registers[i] & 0xFF);
            }

            return bytes;
        }

        /// <summary>
        /// Pack coil/discrete-input booleans into Modbus bit-packed format (LSB-first per
        /// byte). Use this to bridge an NModbus coil/input read into the raw byte buffer
        /// that <see cref="Decode"/> consumes.
        /// </summary>
        public static byte[] CoilsToWireBytes(bool[] coils)
        {
            int byteCount = (coils.Length + 7) / 8;
            byte[] data = new byte[byteCount];

            for (int i = 0; i < coils.Length; i++)
            {
                if (coils[i])
                {
                    data[i / 8] |= (byte)(1 << (i % 8)); // LSB-first
                }
            }

            return data;
        }

        private static byte[] Ordered(AssetTag tag, byte[] wireBytes)
        {
            return tag.IsBigEndian ? ByteSwapper.Swap(wireBytes, tag.SwapPerWord) : wireBytes;
        }

        private static bool IsNonZero(byte[] wireBytes)
        {
            foreach (byte b in wireBytes)
            {
                if (b != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string DecodeString(byte[] wireBytes)
        {
            string decoded = Encoding.UTF8.GetString(wireBytes);

            int terminator = decoded.IndexOf('\0');
            return terminator >= 0 ? decoded.Substring(0, terminator) : decoded;
        }

        private static byte[] EncodeString(object value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value?.ToString() ?? string.Empty);

            if ((utf8.Length % 2) != 0)
            {
                // Modbus registers are 16 bits wide; pad to a whole number of registers.
                Array.Resize(ref utf8, utf8.Length + 1);
            }

            return utf8;
        }

        // The multiplier is applied exactly as the historical decode did: when configured
        // (non-zero) the raw reading is scaled and returned as a float; otherwise the
        // natural width is preserved. Double scales in double to keep its precision.
        private static object Scale(AssetTag tag, byte raw)
        {
            return tag.Multiplier != 0.0f ? (object)((float)raw * tag.Multiplier) : raw;
        }

        private static object Scale(AssetTag tag, short raw)
        {
            return tag.Multiplier != 0.0f ? (object)((float)raw * tag.Multiplier) : raw;
        }

        private static object Scale(AssetTag tag, int raw)
        {
            return tag.Multiplier != 0.0f ? (object)((float)raw * tag.Multiplier) : raw;
        }

        private static object Scale(AssetTag tag, long raw)
        {
            return tag.Multiplier != 0.0f ? (object)((float)raw * tag.Multiplier) : raw;
        }

        private static object Scale(AssetTag tag, ulong raw)
        {
            return tag.Multiplier != 0.0f ? (object)((float)raw * tag.Multiplier) : raw;
        }

        private static object Scale(AssetTag tag, float raw)
        {
            return tag.Multiplier != 0.0f ? (object)(raw * tag.Multiplier) : raw;
        }

        private static object Scale(AssetTag tag, double raw)
        {
            return tag.Multiplier != 0.0f ? (object)(raw * tag.Multiplier) : raw;
        }

        private static void RequireWireLength(AssetTag tag, byte[] wireBytes, int expected)
        {
            if (wireBytes.Length != expected)
            {
                string message = $"Modbus type '{tag.Type}' for tag '{tag.Name}' requires {expected} wire byte(s) but {wireBytes.Length} were read. Check the '?quantity=' in the href matches the declared type width.";
                Log.Logger.Error(message);
                throw new ArgumentException(message);
            }
        }

        private static Exception UnsupportedType(AssetTag tag)
        {
            string message = $"Modbus type '{tag.Type ?? "(null)"}' for tag '{tag.Name}' is not supported. Expected one of: Boolean, Byte, Short, Integer, Long, UnsignedLong, Float, Double, String.";
            Log.Logger.Error(message);
            return new ArgumentException(message);
        }
    }
}
