namespace Opc.Ua.Edge.Translator.Tests.Diagnostics
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// The Explorer formats the CLR value of a <see cref="Variant"/>: SDK 2.0 carries
    /// arrays as <c>ArrayOf&lt;T&gt;</c> and byte strings as <see cref="ByteString"/>, which
    /// must still reach the bounded-array and byte-count formatting.
    /// </summary>
    public sealed class AddressSpaceValueFormattingTests
    {
        [Fact]
        public void Arrays_are_listed_element_by_element()
        {
            string text = AddressSpaceService.FormatValue(new DataValue(new Variant(new[] { 1, 2, 3 })));

            Assert.Equal("[1, 2, 3]", text);
        }

        [Fact]
        public void Long_arrays_are_bounded()
        {
            int[] values = Enumerable.Range(0, 25).ToArray();

            string text = AddressSpaceService.FormatValue(new DataValue(new Variant(values)));

            Assert.StartsWith("[0, 1, 2", text);
            Assert.EndsWith(", ... (25 items)]", text);
            Assert.DoesNotContain("24", text);
        }

        [Fact]
        public void Byte_strings_show_their_length()
        {
            string text = AddressSpaceService.FormatValue(new DataValue(new Variant(new byte[] { 1, 2, 3, 4 })));

            Assert.Equal("byte[4]", text);
        }

        [Fact]
        public void Scalars_are_formatted_invariantly()
        {
            Assert.Equal("1.5", AddressSpaceService.FormatValue(new DataValue(new Variant(1.5))));
            Assert.Equal("hello", AddressSpaceService.FormatValue(new DataValue(new Variant("hello"))));
        }
    }
}
