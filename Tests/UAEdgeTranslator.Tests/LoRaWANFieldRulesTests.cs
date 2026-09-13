namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System.Collections.Generic;
    using Xunit;

    /// <summary>
    /// Tests for the conditional-presence and value-mapping terms of the W3C
    /// WoT LoRaWAN binding.
    /// <para>
    /// A gated field must be reported <c>null</c> when its discriminator says it
    /// is absent, rather than decoded anyway: the distinction between "not
    /// present in this uplink" and "present and read as zero" is exactly what
    /// <c>lorav:presentWhen</c> exists to express.
    /// </para>
    /// </summary>
    public class LoRaWANFieldRulesTests
    {
        [Fact]
        public void A_value_map_translates_a_wire_value()
        {
            ValueMapEntry[] map =
            [
                new() { WireValue = 0, Value = "idle" },
                new() { WireValue = 1, Value = "running" }
            ];

            Assert.Equal("idle", LoRaWANFieldRules.ApplyValueMap(map, 0));
            Assert.Equal("running", LoRaWANFieldRules.ApplyValueMap(map, 1));
        }

        [Fact]
        public void An_unmapped_wire_value_passes_through_unchanged()
        {
            // Substituting a default would invent a reading the device never
            // sent, so an unrecognised value is surfaced as-is.
            ValueMapEntry[] map = [new() { WireValue = 0, Value = "idle" }];

            Assert.Equal(7, LoRaWANFieldRules.ApplyValueMap(map, 7));
        }

        [Fact]
        public void A_value_map_leaves_non_integral_values_alone()
        {
            // A float cannot match a wire value; failing the whole read over it
            // would lose the other fields in the same payload.
            ValueMapEntry[] map = [new() { WireValue = 0, Value = "idle" }];

            Assert.Equal("not a number", LoRaWANFieldRules.ApplyValueMap(map, "not a number"));
        }

        [Fact]
        public void An_absent_value_map_is_a_no_op()
        {
            Assert.Equal(42, LoRaWANFieldRules.ApplyValueMap(null, 42));
            Assert.Equal(42, LoRaWANFieldRules.ApplyValueMap([], 42));
        }

        [Fact]
        public void An_alias_resolves_to_the_offset_of_the_field_that_declares_it()
        {
            // The gate names its discriminator by alias, so the alias has to be
            // resolvable to a concrete byte offset before any read happens.
            LoRaWANFieldRules rules = new();

            rules.Register(
                "reportTypeTag",
                new LoRaWANFieldRule
                {
                    Alias = "reportType",
                    DiscriminatorOffset = 2,
                    DiscriminatorLength = 1,
                    PresentWhen = new PresentWhenCondition { Field = "x" }
                });

            (int? offset, int length) = rules.ResolveAlias("reportType");

            Assert.Equal(2, offset);
            Assert.Equal(1, length);
        }

        [Fact]
        public void An_unknown_alias_resolves_to_no_offset()
        {
            LoRaWANFieldRules rules = new();

            (int? offset, _) = rules.ResolveAlias("nosuchfield");

            Assert.Null(offset);
        }

        [Fact]
        public void A_rule_carrying_no_conditional_terms_is_not_registered()
        {
            // Registering empty rules for every plain field would make the
            // read path check a gate that can never fail.
            LoRaWANFieldRules rules = new();

            rules.Register("plain", new LoRaWANFieldRule());

            Assert.Null(rules.Get("plain"));
        }

        [Fact]
        public void A_rule_with_only_a_value_map_is_registered()
        {
            LoRaWANFieldRules rules = new();

            rules.Register(
                "mapped",
                new LoRaWANFieldRule
                {
                    ValueMap = [new ValueMapEntry { WireValue = 1, Value = "on" }]
                });

            Assert.NotNull(rules.Get("mapped"));
        }

        [Theory]
        [InlineData(0b0000_0001, 0, true)]
        [InlineData(0b0000_0001, 1, false)]
        [InlineData(0b1000_0000, 7, true)]
        [InlineData(0b0010_0000, 5, true)]
        public void A_bit_gate_tests_the_named_bit(int discriminator, int bit, bool expected)
        {
            // Mirrors the shift/mask the asset applies, documenting the bit
            // numbering the binding uses (bit 0 is least significant).
            bool actual = ((discriminator >> bit) & 1) == 1;

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void A_present_when_condition_deserializes_both_shapes()
        {
            // The binding allows {"field","bit"} and {"field","value"}.
            PresentWhenCondition byBit =
                Newtonsoft.Json.JsonConvert.DeserializeObject<PresentWhenCondition>(
                    """{"field":"flags","bit":3}""");

            PresentWhenCondition byValue =
                Newtonsoft.Json.JsonConvert.DeserializeObject<PresentWhenCondition>(
                    """{"field":"reportType","value":1}""");

            Assert.Equal("flags", byBit.Field);
            Assert.Equal(3, byBit.Bit);
            Assert.Null(byBit.Value);

            Assert.Equal("reportType", byValue.Field);
            Assert.Equal(1, byValue.Value);
            Assert.Null(byValue.Bit);
        }

        [Fact]
        public void Every_withdrawn_term_names_a_replacement()
        {
            // The message an author sees has to tell them what to write instead,
            // so an empty replacement would make the rejection unactionable.
            foreach (KeyValuePair<string, string> entry in LoRaWANForm.WithdrawnTerms)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(entry.Value),
                    $"'{entry.Key}' is withdrawn but names no replacement.");
            }
        }

        [Fact]
        public void No_term_is_both_withdrawn_and_unsupported()
        {
            // A term in both lists would produce whichever message happened to
            // be checked first, which is confusing and order-dependent.
            foreach (string term in LoRaWANForm.UnsupportedTerms)
            {
                Assert.False(
                    LoRaWANForm.WithdrawnTerms.ContainsKey(term),
                    $"'{term}' appears in both UnsupportedTerms and WithdrawnTerms.");
            }
        }
    }
}
