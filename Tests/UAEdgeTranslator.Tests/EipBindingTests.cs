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
    /// The Rockwell driver against the W3C WoT EtherNet/IP binding: endpoint and route
    /// path, tag href grammar, the eipv vocabulary, value conversion and UDT layout.
    /// </summary>
    public sealed class EipBindingTests
    {
        [Theory]
        [InlineData("eip://192.168.0.10/1,0/", "192.168.0.10", 44818, "1,0")]
        [InlineData("eip://192.168.0.10/1,3", "192.168.0.10", 44818, "1,3")]
        [InlineData("eip://192.168.0.10", "192.168.0.10", 44818, "1,0")]
        [InlineData("eip://plc.local:2222/1,4,2,192.168.1.20,1,0/", "plc.local", 2222, "1,4,2,192.168.1.20,1,0")]
        public void An_endpoint_carries_host_port_and_route_path(string uri, string host, int port, string routePath)
        {
            Assert.True(EipEndpoint.TryParse(uri, out EipEndpoint endpoint));

            Assert.Equal(host, endpoint.Host);
            Assert.Equal(port, endpoint.Port);
            Assert.Equal(routePath, endpoint.RoutePath);
            Assert.EndsWith("/", endpoint.ToBaseUri(), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("modbus+tcp://192.168.0.10:502")]
        [InlineData("not a uri")]
        [InlineData("")]
        public void A_non_eip_endpoint_is_rejected(string uri)
        {
            Assert.False(EipEndpoint.TryParse(uri, out _));
        }

        [Theory]
        [InlineData("MotorSpeed", "MotorSpeed", 0)]
        [InlineData("./Program:Packaging.Counter", "Program:Packaging.Counter", 0)]
        [InlineData("Program%3APackaging.Counter", "Program:Packaging.Counter", 0)]
        [InlineData("Recipe%5B3%5D.SetPoint", "Recipe[3].SetPoint", 0)]
        [InlineData("./Cxn:Standard:fc7fc958?offset=4", "Cxn:Standard:fc7fc958", 4)]
        [InlineData("eip://192.168.0.10/1,0/Recipe%5B3%5D.SetPoint", "Recipe[3].SetPoint", 0)]
        [InlineData("LegacyTag?2", "LegacyTag", 2)]
        public void An_href_resolves_to_the_tag_path_and_offset(string href, string tagPath, int offset)
        {
            EipTagReference reference = EipTagReference.Parse(href);

            Assert.Equal(tagPath, reference.TagPath);
            Assert.Equal(offset, reference.Offset);
        }

        [Theory]
        [InlineData("MotorSpeed", "MotorSpeed")]
        [InlineData("Program:Packaging.Counter", "./Program:Packaging.Counter")]
        [InlineData("Local:2:O", "./Local:2:O")]
        [InlineData("Recipe[3].SetPoint", "Recipe%5B3%5D.SetPoint")]
        public void A_tag_path_is_written_as_a_resolvable_relative_href(string tagPath, string href)
        {
            Assert.Equal(href, EipTagReference.ToHref(tagPath));

            // Resolving the href against an eip base keeps the route path and the tag.
            Uri resolved = new(new Uri("eip://192.168.0.10/1,0/"), href);
            Assert.Equal(tagPath, EipTagReference.Parse(resolved.OriginalString.Length > 0 ? resolved.AbsoluteUri : href).TagPath);
        }

        [Fact]
        public void A_form_is_written_in_the_eipv_vocabulary()
        {
            EIPForm form = new()
            {
                Href = "MotorSpeed",
                Op = [Op.Readproperty],
                Type = EIPTypeString.REAL,
                PollingTime = 1000,
                Timeout = 2000
            };

            JObject json = JObject.FromObject(form);

            Assert.Equal("eipv:REAL", (string)json["eipv:type"]);
            Assert.Equal(1000, (long)json["eipv:pollingTime"]);
            Assert.Equal(2000, (long)json["eipv:timeout"]);
            Assert.Null(json["type"]);
            Assert.Null(json["pollingTime"]);
            Assert.Null(json["EffectivePollingTime"]);
            Assert.Null(json["eipv:structureDefinition"]);
        }

        [Fact]
        public void A_legacy_form_is_read_as_the_binding_terms()
        {
            const string legacy = """
                {
                  "href": "Production_Data",
                  "op": ["readproperty"],
                  "type": "xsd:BOOL",
                  "pollingTime": 500,
                  "eip:structureDefinition": {
                    "typeName": "UDT_Shift_Data",
                    "fields": [ { "name": "Count", "type": "xsd:DINT", "offset": 4 } ]
                  }
                }
                """;

            EIPForm form = JsonConvert.DeserializeObject<EIPForm>(legacy);

            // The placeholder type of a legacy structured form gives way to its definition.
            Assert.Null(form.Type);
            Assert.Equal(500, form.EffectivePollingTime);
            Assert.Equal("UDT_Shift_Data", form.StructureDefinition.TypeName);
            EIPFieldDefinition field = Assert.Single(form.StructureDefinition.Fields);
            Assert.Equal("Count", field.Name);
            Assert.Equal(EIPTypeString.DINT, field.Type);
            Assert.Equal(4, field.ByteOffset);
        }

        [Theory]
        [InlineData("eipv:REAL", EIPTypeString.REAL)]
        [InlineData("xsd:DINT", EIPTypeString.DINT)]
        [InlineData("eipv:DWORD", EIPTypeString.DWORD)]
        public void Types_are_read_with_either_prefix(string text, EIPTypeString expected)
        {
            Assert.True(EIPTypes.TryParse(text, out EIPTypeString type));
            Assert.Equal(expected, type);
            Assert.Equal("eipv:" + expected, EIPTypes.ToTerm(type));
        }

        [Fact]
        public void An_unsupported_type_is_rejected()
        {
            Assert.False(EIPTypes.TryParse("eipv:TIMER", out _));
        }

        [Fact]
        public void Scale_and_offset_convert_both_ways()
        {
            // value = raw * scale + offset, as in the binding's TankTemp example.
            EIPForm form = new() { Type = EIPTypeString.INT, Scale = 0.1, Offset = -40 };

            Assert.Equal(-15.0, (double)form.ToApplicationValue((short)250), 6);
            Assert.Equal(250.0, (double)form.ToControllerValue(-15.0), 6);

            // Without conversion terms the value passes through untouched.
            Assert.Equal((short)7, new EIPForm { Type = EIPTypeString.INT }.ToApplicationValue((short)7));
        }

        [Fact]
        public void Nested_structure_offsets_are_relative_and_packed_bools_use_their_bit()
        {
            // MotorStatus { BOOL Running @0.0, BOOL Fault @0.1, REAL Speed @4 } nested at 8.
            EIPStructureDefinition motor = new()
            {
                TypeName = "MotorStatus",
                Fields =
                [
                    new EIPFieldDefinition { Name = "Running", Type = EIPTypeString.BOOL, ByteOffset = 0, BitOffset = 0 },
                    new EIPFieldDefinition { Name = "Fault", Type = EIPTypeString.BOOL, ByteOffset = 0, BitOffset = 1 },
                    new EIPFieldDefinition { Name = "Speed", Type = EIPTypeString.REAL, ByteOffset = 4 }
                ]
            };
            EIPStructureDefinition line = new()
            {
                TypeName = "Line",
                Fields =
                [
                    new EIPFieldDefinition { Name = "Count", Type = EIPTypeString.DINT, ByteOffset = 0 },
                    new EIPFieldDefinition { Name = "Motor", ByteOffset = 8, StructureDefinition = motor }
                ]
            };

            byte[] buffer = new byte[16];
            BitConverter.GetBytes(42).CopyTo(buffer, 0);
            buffer[8] = 0b10;
            BitConverter.GetBytes(12.5f).CopyTo(buffer, 12);

            var decoded = RockwellAsset.DecodeUdtFields(buffer, 0, line);

            Assert.Equal(42, decoded["Count"]);
            var nested = Assert.IsType<System.Collections.Generic.Dictionary<string, object>>(decoded["Motor"]);
            Assert.Equal(false, nested["Running"]);
            Assert.Equal(true, nested["Fault"]);
            Assert.Equal(12.5f, nested["Speed"]);
        }

        [Fact]
        public void The_sample_thing_description_follows_the_binding()
        {
            string path = FindRepositoryFile(Path.Combine("Samples", "Rockwell.td.jsonld"));
            JObject td = JObject.Parse(File.ReadAllText(path));

            Assert.Contains(td["@context"], c => c is JObject o && (string)o[EIPForm.Prefix] == EIPForm.BindingNamespace);
            Assert.True(EipEndpoint.TryParse((string)td["base"], out EipEndpoint endpoint));
            Assert.EndsWith("/", (string)td["base"], StringComparison.Ordinal);
            Assert.Equal("1,0", endpoint.RoutePath);

            foreach (JProperty property in ((JObject)td["properties"]).Properties())
            {
                foreach (JObject formJson in (JArray)property.Value["forms"])
                {
                    Assert.Null(formJson["type"]);
                    Assert.Null(formJson["pollingTime"]);
                    Assert.Null(formJson["eip:structureDefinition"]);

                    // A relative href with a colon must start with ./ so it is not a URI scheme.
                    string href = (string)formJson["href"];
                    Assert.True(!href.Contains(":", StringComparison.Ordinal) || href.StartsWith("./", StringComparison.Ordinal), href);

                    EIPForm form = formJson.ToObject<EIPForm>();
                    Assert.True(form.Type == null ^ form.StructureDefinition == null, property.Name);
                }
            }
        }

        private static string FindRepositoryFile(string relativePath)
        {
            for (DirectoryInfo dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException(relativePath);
        }
    }
}
