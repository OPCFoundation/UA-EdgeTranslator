namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.IO;
    using Xunit;

    /// <summary>
    /// Conformance tests for the binding's <c>tlv</c> and <c>ctv</c> layouts,
    /// built on the specification's worked examples
    /// (<c>leak-detector-tlv-01</c> and <c>indoor-sensor-ctv-01</c>).
    /// <para>
    /// In these layouts a value is located by scanning the payload for its
    /// <c>lorav:tag</c> bytes rather than by a fixed offset, because a device
    /// may omit a measurement or reorder measurements between uplinks.
    /// </para>
    /// </summary>
    public class LoRaWANTagLayoutTests
    {
        private static ThingDescription TaggedThing(string tagFieldsJson = null)
        {
            string json = $$"""
            {
              "base": "lorawan://localhost/0000000000000003/",
              "lorav:payloadLayout": "tlv"
              {{(tagFieldsJson is null ? string.Empty : "," + tagFieldsJson)}}
            }
            """;

            return JsonConvert.DeserializeObject<ThingDescription>(json);
        }

        [Fact]
        public void A_tagged_form_addresses_the_field_by_its_tag_bytes()
        {
            // From the spec's TLV example: battery is tag [1, 117], u8.
            LoRaWANProtocolDriver driver = new();

            string form = """
            {
              "href": "uplink",
              "op": [ "subscribeevent", "unsubscribeevent" ],
              "type": "Byte",
              "lorav:tag": [ 1, 117 ],
              "lorav:wireType": "u8"
            }
            """;

            AssetTag tag = driver.CreateTag(
                TaggedThing(), JToken.Parse(form), "asset", 1, "battery", "nsu=x;i=1", null);

            // The asset scans for [1][117] and reads the byte that follows.
            Assert.Equal("0000000000000003/1/117?quantity=1", tag.Address);
        }

        [Fact]
        public void A_two_byte_tagged_field_requests_two_bytes()
        {
            // Temperature in the spec's example is tag [3, 103], s16.
            LoRaWANProtocolDriver driver = new();

            string form = """
            {
              "href": "uplink",
              "type": "Short",
              "lorav:tag": [ 3, 103 ],
              "lorav:wireType": "s16",
              "lorav:divisor": 10
            }
            """;

            AssetTag tag = driver.CreateTag(
                TaggedThing(), JToken.Parse(form), "asset", 1, "temperature", "nsu=x;i=1", null);

            Assert.Equal("0000000000000003/3/103?quantity=2", tag.Address);
        }

        [Fact]
        public void A_tag_matching_the_declared_tag_fields_is_accepted()
        {
            LoRaWANProtocolDriver driver = new();

            const string tagFields = """
            "lorav:tagFields": [
              { "name": "channel_id", "type": "u8" },
              { "name": "channel_type", "type": "u8" }
            ]
            """;

            string form = """
            {
              "href": "uplink",
              "type": "Byte",
              "lorav:tag": [ 4, 104 ],
              "lorav:wireType": "u8"
            }
            """;

            AssetTag tag = driver.CreateTag(
                TaggedThing(tagFields), JToken.Parse(form), "asset", 1, "humidity", "nsu=x;i=1", null);

            Assert.Equal("0000000000000003/4/104?quantity=1", tag.Address);
        }

        [Fact]
        public void A_tag_disagreeing_with_the_declared_tag_fields_is_rejected()
        {
            // The Thing says a tag is two positions; a three-element tag
            // describes a different layout and would never match, so it fails
            // at onboarding rather than reading nothing forever.
            LoRaWANProtocolDriver driver = new();

            const string tagFields = """
            "lorav:tagFields": [
              { "name": "channel_id", "type": "u8" },
              { "name": "channel_type", "type": "u8" }
            ]
            """;

            string form = """
            {
              "href": "uplink",
              "type": "Byte",
              "lorav:tag": [ 1, 2, 3 ],
              "lorav:wireType": "u8"
            }
            """;

            NotSupportedException ex = Assert.Throws<NotSupportedException>(
                () => driver.CreateTag(
                    TaggedThing(tagFields), JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null));

            Assert.Contains("lorav:tag", ex.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("[ 1 ]")]
        [InlineData("[ 1, 2, 3 ]")]
        public void A_tag_that_is_not_two_elements_is_rejected(string tagJson)
        {
            // The asset matches exactly two tag bytes. Reading only the first
            // two elements of a longer tag would select the wrong field.
            LoRaWANProtocolDriver driver = new();

            string form = $$"""
            {
              "href": "uplink",
              "type": "Byte",
              "lorav:tag": {{tagJson}},
              "lorav:wireType": "u8"
            }
            """;

            Assert.Throws<NotSupportedException>(
                () => driver.CreateTag(
                    TaggedThing(), JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null));
        }

        [Fact]
        public void A_tag_value_too_large_for_a_byte_is_rejected()
        {
            // Tag positions are byte-sized; 300 would be truncated silently.
            LoRaWANProtocolDriver driver = new();

            string form = """
            {
              "href": "uplink",
              "type": "Byte",
              "lorav:tag": [ 1, 300 ],
              "lorav:wireType": "u8"
            }
            """;

            NotSupportedException ex = Assert.Throws<NotSupportedException>(
                () => driver.CreateTag(
                    TaggedThing(), JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null));

            Assert.Contains("300", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_tagged_form_ignores_any_path_the_author_wrote_in_the_href()
        {
            // The tag locates the field, so a leftover path in the href must not
            // end up in the address and change which field is read.
            LoRaWANProtocolDriver driver = new();

            string form = """
            {
              "href": "uplink/99",
              "type": "Byte",
              "lorav:tag": [ 1, 117 ],
              "lorav:wireType": "u8"
            }
            """;

            AssetTag tag = driver.CreateTag(
                TaggedThing(), JToken.Parse(form), "asset", 1, "battery", "nsu=x;i=1", null);

            Assert.Equal("0000000000000003/1/117?quantity=1", tag.Address);
        }

        [Fact]
        public void The_ctv_sample_resolves_to_the_same_addresses_as_before()
        {
            // The Milesight sample moved from hand-encoded hrefs like
            // "1/117?quantity=1" to declarative lorav:tag. The generated
            // addresses must be identical, otherwise the conversion silently
            // changed which bytes are read.
            string path = FindSample("MilesightEM300-TH.td.jsonld");

            ThingDescription td =
                JsonConvert.DeserializeObject<ThingDescription>(File.ReadAllText(path).Trim('\uFEFF'));

            LoRaWANProtocolDriver driver = new();

            (string Event, string Expected)[] expectations =
            [
                ("batteryLevel", "24E124136E440353/1/117?quantity=1"),
                ("temperature", "24E124136E440353/3/103?quantity=2"),
                ("humidity", "24E124136E440353/4/104?quantity=1")
            ];

            foreach ((string eventName, string expected) in expectations)
            {
                object form = td.Events[eventName].Forms[0];

                AssetTag tag = driver.CreateTag(td, form, "asset", 1, eventName, "nsu=x;i=1", null);

                Assert.Equal(expected, tag.Address);
            }
        }

        [Fact]
        public void The_ctv_sample_declares_its_tag_composition()
        {
            string path = FindSample("MilesightEM300-TH.td.jsonld");

            JObject td = JObject.Parse(File.ReadAllText(path).Trim('\uFEFF'));

            Assert.Equal("ctv", td["lorav:payloadLayout"]?.ToString());

            JArray tagFields = (JArray)td["lorav:tagFields"];

            Assert.NotNull(tagFields);
            Assert.Equal(2, tagFields.Count);
            Assert.Equal("channel_id", tagFields[0]["name"]?.ToString());
            Assert.Equal("channel_type", tagFields[1]["name"]?.ToString());
        }

        [Fact]
        public void Tag_fields_is_no_longer_rejected()
        {
            // It was previously in UnsupportedTerms on the mistaken basis that
            // the specification had not defined it.
            Assert.DoesNotContain("lorav:tagFields", LoRaWANForm.UnsupportedTerms);
            Assert.DoesNotContain("lorav:tag", LoRaWANForm.UnsupportedTerms);
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
