namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.Models;
    using Xunit;

    /// <summary>
    /// Pins down how a <c>lorav:divisor</c> survives the driver's scaling path.
    /// <para>
    /// The binding prefers a divisor over a fractional multiplier because
    /// <c>0.01</c> is not representable in binary floating point. That advice is
    /// only worth following if the divisor stays exact through
    /// <see cref="LoRaWANForm.EffectiveMultiplier"/>, which collapses it into a
    /// single <see cref="float"/> scale factor.
    /// </para>
    /// </summary>
    public class LoRaWANScalingPrecisionTests
    {
        [Theory]
        [InlineData(2730, 100, 27.3)]
        [InlineData(1899, 100, 18.99)]
        [InlineData(655, 10, 65.5)]
        [InlineData(3300, 1000, 3.3)]
        public void A_divisor_decodes_a_representative_reading_accurately(
            int raw,
            float divisor,
            double expected)
        {
            LoRaWANForm form = new() { Divisor = divisor };

            float scale = form.EffectiveMultiplier() ?? 1.0f;

            // The asset multiplies the raw integer by this scale.
            double decoded = raw * scale;

            // Four decimal places is well inside any sensor's accuracy, and is
            // the tolerance a displayed reading needs.
            Assert.Equal(expected, decoded, 4);
        }

        [Fact]
        public void A_divisor_and_the_equivalent_multiplier_agree_after_scaling()
        {
            // Both spellings describe the same conversion, so a Thing
            // Description using either must decode alike.
            LoRaWANForm byDivisor = new() { Divisor = 100 };
            LoRaWANForm byMultiplier = new() { Multiplier = 0.01f };

            const int raw = 2730;

            double viaDivisor = raw * (byDivisor.EffectiveMultiplier() ?? 1.0f);
            double viaMultiplier = raw * (byMultiplier.EffectiveMultiplier() ?? 1.0f);

            Assert.Equal(viaDivisor, viaMultiplier, 4);
        }

        [Fact]
        public void A_zero_divisor_is_ignored_rather_than_producing_infinity()
        {
            LoRaWANForm form = new() { Divisor = 0 };

            Assert.Equal(1.0f, form.EffectiveMultiplier());
        }

        [Fact]
        public void A_form_with_neither_term_has_no_scaling()
        {
            Assert.Null(new LoRaWANForm().EffectiveMultiplier());
        }

        [Fact]
        public void A_multiplier_and_divisor_together_compose()
        {
            // The binding allows both; the effective scale is multiplier/divisor.
            LoRaWANForm form = new() { Multiplier = 3.0f, Divisor = 2.0f };

            Assert.Equal(1.5f, form.EffectiveMultiplier());
        }
    }
}
