namespace UAEdgeTranslator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Xunit;

    /// <summary>
    /// The Siemens driver against the W3C WoT S7Comm binding
    /// (https://w3c.github.io/wot-binding-templates/bindings/protocols/s7comm/).
    /// </summary>
    public class S7BindingTests
    {
        private static S7Form Form(string href, S7DataType? type = null, params Op[] ops) => new()
        {
            Href = href,
            Op = ops.Length == 0 ? [Op.Readproperty] : ops,
            Type = type
        };

        [Theory]
        [InlineData("s7://192.168.0.10/0,1/", "192.168.0.10", 102, 0, 1)]
        [InlineData("s7://192.168.0.10/0,2/", "192.168.0.10", 102, 0, 2)]
        [InlineData("s7://192.168.0.10/1,3", "192.168.0.10", 102, 1, 3)]
        [InlineData("s7://192.168.0.10", "192.168.0.10", 102, 0, 1)]
        [InlineData("s7://plc.local:1102/0,1/", "plc.local", 1102, 0, 1)]
        [InlineData("s7://192.168.0.10/0,1/%25DB1.DBW20:INT", "192.168.0.10", 102, 0, 1)]
        public void Endpoints_follow_the_s7_uri_scheme(string uri, string host, int port, int rack, int slot)
        {
            S7Endpoint endpoint = SiemensProtocolDriver.ParseEndpoint(uri);
            Assert.Equal(host, endpoint.Host);
            Assert.Equal(port, endpoint.Port);
            Assert.Equal(rack, endpoint.Rack);
            Assert.Equal(slot, endpoint.Slot);
        }

        [Fact]
        public void Base_uri_ends_with_a_slash_and_omits_the_default_port()
        {
            Assert.Equal("s7://192.168.0.10/0,1/", new S7Endpoint("192.168.0.10").ToBaseUri());
            Assert.Equal("s7://plc:1102/0,2/", new S7Endpoint("plc", 1102, 0, 2).ToBaseUri());
            Assert.Throws<ArgumentException>(() => SiemensProtocolDriver.ParseEndpoint("opc.tcp://plc/"));
        }

        [Theory]
        [InlineData("%I0.3:BOOL", S7MemoryArea.I, null, 0, 3, S7DataType.BOOL)]
        [InlineData("%E0.3:BOOL", S7MemoryArea.I, null, 0, 3, S7DataType.BOOL)]
        [InlineData("%Q1.0:BOOL", S7MemoryArea.Q, null, 1, 0, S7DataType.BOOL)]
        [InlineData("%A1.0:BOOL", S7MemoryArea.Q, null, 1, 0, S7DataType.BOOL)]
        [InlineData("%M100:DINT", S7MemoryArea.M, null, 100, null, S7DataType.DINT)]
        [InlineData("%MD100:DINT", S7MemoryArea.M, null, 100, null, S7DataType.DINT)]
        [InlineData("%IW64:INT", S7MemoryArea.I, null, 64, null, S7DataType.INT)]
        [InlineData("%DB1.DBX0.3:BOOL", S7MemoryArea.DB, 1, 0, 3, S7DataType.BOOL)]
        [InlineData("%DB1.DBW20:INT", S7MemoryArea.DB, 1, 20, null, S7DataType.INT)]
        [InlineData("%DB5:10:INT", S7MemoryArea.DB, 5, 10, null, S7DataType.INT)]
        [InlineData("%DBI3.DBD4:REAL", S7MemoryArea.DBI, 3, 4, null, S7DataType.REAL)]
        [InlineData("%C5:WORD", S7MemoryArea.C, null, 5, null, S7DataType.WORD)]
        [InlineData("%Z5:WORD", S7MemoryArea.C, null, 5, null, S7DataType.WORD)]
        [InlineData("%T2:S5TIME", S7MemoryArea.T, null, 2, null, S7DataType.S5TIME)]
        [InlineData("%D10:INT", S7MemoryArea.D, null, 10, null, S7DataType.INT)]
        [InlineData("%LD4:DINT", S7MemoryArea.LD, null, 4, null, S7DataType.DINT)]
        public void Addresses_are_parsed(string text, S7MemoryArea area, int? db, int byteOffset, int? bit, S7DataType type)
        {
            Assert.Null(S7Address.TryParse(text, out S7Address address));
            Assert.Equal(area, address.Area);
            Assert.Equal(db, address.DbNumber);
            Assert.Equal(byteOffset, address.ByteOffset);
            Assert.Equal(bit, address.BitOffset);
            Assert.Equal(type, address.Type);
        }

        [Fact]
        public void String_addresses_carry_their_length()
        {
            Assert.Null(S7Address.TryParse("%DB1.DBB0:STRING(80)", out S7Address address));
            Assert.Equal(S7DataType.STRING, address.Type);
            Assert.Equal(80, address.Length);
            Assert.Equal(82, SiemensAsset.WireSize(address));
            Assert.Equal("%DB1.DBB0:STRING(80)", address.ToString());
        }

        [Theory]
        [InlineData("DB1.DBW20:INT")]
        [InlineData("%DB1.DBW20")]
        [InlineData("%X1:INT")]
        [InlineData("%DB1.DBW20:FOO")]
        [InlineData("%DB1.DBW20.3:INT")]
        [InlineData("%DB1.DBW:INT")]
        public void Malformed_addresses_are_rejected(string text)
        {
            Assert.NotNull(S7Address.TryParse(text, out _));
        }

        [Fact]
        public void Hrefs_are_percent_encoded_relative_references()
        {
            Assert.Equal("./%25DB1.DBW20:INT", S7Address.ToHref("%DB1.DBW20:INT"));
            Assert.Equal("%DB1.DBW20:INT", S7Address.FromHref("./%25DB1.DBW20:INT"));
            Assert.Equal("%DB1.DBX0.3:BOOL", S7Address.FromHref("s7://192.168.0.10/0,1/%25DB1.DBX0.3:BOOL"));
            Assert.Null(S7Address.FromHref("TankLevel"));
        }

        [Fact]
        public void An_href_address_or_explicit_terms_are_required()
        {
            Assert.Null(Form("./%25DB1.DBW20:INT", S7DataType.INT).Validate());
            Assert.NotNull(Form("TankLevel", S7DataType.INT).Validate());

            S7Form explicitTerms = Form("TankLevel", S7DataType.INT);
            explicitTerms.Area = S7MemoryArea.DB;
            explicitTerms.DbNumber = 1;
            explicitTerms.ByteOffset = 20;
            Assert.Null(explicitTerms.Validate());
        }

        [Fact]
        public void Explicit_terms_must_match_the_href_address()
        {
            S7Form same = Form("./%25DB1.DBW20:INT", S7DataType.INT);
            same.Area = S7MemoryArea.DB;
            same.DbNumber = 1;
            same.ByteOffset = 20;
            Assert.Null(same.Validate());

            S7Form different = Form("./%25DB1.DBW20:INT", S7DataType.INT);
            different.Area = S7MemoryArea.DB;
            different.DbNumber = 2;
            different.ByteOffset = 20;
            Assert.NotNull(different.Validate());

            Assert.NotNull(Form("./%25DB1.DBW20:INT", S7DataType.REAL).Validate());
        }

        [Fact]
        public void Explicit_terms_follow_their_assignment_rules()
        {
            S7Form noDb = Form("x", S7DataType.INT);
            noDb.Area = S7MemoryArea.DB;
            noDb.ByteOffset = 0;
            Assert.NotNull(noDb.Validate());

            S7Form noOffset = Form("x", S7DataType.INT);
            noOffset.Area = S7MemoryArea.M;
            Assert.NotNull(noOffset.Validate());

            S7Form boolWithoutBit = Form("x", S7DataType.BOOL);
            boolWithoutBit.Area = S7MemoryArea.M;
            boolWithoutBit.ByteOffset = 0;
            Assert.NotNull(boolWithoutBit.Validate());

            boolWithoutBit.BitOffset = 8;
            Assert.NotNull(boolWithoutBit.Validate());

            boolWithoutBit.BitOffset = 7;
            Assert.Null(boolWithoutBit.Validate());
        }

        [Fact]
        public void Operations_services_and_scale_follow_the_binding()
        {
            Assert.NotNull(Form("./%25M0:INT", S7DataType.INT, Op.Invokeaction).Validate());
            Assert.NotNull(Form("./%25M0:INT", S7DataType.INT, Op.Subscribeevent).Validate());

            S7Form read = Form("./%25M0:INT", S7DataType.INT, Op.Readproperty, Op.Observeproperty);
            read.Service = S7Services.ReadVariable;
            Assert.Null(read.Validate());

            S7Form mixed = Form("./%25M0:INT", S7DataType.INT, Op.Readproperty, Op.Writeproperty);
            mixed.Service = S7Services.WriteVariable;
            Assert.NotNull(mixed.Validate());

            S7Form unknown = Form("./%25M0:INT", S7DataType.INT);
            unknown.Service = "ReadSZL";
            Assert.NotNull(unknown.Validate());

            S7Form zero = Form("./%25M0:INT", S7DataType.INT);
            zero.Scale = 0;
            Assert.NotNull(zero.Validate());

            S7Form stringScale = Form("./%25DB1.DBB0:STRING", S7DataType.STRING);
            stringScale.Scale = 2;
            Assert.NotNull(stringScale.Validate());
        }

        [Fact]
        public void Value_conversion_matches_the_binding_example()
        {
            // value = raw x scale + offset; raw 27648 is the analog full scale.
            S7Form form = Form("./%25IW64:INT", S7DataType.INT);
            form.Scale = 0.0036169;
            form.Offset = -50;
            Assert.Equal(27648 * 0.0036169 - 50, (double)form.ToApplicationValue((short)27648), 6);
            Assert.Equal(27648d, form.ToControllerValue(27648 * 0.0036169 - 50, S7DataType.INT));
            Assert.Equal(32767d, form.ToControllerValue(1000.0, S7DataType.INT));
        }

        [Fact]
        public void Controller_families_limit_types_and_areas()
        {
            Assert.Null(Form("./%25DB1.DBX0:LREAL", S7DataType.LREAL).Validate(S7Controller.S7_1200));
            Assert.NotNull(Form("./%25DB1.DBX0:LREAL", S7DataType.LREAL).Validate(S7Controller.S7_300));
            Assert.NotNull(Form("./%25DB1.DBX0:LINT", S7DataType.LINT).Validate(S7Controller.S7_1200));
            Assert.Null(Form("./%25DB1.DBX0:LINT", S7DataType.LINT).Validate(S7Controller.S7_1500));
            Assert.Null(Form("./%25DB1.DBW0:S5TIME", S7DataType.S5TIME).Validate(S7Controller.S7_400));
            Assert.Null(Form("./%25DB1.DBW0:S5TIME", S7DataType.S5TIME).Validate(S7Controller.S7_1500));
            Assert.NotNull(Form("./%25DB1.DBW0:S5TIME", S7DataType.S5TIME).Validate(S7Controller.S7_1200));
            Assert.NotNull(Form("./%25C0:WORD", S7DataType.WORD).Validate(S7Controller.S7_1500));
            Assert.Null(Form("./%25C0:WORD", S7DataType.WORD).Validate(S7Controller.S7_300));
            Assert.Null(Form("./%25DB1.DBX0:LINT", S7DataType.LINT).Validate(S7Controller.ANY));
        }

        [Fact]
        public void Thing_terms_require_tsaps_for_tsap_families_and_never_default_them()
        {
            ThingDescription td = new() { AdditionalData = new Dictionary<string, object> { ["s7v:controller"] = "LOGO" } };
            Assert.Throws<ArgumentException>(() => S7ConnectionTerms.FromThingDescription(td));

            td.AdditionalData["s7v:localTsap"] = 512;
            td.AdditionalData["s7v:remoteTsap"] = "0x0300";
            S7ConnectionTerms terms = S7ConnectionTerms.FromThingDescription(td);
            Assert.Equal(S7Controller.LOGO, terms.Controller);
            Assert.Equal((ushort)0x0200, terms.LocalTsap);
            Assert.Equal((ushort)0x0300, terms.RemoteTsap);
            Assert.True(terms.UsesTsaps);

            td.AdditionalData["s7v:pduSize"] = 64;
            Assert.Throws<ArgumentException>(() => S7ConnectionTerms.FromThingDescription(td));

            td.AdditionalData["s7v:controller"] = "S7-1500";
            Assert.Throws<ArgumentException>(() => S7ConnectionTerms.FromThingDescription(td));

            Assert.Null(S7ConnectionTerms.FromThingDescription(new ThingDescription()).Controller);
        }

        [Fact]
        public void Strings_use_the_current_length_and_reject_overlong_values()
        {
            byte[] buffer = { 10, 3, (byte)'a', (byte)'b', (byte)'c', (byte)'x', 0, 0, 0, 0, 0, 0 };
            Assert.Equal("abc", SiemensAsset.DecodeS7String(buffer));

            byte[] encoded = new byte[12];
            SiemensAsset.EncodeS7String(encoded, 10, "hello");
            Assert.Equal(10, encoded[0]);
            Assert.Equal(5, encoded[1]);
            Assert.Equal("hello", SiemensAsset.DecodeS7String(encoded));
            Assert.Throws<ArgumentException>(() => SiemensAsset.EncodeS7String(new byte[6], 4, "hello"));
        }

        [Fact]
        public void Values_are_big_endian_and_bools_are_single_bits()
        {
            Assert.Null(S7Address.TryParse("%DB1.DBW20:INT", out S7Address word));
            byte[] bytes = SiemensAsset.Encode((short)0x1234, S7DataType.INT, word);
            Assert.Equal(new byte[] { 0x12, 0x34 }, bytes);
            Assert.Equal((short)0x1234, SiemensAsset.Decode(bytes, S7DataType.INT));

            Assert.Null(S7Address.TryParse("%DB1.DBX4.3:BOOL", out S7Address bit));
            Assert.Equal(35, SiemensAsset.Start(bit));
            Assert.Equal(true, SiemensAsset.Decode(new byte[] { 1 }, S7DataType.BOOL));
        }

        [Fact]
        public void Durations_are_iso_8601()
        {
            Assert.Equal("PT1.5S", SiemensAsset.Decode(new byte[] { 0, 0, 0x05, 0xDC }, S7DataType.TIME));
            Assert.Equal(TimeSpan.FromSeconds(1.5), SiemensAsset.ConvertToTimeSpan("PT1.5S"));
        }

        [Fact]
        public void Legacy_forms_are_mapped_onto_the_binding_terms()
        {
            string legacy = "{\"href\":\"DB1?0\",\"op\":[\"readproperty\"],\"s7:rack\":0,\"s7:slot\":0,\"s7:dbnumber\":1," +
                "\"s7:start\":4,\"s7:size\":2,\"s7:pos\":0,\"s7:maxlen\":0,\"type\":\"xsd:short\",\"s7:target\":\"DB\",\"pollingTime\":500}";
            S7Form form = JsonConvert.DeserializeObject<S7Form>(legacy);
            Assert.Null(form.Validate());
            Assert.Null(form.TryResolveAddress(out S7Address address));
            Assert.Equal("%DB1.DBW4:INT", address.ToString());
            Assert.Equal(500, form.EffectivePollingTime);

            string merker = "{\"href\":\"M120\",\"op\":[\"readproperty\"],\"s7:dbnumber\":0,\"s7:start\":120,\"s7:pos\":3," +
                "\"type\":\"xsd:boolean\",\"s7:target\":\"MB\",\"s7:s7type\":\"BOOL\"}";
            Assert.Null(JsonConvert.DeserializeObject<S7Form>(merker).TryResolveAddress(out S7Address bit));
            Assert.Equal("%M120.3:BOOL", bit.ToString());
        }

        [Fact]
        public void Binding_forms_round_trip_without_legacy_terms()
        {
            JObject json = JObject.FromObject(Form("./%25DB1.DBW20:INT", S7DataType.INT));
            Assert.Equal("s7v:INT", (string)json["s7v:type"]);
            Assert.Null(json["type"]);
            Assert.Null(json["s7:dbnumber"]);
            Assert.Null(json["s7v:area"]);
        }

        [Fact]
        public void Driver_rejects_forms_that_violate_the_binding()
        {
            SiemensProtocolDriver driver = new();
            ThingDescription td = new() { Name = "plc", Base = "s7://127.0.0.1/0,1/" };

            AssetTag tag = driver.CreateTag(td, JsonConvert.SerializeObject(Form("./%25DB1.DBW20:INT", S7DataType.INT)), "plc", 1, "v", null, null);
            Assert.Equal("INT", tag.Type);

            Assert.Throws<ArgumentException>(() =>
                driver.CreateTag(td, JsonConvert.SerializeObject(Form("./%25DB1.DBW20:FOO")), "plc", 1, "v", null, null));
        }

        [Fact]
        public void Every_form_of_the_sample_follows_the_binding()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Siemens_S7_PLC.td.jsonld");
            ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(File.ReadAllText(path));

            Assert.Equal("s7://192.168.178.2/0,1/", td.Base);
            S7ConnectionTerms terms = S7ConnectionTerms.FromThingDescription(td);
            Assert.Equal(S7Controller.S7_1500, terms.Controller);

            foreach (KeyValuePair<string, Property> property in td.Properties)
            {
                foreach (object formObject in property.Value.Forms)
                {
                    S7Form form = JsonConvert.DeserializeObject<S7Form>(formObject.ToString());
                    Assert.True(form.Validate(terms.Controller) == null, property.Key + ": " + form.Validate(terms.Controller));
                    Assert.StartsWith("./%25", form.Href);
                    Assert.NotNull(form.Type);
                }
            }
        }

        [Fact]
        public void Controller_family_is_derived_from_the_order_number()
        {
            Assert.Equal(S7Controller.S7_1500, S7Controllers.FromOrderNumber("6ES7 516-3AN01-0AB0"));
            Assert.Equal(S7Controller.S7_1200, S7Controllers.FromOrderNumber("6ES7 214-1AG40-0XB0"));
            Assert.Equal(S7Controller.S7_300, S7Controllers.FromOrderNumber("6ES7 315-2EH14-0AB0"));
            Assert.Equal(S7Controller.S7_400, S7Controllers.FromOrderNumber("6ES7 416-3XR05-0AB0"));
            Assert.Null(S7Controllers.FromOrderNumber("6AV2 123-2GB03-0AX0"));
        }

        [Fact]
        public void Browse_properties_use_the_recommended_data_schema()
        {
            Assert.Null(S7Address.TryParse("%DB3.DBW0:WORD", out S7Address address));
            Property property = SiemensProtocolDriver.CreateProperty(address, readOnly: false);
            Assert.Equal(TypeEnum.Integer, property.Type);
            Assert.Equal(0, property.Minimum);
            Assert.Equal(65535, property.Maximum);
            S7Form form = (S7Form)property.Forms[0];
            Assert.Equal("./%25DB3.DBW0:WORD", form.Href);
            Assert.Null(form.Validate());
        }
    }
}
