namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Xunit;

    /// <summary>
    /// Verifies the shipped LoRaWAN samples still onboard correctly after the
    /// binding vocabulary changes.
    /// <para>
    /// A sample is documentation that executes: if it drifts from the driver it
    /// teaches the wrong shape, and the failure only appears when someone copies
    /// it onto real hardware.
    /// </para>
    /// </summary>
    public class LoRaWANSampleAccuracyTests
    {
        public static IEnumerable<object[]> Samples()
        {
            yield return new object[] { "DraginoLHT65N.td.jsonld" };
            yield return new object[] { "MilesightEM300-TH.td.jsonld" };
        }

        private static ThingDescription Load(string fileName) =>
            JsonConvert.DeserializeObject<ThingDescription>(
                File.ReadAllText(FindSample(fileName)).Trim('\uFEFF'));

        [Theory]
        [MemberData(nameof(Samples))]
        public void Every_form_in_a_sample_creates_a_tag(string fileName)
        {
            // Onboarding runs CreateTag for every form, so anything the driver
            // would reject surfaces here rather than on a gateway.
            ThingDescription td = Load(fileName);
            LoRaWANProtocolDriver driver = new();

            Assert.NotNull(td.Events);
            Assert.NotEmpty(td.Events);

            foreach (KeyValuePair<string, TDEvent> tdEvent in td.Events)
            {
                foreach (object form in tdEvent.Value.Forms)
                {
                    AssetTag tag = driver.CreateTag(
                        td, form, "asset", 1, tdEvent.Key, "nsu=x;i=1", null);

                    Assert.NotNull(tag);
                    Assert.False(string.IsNullOrWhiteSpace(tag.Address));
                }
            }
        }

        [Theory]
        [MemberData(nameof(Samples))]
        public void A_sample_uses_no_withdrawn_term(string fileName)
        {
            string json = File.ReadAllText(FindSample(fileName));

            foreach (KeyValuePair<string, string> withdrawn in LoRaWANForm.WithdrawnTerms)
            {
                Assert.DoesNotContain(
                    "\"" + withdrawn.Key + "\"",
                    json,
                    StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_dragino_sample_addresses_its_four_fields_by_offset()
        {
            // A fixed-layout device: each value sits at a known byte position.
            ThingDescription td = Load("DraginoLHT65N.td.jsonld");
            LoRaWANProtocolDriver driver = new();

            (string Event, string Expected)[] expectations =
            [
                ("batteryLevel", "A84041B98D5CB233/0?quantity=2"),
                ("temperature", "A84041B98D5CB233/2?quantity=2"),
                ("humidity", "A84041B98D5CB233/4?quantity=2"),
                ("extTemperature", "A84041B98D5CB233/7?quantity=2")
            ];

            foreach ((string eventName, string expected) in expectations)
            {
                AssetTag tag = driver.CreateTag(
                    td, td.Events[eventName].Forms[0], "asset", 1, eventName, "nsu=x;i=1", null);

                Assert.Equal(expected, tag.Address);
            }
        }

        [Fact]
        public void The_milesight_sample_addresses_its_fields_by_tag()
        {
            // A ctv device: each value is found by scanning for its tag bytes.
            ThingDescription td = Load("MilesightEM300-TH.td.jsonld");
            LoRaWANProtocolDriver driver = new();

            (string Event, string Expected)[] expectations =
            [
                ("batteryLevel", "24E124136E440353/1/117?quantity=1"),
                ("temperature", "24E124136E440353/3/103?quantity=2"),
                ("humidity", "24E124136E440353/4/104?quantity=1")
            ];

            foreach ((string eventName, string expected) in expectations)
            {
                AssetTag tag = driver.CreateTag(
                    td, td.Events[eventName].Forms[0], "asset", 1, eventName, "nsu=x;i=1", null);

                Assert.Equal(expected, tag.Address);
            }
        }

        [Theory]
        [MemberData(nameof(Samples))]
        public void A_sample_declares_the_prefixes_its_terms_use(string fileName)
        {
            // A term whose prefix is not bound in @context is not the term it
            // looks like; it is an unresolved name that happens to contain a
            // colon.
            string json = File.ReadAllText(FindSample(fileName));
            JObject td = JObject.Parse(json.Trim('\uFEFF'));

            string context = td["@context"]?.ToString() ?? string.Empty;

            if (json.Contains("\"lorav:", StringComparison.Ordinal))
            {
                Assert.Contains("lorav", context, StringComparison.Ordinal);
            }

            if (json.Contains("\"schema:", StringComparison.Ordinal))
            {
                Assert.Contains("schema", context, StringComparison.Ordinal);
            }
        }

        [Theory]
        [MemberData(nameof(Samples))]
        public void A_sample_models_its_values_as_events(string fileName)
        {
            // The binding is uplink-only: a device transmits on its own
            // schedule, so values are events rather than readable properties.
            ThingDescription td = Load(fileName);

            Assert.NotEmpty(td.Events);
            Assert.True(
                (td.Properties is null) || (td.Properties.Count == 0),
                $"'{fileName}' declares properties; the LoRaWAN binding models values as events.");
        }

        [Theory]
        [MemberData(nameof(Samples))]
        public void A_sample_scales_with_a_divisor_rather_than_a_fractional_multiplier(string fileName)
        {
            // lorav:multiplier and lorav:divisor are mutually exclusive, and the
            // binding prefers the divisor: {"multiplier": 0.01} is not
            // representable in binary floating point, so 2730 centi-degrees
            // decodes as 27.299999... instead of 27.3.
            string json = File.ReadAllText(FindSample(fileName));

            Assert.DoesNotContain("\"lorav:multiplier\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void A_divisor_scales_exactly_where_a_fractional_multiplier_does_not()
        {
            // Demonstrates why the samples use divisors: the same reading,
            // scaled both ways.
            const short raw = 2730;

            double viaMultiplier = raw * (float)0.01;
            double viaDivisor = raw / 100.0;

            Assert.NotEqual(27.3, viaMultiplier, 6);
            Assert.Equal(27.3, viaDivisor, 6);
        }

        private static string FindSample(string fileName)
        {
            DirectoryInfo directory = new(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "Samples", fileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException($"Could not locate the sample '{fileName}'.");
        }
    }
}
