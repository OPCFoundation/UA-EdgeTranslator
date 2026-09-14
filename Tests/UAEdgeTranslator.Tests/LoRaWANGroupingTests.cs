namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Xunit;

    /// <summary>
    /// Tests for the grouping terms (<c>lorav:slot</c>, <c>lorav:padBefore</c>),
    /// <c>lorav:defaultEventingFrequencyMinutes</c>, and the resolution of a
    /// <c>lorav:presentWhen</c> discriminator by event name.
    /// </summary>
    public class LoRaWANGroupingTests
    {
        private static ThingDescription Thing(string extraJson = null)
        {
            string json = $$"""
            {
              "base": "lorawan://localhost/0000000000000003/",
              "lorav:payloadLayout": "fixed"
              {{(extraJson is null ? string.Empty : "," + extraJson)}}
            }
            """;

            return JsonConvert.DeserializeObject<ThingDescription>(json);
        }

        [Fact]
        public void A_value_without_a_slot_sits_at_its_declared_offset()
        {
            LoRaWANProtocolDriver driver = new();

            string form = """
            {
              "href": "uplink",
              "type": "Short",
              "lorav:byteOffset": 4,
              "lorav:wireType": "s16"
            }
            """;

            AssetTag tag = driver.CreateTag(
                Thing(), JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null);

            Assert.Equal("0000000000000003/4?quantity=2", tag.Address);
        }

        [Fact]
        public void Pad_before_shifts_a_value_forward()
        {
            // Reserved bytes sit between the locator and the value.
            LoRaWANProtocolDriver driver = new();

            string form = """
            {
              "href": "uplink",
              "type": "Short",
              "lorav:byteOffset": 2,
              "lorav:padBefore": 3,
              "lorav:wireType": "s16"
            }
            """;

            AssetTag tag = driver.CreateTag(
                Thing(), JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null);

            Assert.Equal("0000000000000003/5?quantity=2", tag.Address);
        }

        [Fact]
        public void A_later_slot_starts_after_the_bytes_of_earlier_ones()
        {
            // Three values share byteOffset 0: a u8, then an s16, then a u8.
            // The third begins 3 bytes in.
            const string events = """
            "events": {
              "first":  { "forms": [ { "href": "uplink", "type": "Byte",  "lorav:byteOffset": 0, "lorav:slot": 0, "lorav:wireType": "u8" } ] },
              "second": { "forms": [ { "href": "uplink", "type": "Short", "lorav:byteOffset": 0, "lorav:slot": 1, "lorav:wireType": "s16" } ] },
              "third":  { "forms": [ { "href": "uplink", "type": "Byte",  "lorav:byteOffset": 0, "lorav:slot": 2, "lorav:wireType": "u8" } ] }
            }
            """;

            ThingDescription td = Thing(events);
            LoRaWANProtocolDriver driver = new();

            AssetTag first = driver.CreateTag(td, td.Events["first"].Forms[0], "asset", 1, "first", "nsu=x;i=1", null);
            AssetTag second = driver.CreateTag(td, td.Events["second"].Forms[0], "asset", 1, "second", "nsu=x;i=2", null);
            AssetTag third = driver.CreateTag(td, td.Events["third"].Forms[0], "asset", 1, "third", "nsu=x;i=3", null);

            Assert.Equal("0000000000000003/0?quantity=1", first.Address);
            Assert.Equal("0000000000000003/1?quantity=2", second.Address);
            Assert.Equal("0000000000000003/3?quantity=1", third.Address);
        }

        [Fact]
        public void A_derived_slot_does_not_displace_later_values()
        {
            // A derived value occupies no payload bytes, so the value after it
            // must not be pushed along.
            const string events = """
            "events": {
              "raw":      { "forms": [ { "href": "uplink", "type": "Byte", "lorav:byteOffset": 0, "lorav:slot": 0, "lorav:wireType": "u8" } ] },
              "computed": { "forms": [ { "href": "uplink", "type": "Float", "lorav:byteOffset": 0, "lorav:slot": 1, "lorav:wireType": "number", "lorav:derived": { "ref": "$raw", "polynomial": [ 0, 2 ] } } ] },
              "next":     { "forms": [ { "href": "uplink", "type": "Byte", "lorav:byteOffset": 0, "lorav:slot": 2, "lorav:wireType": "u8" } ] }
            }
            """;

            ThingDescription td = Thing(events);
            LoRaWANProtocolDriver driver = new();

            AssetTag next = driver.CreateTag(td, td.Events["next"].Forms[0], "asset", 1, "next", "nsu=x;i=3", null);

            // Only 'raw' consumed a byte.
            Assert.Equal("0000000000000003/1?quantity=1", next.Address);
        }

        [Fact]
        public void Slots_in_different_groups_do_not_interfere()
        {
            // Two values at different offsets are not part of one group, even
            // though both declare a slot.
            const string events = """
            "events": {
              "groupA": { "forms": [ { "href": "uplink", "type": "Short", "lorav:byteOffset": 0, "lorav:slot": 0, "lorav:wireType": "s16" } ] },
              "groupB": { "forms": [ { "href": "uplink", "type": "Byte",  "lorav:byteOffset": 8, "lorav:slot": 1, "lorav:wireType": "u8" } ] }
            }
            """;

            ThingDescription td = Thing(events);
            LoRaWANProtocolDriver driver = new();

            AssetTag b = driver.CreateTag(td, td.Events["groupB"].Forms[0], "asset", 1, "groupB", "nsu=x;i=2", null);

            // groupA's 2 bytes are at a different offset and must not shift it.
            Assert.Equal("0000000000000003/8?quantity=1", b.Address);
        }

        [Fact]
        public void A_gate_resolves_its_discriminator_by_event_name()
        {
            // lorav:alias is only needed "when a condition's field differs from
            // the event name", so a plain event name must resolve to the
            // discriminator's offset. Without this the gate silently reports
            // every value absent.
            const string events = """
            "events": {
              "reportType": { "forms": [ { "href": "uplink", "type": "Byte", "lorav:byteOffset": 2, "lorav:wireType": "u8" } ] },
              "gated":      { "forms": [ { "href": "uplink", "type": "Short", "lorav:byteOffset": 4, "lorav:wireType": "s16", "lorav:presentWhen": { "field": "reportType", "value": 1 } } ] }
            }
            """;

            ThingDescription td = Thing(events);
            LoRaWANProtocolDriver driver = new();
            LoRaWANNetworkServerAsset asset = driver.NetworkServer;

            driver.CreateTag(td, td.Events["reportType"].Forms[0], "asset", 1, "reportType", "nsu=x;i=1", null);
            AssetTag gated = driver.CreateTag(td, td.Events["gated"].Forms[0], "asset", 1, "gated", "nsu=x;i=2", null);

            LoRaWANFieldRule rule = asset.FieldRules.Get(gated.Name);

            Assert.NotNull(rule);

            // The gate must know where to read the discriminator from.
            Assert.Equal(2, rule.DiscriminatorOffset);
            Assert.Equal(1, rule.DiscriminatorLength);
        }

        [Fact]
        public void An_explicit_alias_still_wins_over_an_event_name()
        {
            // An author declares an alias precisely when the condition's field
            // is not the event name, so the alias must take precedence.
            const string events = """
            "events": {
              "kind":   { "forms": [ { "href": "uplink", "type": "Byte", "lorav:byteOffset": 9, "lorav:wireType": "u8" } ] },
              "sensor": { "forms": [ { "href": "uplink", "type": "Byte", "lorav:byteOffset": 1, "lorav:wireType": "u8", "lorav:alias": "kind" } ] },
              "gated":  { "forms": [ { "href": "uplink", "type": "Short", "lorav:byteOffset": 4, "lorav:wireType": "s16", "lorav:presentWhen": { "field": "kind", "value": 1 } } ] }
            }
            """;

            ThingDescription td = Thing(events);
            LoRaWANProtocolDriver driver = new();
            LoRaWANNetworkServerAsset asset = driver.NetworkServer;

            driver.CreateTag(td, td.Events["kind"].Forms[0], "asset", 1, "kind", "nsu=x;i=1", null);
            driver.CreateTag(td, td.Events["sensor"].Forms[0], "asset", 1, "sensor", "nsu=x;i=2", null);
            AssetTag gated = driver.CreateTag(td, td.Events["gated"].Forms[0], "asset", 1, "gated", "nsu=x;i=3", null);

            // Offset 1 is the aliased form, not offset 9 from the event name.
            Assert.Equal(1, asset.FieldRules.Get(gated.Name).DiscriminatorOffset);
        }

        [Fact]
        public void The_eventing_frequency_does_not_become_the_polling_interval()
        {
            // Polling at the uplink period would make a value that arrives just
            // after a poll wait almost a full period to be published, so worst
            // case staleness approaches TWICE the uplink interval. Reading is
            // also cheap - the asset serves the last decoded payload from
            // memory - so there is nothing to be saved by polling slowly.
            ThingDescription td = Thing("\"lorav:defaultEventingFrequencyMinutes\": 20");
            LoRaWANProtocolDriver driver = new();

            string form = """
            { "href": "uplink", "type": "Short", "lorav:byteOffset": 0, "lorav:wireType": "s16" }
            """;

            AssetTag tag = driver.CreateTag(td, JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null);

            Assert.Equal(1000, tag.PollingInterval);
            Assert.NotEqual(20 * 60 * 1000, tag.PollingInterval);
        }

        [Fact]
        public void The_eventing_frequency_is_still_readable_for_diagnostics()
        {
            // The term is not discarded: it states how stale a reading may
            // legitimately be, which is worth surfacing even though it must not
            // drive polling.
            ThingDescription td = Thing("\"lorav:defaultEventingFrequencyMinutes\": 20");

            Assert.Equal(20, LoRaWANProtocolDriver.ReadEventingFrequencyMinutes(td));
        }

        [Theory]
        [InlineData("\"lorav:defaultEventingFrequencyMinutes\": 0")]
        [InlineData("\"lorav:defaultEventingFrequencyMinutes\": -5")]
        [InlineData("\"lorav:defaultEventingFrequencyMinutes\": \"soon\"")]
        public void A_nonsensical_eventing_frequency_reads_as_absent(string json)
        {
            Assert.Null(LoRaWANProtocolDriver.ReadEventingFrequencyMinutes(Thing(json)));
        }

        [Fact]
        public void A_thing_without_an_eventing_frequency_keeps_the_default_interval()
        {
            LoRaWANProtocolDriver driver = new();

            string form = """
            { "href": "uplink", "type": "Short", "lorav:byteOffset": 0, "lorav:wireType": "s16" }
            """;

            AssetTag tag = driver.CreateTag(Thing(), JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null);

            Assert.Equal(1000, tag.PollingInterval);
        }
    }
}
