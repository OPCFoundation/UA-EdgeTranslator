namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using Xunit;

    /// <summary>
    /// Conformance tests for the official W3C WoT LoRaWAN binding
    /// (<c>https://www.w3.org/2026/wot/lorawan#</c>).
    /// <para>
    /// The worked example is taken verbatim from the specification: a
    /// temperature sensor whose value sits at byte offset 2 as a big-endian
    /// <c>xsd:short</c> with a divisor of 100, so the payload
    /// <c>01 07 6B 0C CC</c> decodes 0x076B (1899) to 18.99 °C.
    /// </para>
    /// </summary>
    public class LoRaWANBindingConformanceTests
    {
        /// <summary>The temperature event form from the specification's example TD.</summary>
        private const string _specExampleForm = """
        {
          "href": "uplink",
          "op": ["subscribeevent", "unsubscribeevent"],
          "type": "Float",
          "lorav:byteOffset": 2,
          "lorav:wireType": "xsd:short",
          "lorav:endian": "big",
          "lorav:divisor": 100
        }
        """;

        [Fact]
        public void Driver_advertises_the_official_binding_namespace()
        {
            LoRaWANProtocolDriver driver = new();

            // A Thing Description author matches on this value, so it has to be
            // the published namespace rather than a placeholder.
            Assert.Equal("https://www.w3.org/2026/wot/lorawan#", driver.WoTBindingUri);
            Assert.Equal(LoRaWANForm.BindingNamespace, driver.WoTBindingUri);
        }

        [Fact]
        public void Spec_example_form_deserialises_with_the_official_terms()
        {
            LoRaWANForm form = JsonConvert.DeserializeObject<LoRaWANForm>(_specExampleForm);

            Assert.Equal("uplink", form.Href);
            Assert.Equal(2, form.ByteOffset);
            Assert.Equal("xsd:short", form.WireType);
            Assert.Equal("big", form.Endian);
            Assert.Equal(100f, form.Divisor);
        }

        [Fact]
        public void Spec_example_scaling_decodes_the_documented_value()
        {
            LoRaWANForm form = JsonConvert.DeserializeObject<LoRaWANForm>(_specExampleForm);

            // Payload 01 07 6B 0C CC -> bytes 2..3 are 0x076B = 1899 raw.
            const short raw = 0x076B;

            float? scale = form.EffectiveMultiplier();

            Assert.NotNull(scale);

            float decoded = raw * scale.Value;

            // The specification states this decodes to 18.99 degrees Celsius.
            Assert.Equal(18.99f, decoded, 2);
        }

        [Theory]
        [InlineData("big", true)]
        [InlineData("BIG", true)]
        [InlineData("little", false)]
        [InlineData("LITTLE", false)]
        public void Endian_term_controls_byte_order(string endian, bool expectedBigEndian)
        {
            LoRaWANForm form = new() { Endian = endian };

            Assert.Equal(expectedBigEndian, form.IsBigEndian());
        }

        [Fact]
        public void Byte_order_defaults_to_big_endian_when_the_term_is_absent()
        {
            // The binding's own examples and typical device datasheets use
            // big-endian multi-byte fields, so that is the default.
            Assert.True(new LoRaWANForm().IsBigEndian());
        }

        [Fact]
        public void Pre_standard_byte_order_terms_are_no_longer_part_of_the_model()
        {
            // The driver implements the published vocabulary only. The old
            // lorav:mostSignificantByte / lorav:mostSignificantWord terms are
            // not in the specification and must not reappear.
            Assert.Null(typeof(LoRaWANForm).GetProperty("MostSignificantByte"));
            Assert.Null(typeof(LoRaWANForm).GetProperty("MostSignificantWord"));
        }

        [Fact]
        public void A_form_using_pre_standard_terms_ignores_them()
        {
            // A stale Thing Description does not crash, but its non-standard
            // terms carry no meaning: byte order falls back to the default
            // rather than being silently honoured.
            const string legacy = """
            {
              "href": "uplink",
              "type": "Float",
              "lorav:mostSignificantByte": false,
              "lorav:mostSignificantWord": false
            }
            """;

            LoRaWANForm form = JsonConvert.DeserializeObject<LoRaWANForm>(legacy);

            Assert.Null(form.Endian);
            Assert.True(form.IsBigEndian());
        }

        [Theory]
        [InlineData(null, null, null)]      // neither specified
        [InlineData(2.0f, null, 2.0f)]      // multiplier only
        [InlineData(null, 100f, 0.01f)]     // divisor only
        [InlineData(3.0f, 2.0f, 1.5f)]      // both combined
        public void Multiplier_and_divisor_combine(float? multiplier, float? divisor, float? expected)
        {
            LoRaWANForm form = new() { Multiplier = multiplier, Divisor = divisor };

            float? actual = form.EffectiveMultiplier();

            if (expected is null)
            {
                Assert.Null(actual);
            }
            else
            {
                Assert.Equal(expected.Value, actual.Value, 4);
            }
        }

        [Fact]
        public void A_zero_divisor_does_not_produce_infinity()
        {
            // Guards a malformed TD from silently turning every reading into
            // Infinity or NaN.
            LoRaWANForm form = new() { Multiplier = 5.0f, Divisor = 0f };

            float? scale = form.EffectiveMultiplier();

            Assert.NotNull(scale);
            Assert.False(float.IsInfinity(scale.Value));
            Assert.False(float.IsNaN(scale.Value));
        }

        [Fact]
        public void Thing_level_vocabulary_deserialises()
        {
            // Thing-level terms from the specification's example TD.
            const string thing = """
            {
              "lorav:payloadLayout": "fixed",
              "lorav:devEUI": "0000000000000001",
              "lorav:joinEUI": "0000000000000000",
              "lorav:macVersion": "1.0.3"
            }
            """;

            LoRaWANThingDescription td = JsonConvert.DeserializeObject<LoRaWANThingDescription>(thing);

            Assert.Equal("fixed", td.PayloadLayout);
            Assert.Equal("0000000000000001", td.DevEUI);
            Assert.Equal("0000000000000000", td.JoinEUI);
            Assert.Equal("1.0.3", td.MacVersion);
        }

        [Fact]
        public void CreateTag_maps_the_spec_example_onto_an_asset_tag()
        {
            LoRaWANProtocolDriver driver = new();
            ThingDescription td = new() { Base = "lorawan://0000000000000001/appkey/device" };

            AssetTag tag = driver.CreateTag(td, JToken.Parse(_specExampleForm), "asset", 1, "temperature", "nsu=x;i=1", null);

            Assert.Equal("temperature", tag.Name);
            Assert.True(tag.IsBigEndian);

            // divisor 100 becomes a 0.01 multiplier on the tag.
            Assert.Equal(0.01f, tag.Multiplier, 4);
        }

        [Theory]
        // href already carries the offset; no binding terms to fold in.
        [InlineData("""{"href":"A84041B98D5CB233/2?quantity=2","type":"xsd:short"}""", 4)]
        // byteOffset/byteLength are folded into the href's own scheme.
        [InlineData("""{"href":"A84041B98D5CB233","type":"xsd:short","lorav:byteOffset":2,"lorav:byteLength":2}""", 4)]
        // Channel-style address keeps its extra segment.
        [InlineData("""{"href":"24E124136E440353/3/103?quantity=2","type":"xsd:short"}""", 5)]
        public void Tag_address_stays_parseable_by_the_asset(string form, int expectedParts)
        {
            // LoRaWANNetworkServerAsset splits the address on [?&=/] and
            // dispatches on the number of parts (4 or 5). An address that
            // produces any other count is silently never read, so the shape of
            // the generated address is load-bearing.
            LoRaWANProtocolDriver driver = new();
            ThingDescription td = new() { Base = "lorawan://0000000000000001/appkey/device" };

            AssetTag tag = driver.CreateTag(td, JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null);

            string[] parts = tag.Address.Split(['?', '&', '=', '/']);

            Assert.Equal(expectedParts, parts.Length);
            Assert.True(parts.Length is 4 or 5, $"Address '{tag.Address}' splits into {parts.Length} parts; the asset only handles 4 or 5.");
        }

        [Fact]
        public void Byte_offset_and_length_are_folded_into_the_address()
        {
            LoRaWANProtocolDriver driver = new();
            ThingDescription td = new() { Base = "lorawan://0000000000000001/appkey/device" };

            const string form = """
            {
              "href": "A84041B98D5CB233",
              "type": "xsd:short",
              "lorav:byteOffset": 7,
              "lorav:byteLength": 2
            }
            """;

            AssetTag tag = driver.CreateTag(td, JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null);

            Assert.Equal("A84041B98D5CB233/7?quantity=2", tag.Address);
        }

        [Theory]
        [InlineData("xsd:byte", 1)]
        [InlineData("xsd:short", 2)]
        [InlineData("xsd:int", 4)]
        [InlineData("xsd:long", 8)]
        public void Byte_length_defaults_from_the_wire_type(string wireType, int expectedQuantity)
        {
            // lorav:byteLength is optional; the width then follows from
            // lorav:wireType rather than silently reading the wrong span.
            LoRaWANProtocolDriver driver = new();
            ThingDescription td = new() { Base = "lorawan://0000000000000001/appkey/device" };

            string form = $$"""
            {
              "href": "A84041B98D5CB233",
              "type": "xsd:short",
              "lorav:byteOffset": 0,
              "lorav:wireType": "{{wireType}}"
            }
            """;

            AssetTag tag = driver.CreateTag(td, JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null);

            Assert.EndsWith($"quantity={expectedQuantity}", tag.Address, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("lorav:compute")]
        [InlineData("lorav:derived")]
        [InlineData("lorav:switchField")]
        [InlineData("lorav:presentWhen")]
        [InlineData("lorav:presenceBit")]
        public void Unsupported_binding_terms_are_rejected_rather_than_ignored(string term)
        {
            // These change how the payload must be read. Ignoring them would
            // produce a confidently wrong value instead of an error.
            LoRaWANProtocolDriver driver = new();
            ThingDescription td = new() { Base = "lorawan://0000000000000001/appkey/device" };

            string form = $$"""
            {
              "href": "uplink",
              "type": "Float",
              "lorav:byteOffset": 0,
              "{{term}}": "something"
            }
            """;

            NotSupportedException ex = Assert.Throws<NotSupportedException>(
                () => driver.CreateTag(td, JToken.Parse(form), "asset", 1, "value", "nsu=x;i=1", null));

            Assert.Contains(term, ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_form_using_only_supported_terms_is_accepted()
        {
            // The negative case for the guard above: the spec example itself
            // must not be rejected.
            LoRaWANProtocolDriver driver = new();
            ThingDescription td = new() { Base = "lorawan://0000000000000001/appkey/device" };

            AssetTag tag = driver.CreateTag(td, JToken.Parse(_specExampleForm), "asset", 1, "temperature", "nsu=x;i=1", null);

            Assert.NotNull(tag);
        }

        [Theory]
        [InlineData("DraginoLHT65N.td.jsonld")]
        [InlineData("MilesightEM300-TH.td.jsonld")]
        public void Shipped_lorawan_samples_conform_to_the_binding(string fileName)
        {
            // The samples are what users copy, so they have to be conformant and
            // they have to survive onboarding.
            JObject td = JObject.Parse(File.ReadAllText(FindSample(fileName)));

            // Conformance requires the namespace be declared in @context.
            Assert.Contains(LoRaWANForm.BindingNamespace, td["@context"].ToString(), StringComparison.Ordinal);

            LoRaWANProtocolDriver driver = new();
            ThingDescription parsed = JsonConvert.DeserializeObject<ThingDescription>(td.ToString());

            // The binding models uplink values as events, not properties, so a
            // conformant sample carries no property affordances at all.
            Assert.True(
                parsed.Properties is null || parsed.Properties.Count == 0,
                $"{fileName} declares properties; the binding models uplinks as events.");

            Assert.NotNull(parsed.Events);
            Assert.NotEmpty(parsed.Events);

            foreach (KeyValuePair<string, TDEvent> tdEvent in parsed.Events)
            {
                Assert.NotNull(tdEvent.Value.Forms);

                foreach (object form in tdEvent.Value.Forms)
                {
                    AssetTag tag = driver.CreateTag(parsed, form, "asset", 1, tdEvent.Key, "nsu=x;i=1", null);

                    // Every generated address must be one the asset can parse.
                    string[] parts = tag.Address.Split(['?', '&', '=', '/']);

                    Assert.True(
                        parts.Length is 4 or 5,
                        $"{fileName}/{tdEvent.Key}: address '{tag.Address}' splits into {parts.Length} parts; the asset handles only 4 or 5.");
                }
            }
        }

        [Fact]
        public void Binding_terms_win_when_the_href_also_encodes_a_location()
        {
            // A form should not state the field location twice, but if it does,
            // the specification's terms are authoritative and any offset baked
            // into the href is discarded. Otherwise the two could disagree and
            // the driver would silently read the wrong bytes.
            LoRaWANProtocolDriver driver = new();
            ThingDescription td = new() { Base = "lorawan://0000000000000001/appkey/device" };

            const string conflicting = """
            {
              "href": "A84041B98D5CB233/0?quantity=2",
              "type": "xsd:short",
              "lorav:byteOffset": 7,
              "lorav:byteLength": 4
            }
            """;

            AssetTag tag = driver.CreateTag(td, JToken.Parse(conflicting), "asset", 1, "value", "nsu=x;i=1", null);

            Assert.Equal("A84041B98D5CB233/7?quantity=4", tag.Address);
        }

        [Theory]
        [InlineData("DraginoLHT65N.td.jsonld")]
        [InlineData("MilesightEM300-TH.td.jsonld")]
        public void Shipped_lorawan_samples_state_the_field_location_only_once(string fileName)
        {
            // The href is the transport endpoint; lorav:byteOffset and
            // lorav:byteLength say where the value sits inside the payload. The
            // specification's own example keeps these separate ("href":
            // "uplink"), and stating the offset in both places invites the two
            // to drift apart.
            JObject td = JObject.Parse(File.ReadAllText(FindSample(fileName)));

            foreach (JProperty affordance in td["events"].Children<JProperty>())
            {
                foreach (JToken form in affordance.Value["forms"])
                {
                    if (form["lorav:byteOffset"] is null)
                    {
                        continue;
                    }

                    string href = form["href"].ToString();

                    Assert.False(
                        href.Contains("quantity=", StringComparison.Ordinal),
                        $"{fileName}/{affordance.Name}: href '{href}' also encodes the field location that lorav:byteOffset/byteLength already state.");
                }
            }
        }

        [Theory]
        [InlineData("DraginoLHT65N.td.jsonld")]
        [InlineData("MilesightEM300-TH.td.jsonld")]
        public void Shipped_lorawan_samples_use_the_subscribe_event_operation(string fileName)
        {
            // subscribeevent is the operation the binding emphasises; a form
            // without it does not describe a LoRaWAN uplink.
            JObject td = JObject.Parse(File.ReadAllText(FindSample(fileName)));

            foreach (JProperty affordance in td["events"].Children<JProperty>())
            {
                foreach (JToken form in affordance.Value["forms"])
                {
                    string ops = form["op"]?.ToString() ?? string.Empty;

                    Assert.Contains("subscribeevent", ops, StringComparison.Ordinal);
                }
            }
        }

        [Theory]
        // The form the specification's own examples use: interface + device,
        // with the operation target supplied by each form's relative href.
        [InlineData("lorawan://ns.example.org/0000000000000001/", "ns.example.org", "0000000000000001", "uplink")]
        [InlineData("lorawan://localhost/A84041B98D5CB233/", "localhost", "A84041B98D5CB233", "uplink")]
        // An explicit target is still accepted; that is the fully resolved URI.
        [InlineData("lorawan://ns.example.org/0000000000000001/uplink", "ns.example.org", "0000000000000001", "uplink")]
        [InlineData("lorawan://ns.example.org/0000000000000001/downlink", "ns.example.org", "0000000000000001", "downlink")]
        public void Base_uri_follows_the_binding_abnf(string uri, string authority, string devEui, string target)
        {
            // lorawan-URI = "lorawan://" authority "/" dev-eui "/" op-target
            LoRaWANUri parsed = LoRaWANUri.Parse(uri);

            Assert.Equal(authority, parsed.Authority);
            Assert.Equal(devEui, parsed.DevEUI);
            Assert.Equal(target, parsed.Target);
        }

        [Theory]
        // The pre-standard shape: devEUI in the authority position, key embedded.
        [InlineData("lorawan://A84041B98D5CB233/569F632C48146DBB92E7ECA0859F12A7/device")]
        // Not 16 hex digits.
        [InlineData("lorawan://host/NOTAHEXEUI/uplink")]
        // Not an operation target the binding defines.
        [InlineData("lorawan://host/0000000000000001/device")]
        // Too few segments to identify a device.
        [InlineData("lorawan://host")]
        [InlineData("")]
        [InlineData(null)]
        public void Malformed_base_uris_are_rejected(string uri)
        {
            Assert.Throws<ArgumentException>(() => LoRaWANUri.Parse(uri));
        }

        [Fact]
        public void The_router_config_gateway_uri_still_parses()
        {
            // Gateway provisioning predates the binding and is specific to this
            // translator; conforming the device URI must not break it.
            LoRaWANUri parsed = LoRaWANUri.Parse("lorawan://A84041B98D5CB233/basicstation/routerconfig");

            Assert.True(parsed.IsRouterConfig);
            Assert.Equal("A84041B98D5CB233", parsed.DevEUI);
            Assert.Equal("basicstation", parsed.GatewayModel);
        }

        [Fact]
        public void The_sample_router_config_is_a_plain_lns_message()
        {
            // A router configuration describes the gateway's radio hardware and
            // has no WoT affordances, so it is stored as plain JSON rather than
            // wrapped in a Thing Description that would be onboarded as an asset.
            JObject config = JObject.Parse(File.ReadAllText(FindSample("SX1301EU.json")));

            Assert.Equal("router_config", config["msgtype"].ToString());

            // The fields Basic Station requires to bring the radio up.
            Assert.NotNull(config["region"]);
            Assert.NotNull(config["hwspec"]);
            Assert.NotNull(config["freq_range"]);
            Assert.NotNull(config["DRs"]);
            Assert.NotNull(config["sx1301_conf"]);

            // It must NOT be a Thing Description.
            Assert.Null(config["@context"]);
            Assert.Null(config["properties"]);
            Assert.Null(config["securityDefinitions"]);
        }

        [Fact]
        public void Every_router_config_declares_the_radio_abstraction_layer_hwspec()
        {
            // 'hwspec' names Basics Station's radio abstraction layer, not the
            // concentrator chip. A station built for an SX1302 (platform=corecell)
            // still expects 'sx1301/1' and rejects 'sx1302/1' outright with
            // "Unsupported hwspec", leaving the gateway reconnecting in a loop
            // with a valid-looking configuration.
            foreach (string path in Directory.EnumerateFiles(FindSamplesFolder(), "*.json"))
            {
                JObject config = JObject.Parse(File.ReadAllText(path));

                if (!string.Equals(config["msgtype"]?.ToString(), "router_config", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.Equal("sx1301/1", config["hwspec"]?.ToString());

                // The channel plan key is named after the same abstraction layer.
                Assert.NotNull(config["sx1301_conf"]);
            }
        }

        private static string FindSamplesFolder()
        {
            DirectoryInfo directory = new(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "Samples");

                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the 'Samples' folder.");
        }

        [Fact]
        public void No_router_config_thing_description_remains()
        {
            // A .jsonld file in the settings folder is auto-onboarded as an
            // asset, which is exactly what a router configuration is not.
            DirectoryInfo directory = new(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string samples = Path.Combine(directory.FullName, "Samples");

                if (Directory.Exists(samples))
                {
                    Assert.False(
                        File.Exists(Path.Combine(samples, "SX1301EU.td.jsonld")),
                        "The router configuration must be a plain .json file, not a Thing Description.");

                    return;
                }

                directory = directory.Parent;
            }
        }

        [Fact]
        public void A_thing_description_embedding_an_otaa_key_is_rejected()
        {
            // The binding states root keys "MUST NOT be embedded in base, href,
            // or URI query strings". Enforce it rather than only documenting it.
            LoRaWANProtocolDriver driver = new();

            ThingDescription td = new()
            {
                Base = "lorawan://A84041B98D5CB233/569F632C48146DBB92E7ECA0859F12A7/device"
            };

            NotSupportedException ex = Assert.ThrowsAsync<NotSupportedException>(
                () => driver.CreateAndConnectAssetAsync(td)).GetAwaiter().GetResult();

            // The message must tell the operator what to do instead, not just
            // that the Thing Description was refused.
            Assert.Contains("forbids", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LORAWAN_APPKEY", ex.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("569F632C48146DBB92E7ECA0859F12A7", true)]   // 32 hex = 128-bit key
        [InlineData("5572404c696e6b4c6f52613230313823", true)]
        [InlineData("A84041B98D5CB233", false)]                  // 16 hex = devEUI
        [InlineData("localhost", false)]
        [InlineData("uplink", false)]
        public void Secret_detection_distinguishes_keys_from_identifiers(string value, bool expected)
        {
            Assert.Equal(expected, LoRaWANUri.LooksLikeSecret(value));
        }

        [Theory]
        [InlineData("DraginoLHT65N.td.jsonld")]
        [InlineData("MilesightEM300-TH.td.jsonld")]
        public void Shipped_lorawan_samples_contain_no_secrets(string fileName)
        {
            // These files are published in a public repository, so a leaked key
            // would be a real disclosure, not just a conformance slip.
            string json = File.ReadAllText(FindSample(fileName));

            foreach (Match match in Regex.Matches(json, "[0-9A-Fa-f]{32}"))
            {
                Assert.Fail($"{fileName} contains what looks like a 128-bit key: '{match.Value}'.");
            }
        }

        [Theory]
        [InlineData("DraginoLHT65N.td.jsonld")]
        [InlineData("MilesightEM300-TH.td.jsonld")]
        public void Shipped_lorawan_samples_declare_the_otaa_security_scheme(string fileName)
        {
            // The binding requires root keys be declared as apikey schemes with
            // the conventional name appKey.
            JObject td = JObject.Parse(File.ReadAllText(FindSample(fileName)));

            JToken scheme = td["securityDefinitions"]["otaa_sc"];

            Assert.NotNull(scheme);
            Assert.Equal("apikey", scheme["scheme"].ToString());
            Assert.Equal("appKey", scheme["name"].ToString());
        }

        [Theory]
        [InlineData("DraginoLHT65N.td.jsonld")]
        [InlineData("MilesightEM300-TH.td.jsonld")]
        public void Shipped_lorawan_samples_use_a_conformant_base_uri(string fileName)
        {
            JObject td = JObject.Parse(File.ReadAllText(FindSample(fileName)));

            string baseUri = td["base"].ToString();
            LoRaWANUri parsed = LoRaWANUri.Parse(baseUri);

            // The device identity must also be stated as a thing-level term,
            // and the two must agree.
            Assert.Equal(td["lorav:devEUI"].ToString(), parsed.DevEUI);

            // A Thing "sets base once and uses the relative targets uplink and
            // downlink in each form", so the operation target belongs to the
            // form rather than being repeated in the base.
            Assert.DoesNotContain(LoRaWANUri.UplinkTarget, baseUri, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(LoRaWANUri.DownlinkTarget, baseUri, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("DraginoLHT65N.td.jsonld")]
        [InlineData("MilesightEM300-TH.td.jsonld")]
        public void Shipped_lorawan_samples_use_no_retired_terms(string fileName)
        {
            string json = File.ReadAllText(FindSample(fileName));

            Assert.DoesNotContain("mostSignificantByte", json, StringComparison.Ordinal);
            Assert.DoesNotContain("mostSignificantWord", json, StringComparison.Ordinal);
        }

        private static string FindSample(string fileName)
        {
            // Walk up from the test binary to the repository's Samples folder.
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
