namespace Opc.Ua.Edge.Translator.Tests
{
    using Xunit;

    public class ByteSwapperTests
    {
        [Fact]
        public void Swap_2_bytes_reverses_bytes()
        {
            byte[] result = ByteSwapper.Swap(new byte[] { 0x01, 0x02 });
            Assert.Equal(new byte[] { 0x02, 0x01 }, result);
        }

        [Fact]
        public void Swap_4_bytes_full_reverse_when_swapPerWord_is_false()
        {
            byte[] result = ByteSwapper.Swap(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            Assert.Equal(new byte[] { 0x04, 0x03, 0x02, 0x01 }, result);
        }

        [Fact]
        public void Swap_4_bytes_swaps_each_word_when_swapPerWord_is_true()
        {
            byte[] result = ByteSwapper.Swap(new byte[] { 0x01, 0x02, 0x03, 0x04 }, swapPerWord: true);
            Assert.Equal(new byte[] { 0x02, 0x01, 0x04, 0x03 }, result);
        }

        [Fact]
        public void Swap_8_bytes_full_reverse_when_swapPerWord_is_false()
        {
            byte[] input = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            byte[] result = ByteSwapper.Swap(input);
            Assert.Equal(new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 }, result);
        }

        [Fact]
        public void Swap_8_bytes_swaps_each_word_when_swapPerWord_is_true()
        {
            byte[] input = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            byte[] result = ByteSwapper.Swap(input, swapPerWord: true);
            Assert.Equal(new byte[] { 0x02, 0x01, 0x04, 0x03, 0x06, 0x05, 0x08, 0x07 }, result);
        }

        [Fact]
        public void Swap_returns_input_unchanged_for_unsupported_lengths()
        {
            byte[] input = { 0x01, 0x02, 0x03 };
            byte[] result = ByteSwapper.Swap(input);
            Assert.Same(input, result);
        }
    }
}
