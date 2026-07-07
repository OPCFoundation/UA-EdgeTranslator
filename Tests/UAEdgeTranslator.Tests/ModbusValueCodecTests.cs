namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using Xunit;

    /// <summary>
    /// Unit tests for <see cref="ModbusValueCodec"/>: the size- and endian-aware
    /// decode/encode shared by the ModbusTCP and ModbusRTU drivers. These lock in
    /// the fix that lets native 16-bit registers be read at <c>quantity=1</c>
    /// (a single register) instead of forcing <c>quantity=2</c> with a zero-padded
    /// neighbour register.
    /// </summary>
    public class ModbusValueCodecTests
    {
        // Multiplier 0 disables scaling so decode returns the natural CLR width,
        // which makes width assertions unambiguous.
        private static AssetTag Tag(string type, bool bigEndian = false, bool swapPerWord = false, float multiplier = 0.0f)
        {
            return new AssetTag
            {
                Name = "t",
                Type = type,
                IsBigEndian = bigEndian,
                SwapPerWord = swapPerWord,
                Multiplier = multiplier
            };
        }

        [Fact]
        public void Short_decodes_from_single_register_little_endian()
        {
            byte[] wire = BitConverter.GetBytes((short)1234);

            object value = ModbusValueCodec.Decode(Tag("Short"), wire);

            Assert.Equal(2, wire.Length);
            Assert.Equal((short)1234, Assert.IsType<short>(value));
        }

        [Fact]
        public void Short_decodes_from_single_register_big_endian()
        {
            byte[] wire = { 0x04, 0xD2 }; // 1234 in Modbus wire order [Hi][Lo]

            object value = ModbusValueCodec.Decode(Tag("Short", bigEndian: true), wire);

            Assert.Equal((short)1234, Assert.IsType<short>(value));
        }

        [Fact]
        public void Byte_decodes_low_byte_of_register()
        {
            byte[] wire = BitConverter.GetBytes((ushort)0x00AB);

            object value = ModbusValueCodec.Decode(Tag("Byte"), wire);

            Assert.Equal((byte)0xAB, Assert.IsType<byte>(value));
        }

        [Fact]
        public void Integer_decodes_from_two_registers()
        {
            byte[] wire = BitConverter.GetBytes(70000);

            Assert.Equal(70000, Assert.IsType<int>(ModbusValueCodec.Decode(Tag("Integer"), wire)));
        }

        [Fact]
        public void Long_decodes_from_four_registers()
        {
            byte[] wire = BitConverter.GetBytes(1234567890123L);

            Assert.Equal(1234567890123L, Assert.IsType<long>(ModbusValueCodec.Decode(Tag("Long"), wire)));
        }

        [Fact]
        public void UnsignedLong_decodes_from_four_registers()
        {
            byte[] wire = BitConverter.GetBytes(9876543210UL);

            Assert.Equal(9876543210UL, Assert.IsType<ulong>(ModbusValueCodec.Decode(Tag("UnsignedLong"), wire)));
        }

        [Fact]
        public void Float_decodes_from_two_registers()
        {
            byte[] wire = BitConverter.GetBytes(123.5f);

            Assert.Equal(123.5f, Assert.IsType<float>(ModbusValueCodec.Decode(Tag("Float"), wire)));
        }

        [Fact]
        public void Double_decodes_from_four_registers()
        {
            byte[] wire = BitConverter.GetBytes(1234.5678d);

            Assert.Equal(1234.5678d, Assert.IsType<double>(ModbusValueCodec.Decode(Tag("Double"), wire)));
        }

        [Theory]
        [InlineData(new byte[] { 0x00, 0x00 }, false)]
        [InlineData(new byte[] { 0x00, 0x01 }, true)] // high byte set: previously false via ToBoolean(bytes[0])
        [InlineData(new byte[] { 0x01, 0x00 }, true)]
        [InlineData(new byte[] { 0x01 }, true)] // packed coil bit
        public void Boolean_is_true_when_any_register_byte_is_nonzero(byte[] wire, bool expected)
        {
            Assert.Equal(expected, Assert.IsType<bool>(ModbusValueCodec.Decode(Tag("Boolean"), wire)));
        }

        [Fact]
        public void String_decodes_utf8_and_trims_nul_padding()
        {
            byte[] wire = { (byte)'H', (byte)'i', 0x00, 0x00 };

            Assert.Equal("Hi", Assert.IsType<string>(ModbusValueCodec.Decode(Tag("String"), wire)));
        }

        [Fact]
        public void Multiplier_scales_reading_and_returns_float()
        {
            byte[] wire = BitConverter.GetBytes((short)5);

            object value = ModbusValueCodec.Decode(Tag("Short", multiplier: 10.0f), wire);

            Assert.Equal(50.0f, Assert.IsType<float>(value));
        }

        [Fact]
        public void Decode_throws_when_wire_length_does_not_match_type_width()
        {
            byte[] wire = BitConverter.GetBytes((short)5); // 2 bytes, but Integer needs 4

            Assert.Throws<ArgumentException>(() => ModbusValueCodec.Decode(Tag("Integer"), wire));
        }

        [Fact]
        public void Decode_throws_for_unknown_type()
        {
            Assert.Throws<ArgumentException>(() => ModbusValueCodec.Decode(Tag("NotAModbusType"), new byte[] { 0x00, 0x00 }));
        }

        [Fact]
        public void Encode_short_produces_a_single_register_without_promotion()
        {
            byte[] bytes = ModbusValueCodec.Encode(Tag("Short"), (short)1234);

            Assert.Equal(2, bytes.Length);
        }

        [Fact]
        public void Encode_integer_produces_two_registers()
        {
            byte[] bytes = ModbusValueCodec.Encode(Tag("Integer"), 70000);

            Assert.Equal(4, bytes.Length);
        }

        [Fact]
        public void Encode_throws_for_unknown_type()
        {
            Assert.Throws<ArgumentException>(() => ModbusValueCodec.Encode(Tag("NotAModbusType"), 1));
        }

        [Theory]
        [InlineData("Short", false, (short)1234)]
        [InlineData("Short", true, (short)1234)]
        [InlineData("Byte", false, (byte)200)]
        [InlineData("Integer", false, 70000)]
        [InlineData("Integer", true, 70000)]
        [InlineData("Float", false, 123.5f)]
        [InlineData("Float", true, 123.5f)]
        [InlineData("Double", false, 1234.5678d)]
        [InlineData("Long", false, 1234567890123L)]
        [InlineData("UnsignedLong", false, 9876543210UL)]
        public void Encode_then_decode_round_trips(string type, bool bigEndian, object expected)
        {
            AssetTag tag = Tag(type, bigEndian: bigEndian);

            byte[] encoded = ModbusValueCodec.Encode(tag, expected);
            object decoded = ModbusValueCodec.Decode(tag, encoded);

            Assert.Equal(expected, decoded);
        }

        [Fact]
        public void Encode_then_decode_round_trips_with_byte_and_word_swap()
        {
            AssetTag tag = Tag("Integer", bigEndian: true, swapPerWord: true);

            byte[] encoded = ModbusValueCodec.Encode(tag, 70000);

            Assert.Equal(70000, ModbusValueCodec.Decode(tag, encoded));
        }
    }
}
