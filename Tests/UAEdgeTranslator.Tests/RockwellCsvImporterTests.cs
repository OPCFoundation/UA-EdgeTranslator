namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Opc.Ua.Edge.Translator.Tools;
    using System;
    using System.IO;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// The UA-WoTGenerator Rockwell CSV import produces Thing Models that follow the W3C
    /// WoT EtherNet/IP binding, checked on the shipped Studio 5000 sample export.
    /// </summary>
    public sealed class RockwellCsvImporterTests
    {
        private static readonly string s_sample = Path.Combine(AppContext.BaseDirectory, "TestData", "Rockwell_Sample_Tags.csv");

        private static JObject GenerateSample()
        {
            RockwellCsvImporter.Result result = RockwellCsvImporter.Import(s_sample);
            return JObject.Parse(RockwellCsvImporter.Serialize(result.ThingModel));
        }

        [Fact]
        public void The_thing_model_names_the_binding_and_the_controller_route()
        {
            JObject tm = GenerateSample();

            Assert.Contains(tm["@context"], c => c is JObject o && (string)o[EIPForm.Prefix] == EIPForm.BindingNamespace);
            Assert.Equal("ControlLogix", (string)tm["eipv:controller"]);

            // The template base parses once its placeholders are filled in on onboarding.
            string filled = ((string)tm["base"]).Replace("{{address}}", "192.168.0.10").Replace("{{port}}", "44818");
            Assert.True(EipEndpoint.TryParse(filled, out EipEndpoint endpoint));
            Assert.Equal("1,0", endpoint.RoutePath);
            Assert.EndsWith("/", filled, StringComparison.Ordinal);
        }

        [Fact]
        public void Every_form_follows_the_binding()
        {
            JObject tm = GenerateSample();
            JObject properties = (JObject)tm["properties"];

            Assert.True(properties.Count > 1000, "properties: " + properties.Count);

            foreach (JProperty property in properties.Properties())
            {
                JObject formJson = (JObject)Assert.Single((JArray)property.Value["forms"]);

                // No legacy or null terms.
                Assert.Null(formJson["type"]);
                Assert.Null(formJson["pollingTime"]);
                Assert.Null(formJson["eip:structureDefinition"]);
                Assert.DoesNotContain(property.Value.Children<JProperty>(), p => p.Value.Type == JTokenType.Null);

                // A colon in a relative href must not read as a URI scheme.
                string href = (string)formJson["href"];
                Assert.True(!href.Contains(":") || href.StartsWith("./", StringComparison.Ordinal), href);

                EIPForm form = formJson.ToObject<EIPForm>();
                if (form.Type is EIPTypeString type && form.IsArray)
                {
                    // Proposed array extension: an array schema of the element type.
                    Assert.Null(form.ValidateArray());
                    Assert.Equal("array", (string)property.Value["type"]);
                    Assert.Equal(RockwellCsvImporter.WotTypeOf(type).ToString(), (string)property.Value["items"]["type"], ignoreCase: true);
                    Assert.Equal(form.EffectiveElementCount, (int)property.Value["maxItems"]);
                }
                else if (form.Type is EIPTypeString scalarType)
                {
                    Assert.Equal(RockwellCsvImporter.WotTypeOf(scalarType).ToString(), (string)property.Value["type"], ignoreCase: true);
                }
                else
                {
                    // Structured tags are named only; the driver reads their live layout.
                    Assert.Null(form.StructureDefinition);
                    Assert.Equal("object", (string)property.Value["type"]);
                }
            }
        }

        [Fact]
        public void Program_scoped_tags_are_addressed_through_their_program()
        {
            JObject tm = GenerateSample();

            JObject mixer = (JObject)tm["properties"]["Mixer.AR"];
            Assert.Equal("./Program:Mixer.AR", (string)mixer["forms"][0]["href"]);
            Assert.Equal("Program:Mixer.AR", EipTagReference.Parse((string)mixer["forms"][0]["href"]).TagPath);

            JObject controllerScoped = (JObject)tm["properties"]["a"];
            Assert.Equal("a", (string)controllerScoped["forms"][0]["href"]);
            Assert.Equal("eipv:REAL", (string)controllerScoped["forms"][0]["eipv:type"]);
        }

        [Fact]
        public void Add_on_instruction_parameters_are_skipped()
        {
            RockwellCsvImporter.Result result = RockwellCsvImporter.Import(s_sample);

            Assert.DoesNotContain(result.ThingModel.Properties.Keys, k => k.Contains(":AOI"));
            Assert.Contains(result.Skipped, s => s.Contains("Add-On Instruction parameter"));
        }

        [Fact]
        public void Numeric_array_tags_become_array_properties()
        {
            RockwellCsvImporter.Result result = RockwellCsvImporter.Import(s_sample);

            Property alarms = result.ThingModel.Properties["Mixer.AlarmRC"];
            Assert.Equal(TypeEnum.Array, alarms.Type);
            Assert.Equal(TypeEnum.Integer, alarms.Items.Type);
            Assert.Equal(10, alarms.MaxItems);
            EIPForm form = (EIPForm)alarms.Forms[0];
            Assert.Equal(EIPTypeString.DINT, form.Type);
            Assert.Equal(new[] { 10 }, form.Dimensions);
            Assert.Equal("./Program:Mixer.AlarmRC", form.Href);

            // Arrays the extension does not describe are still skipped, with the reason.
            Assert.Contains(result.Skipped, s => s.StartsWith("LineData: array of MachineUDT", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("[10]", new[] { 10 })]
        [InlineData("[4,5]", new[] { 4, 5 })]
        [InlineData("[2, 3, 4]", new[] { 2, 3, 4 })]
        public void Dimensions_are_parsed_outermost_first(string text, int[] expected)
        {
            Assert.Equal(expected, RockwellCsvImporter.ParseDimensions(text));
        }

        [Theory]
        [InlineData("[0]")]
        [InlineData("[1,2,3,4]")]
        [InlineData("[x]")]
        public void Unreadable_dimensions_are_rejected(string text)
        {
            Assert.Null(RockwellCsvImporter.ParseDimensions(text));
        }


        [Fact]
        public void External_access_sets_read_only_and_operations()
        {
            // The sample has no read-only controller tags, so a small export covers them.
            string csv = Path.Combine(Path.GetTempPath(), "rockwell-access-" + Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllLines(csv, new[]
            {
                "TYPE,SCOPE,NAME,DESCRIPTION,DATATYPE,SPECIFIER,ATTRIBUTES",
                "TAG,,Speed,\"\",\"REAL\",\"\",\"(RADIX := Float, Constant := false, ExternalAccess := Read/Write)\"",
                "TAG,,Limit,\"\",\"DINT\",\"\",\"(RADIX := Decimal, Constant := false, ExternalAccess := Read Only)\"",
                "TAG,,Factor,\"\",\"REAL\",\"\",\"(RADIX := Float, Constant := true, ExternalAccess := Read/Write)\"",
                "TAG,,Hidden,\"\",\"DINT\",\"\",\"(RADIX := Decimal, Constant := false, ExternalAccess := None)\"",
                "TAG,Line,Timer1,\"Cycle, with comma\",\"TIMER\",\"\",\"(Constant := false, ExternalAccess := Read/Write)\""
            });

            try
            {
                RockwellCsvImporter.Result result = RockwellCsvImporter.Import(csv);
                var properties = result.ThingModel.Properties;

                Assert.False(properties["Speed"].ReadOnly);
                Assert.Contains(Op.Writeproperty, ((EIPForm)properties["Speed"].Forms[0]).Op);

                Assert.True(properties["Limit"].ReadOnly);
                Assert.DoesNotContain(Op.Writeproperty, ((EIPForm)properties["Limit"].Forms[0]).Op);
                Assert.True(properties["Factor"].ReadOnly);

                Assert.False(properties.ContainsKey("Hidden"));
                Assert.Contains(result.Skipped, s => s.StartsWith("Hidden: ExternalAccess := None", StringComparison.Ordinal));

                // A predefined structure is named only, and a quoted comma does not split the row.
                EIPForm timer = (EIPForm)properties["Line.Timer1"].Forms[0];
                Assert.Null(timer.Type);
                Assert.Equal("./Program:Line.Timer1", timer.Href);
                Assert.Equal(TypeEnum.Object, properties["Line.Timer1"].Type);
            }
            finally
            {
                File.Delete(csv);
            }
        }
    }
}
