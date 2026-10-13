namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using System;
    using Xunit;

    /// <summary>
    /// Covers <see cref="VariantConversion"/>, which restores the pre-SDK-2.0.0 driver
    /// contract: values reach object-based driver code as plain CLR values. Each case
    /// below corresponds to a mistake that compiles cleanly but breaks at runtime.
    /// </summary>
    public class VariantConversionTests
    {
        [Fact]
        public void Null_variant_yields_null()
        {
            Assert.Null(VariantConversion.ToClrValue(default));
        }

        [Fact]
        public void Never_returns_a_boxed_Variant()
        {
            // Regression guard for the core bug: assigning a Variant to object boxes the
            // struct itself, so driver conversions such as Convert.ToInt16 throw.
            object value = VariantConversion.ToClrValue(Variant.From(42));

            Assert.IsNotType<Variant>(value);
            Assert.Equal((short)42, Convert.ToInt16(value));
        }

        [Fact]
        public void Primitives_and_strings_pass_through_unchanged()
        {
            Assert.Equal(1.5f, Assert.IsType<float>(VariantConversion.ToClrValue(Variant.From(1.5f))));
            Assert.Equal(7, Assert.IsType<int>(VariantConversion.ToClrValue(Variant.From(7))));
            Assert.True(Assert.IsType<bool>(VariantConversion.ToClrValue(Variant.From(true))));
            Assert.Equal("text", Assert.IsType<string>(VariantConversion.ToClrValue(Variant.From("text"))));
        }

        [Fact]
        public void ByteString_maps_to_byte_array()
        {
            object value = VariantConversion.ToClrValue(Variant.From((ByteString)new byte[] { 1, 2, 3 }));

            Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<byte[]>(value));
        }

        [Fact]
        public void DateTimeUtc_maps_to_utc_DateTime()
        {
            DateTime now = new(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);

            object value = VariantConversion.ToClrValue(Variant.From((DateTimeUtc)now));

            DateTime result = Assert.IsType<DateTime>(value);
            Assert.Equal(DateTimeKind.Utc, result.Kind);
            Assert.Equal(now, result);
        }

        [Fact]
        public void ArrayOf_maps_to_clr_array()
        {
            object value = VariantConversion.ToClrValue(Variant.From(new ArrayOf<float>(new[] { 1f, 2f })));

            Assert.Equal(new[] { 1f, 2f }, Assert.IsType<float[]>(value));
        }

        [Fact]
        public void ArrayOf_ByteString_maps_elements_to_byte_arrays()
        {
            ArrayOf<ByteString> source = new(new[] { (ByteString)new byte[] { 1 }, (ByteString)new byte[] { 2, 3 } });

            byte[][] result = Assert.IsType<byte[][]>(VariantConversion.ToClrValue(Variant.From(source)));

            Assert.Equal(new byte[] { 1 }, result[0]);
            Assert.Equal(new byte[] { 2, 3 }, result[1]);
        }

        [Fact]
        public void ArrayOf_DateTimeUtc_maps_elements_to_DateTimes()
        {
            DateTime time = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            ArrayOf<DateTimeUtc> source = new(new[] { (DateTimeUtc)time });

            DateTime[] result = Assert.IsType<DateTime[]>(VariantConversion.ToClrValue(Variant.From(source)));

            Assert.Equal(time, result[0]);
        }
    }
}
