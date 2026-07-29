namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Models;
    using System.Collections.Generic;
    using System.Linq;
    using Xunit;

    public class ThingDescriptionTests
    {
        [Fact]
        public void Round_trip_serialization_preserves_top_level_fields()
        {
            ThingDescription td = new()
            {
                Context = new object[] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:dev",
                Security = new[] { "nosec_sc" },
                Type = new[] { "Thing" },
                Name = "dev",
                Base = "mock://dev:1/1",
                Title = "dev",
                Description = "round-trip",
                SecurityDefinitions = new SecurityDefinitions
                {
                    NosecSc = new NosecSc { Scheme = "nosec" }
                },
                Properties = new Dictionary<string, Property>
                {
                    ["temperature"] = new Property
                    {
                        Type = TypeEnum.Number,
                        ReadOnly = true,
                        Observable = true,
                        OpcUaNodeId = "nsu=urn:x;i=1",
                        OpcUaType = "nsu=urn:x;i=10",
                        OpcUaFieldPath = "value",
                        Forms = new object[]
                        {
                            new GenericForm
                            {
                                Href = "/t",
                                Op = new[] { Op.Readproperty, Op.Observeproperty },
                                Type = TypeString.Float,
                                PollingTime = 1000
                            }
                        }
                    }
                },
                Actions = new Dictionary<string, TDAction>
                {
                    ["reset"] = new TDAction
                    {
                        Input = new TDArguments
                        {
                            Type = TypeEnum.Object,
                            Required = new[] { "force" },
                            Properties = new Dictionary<string, Property>
                            {
                                ["force"] = new Property { Type = TypeEnum.Boolean }
                            }
                        },
                        Output = new TDArguments
                        {
                            Type = TypeEnum.String,
                            Properties = new Dictionary<string, Property>()
                        },
                        Forms = new object[] { new { href = "/r" } }
                    }
                }
            };

            string json = JsonConvert.SerializeObject(td);
            ThingDescription roundTripped = JsonConvert.DeserializeObject<ThingDescription>(json);

            Assert.NotNull(roundTripped);
            Assert.Equal("urn:dev", roundTripped.Id);
            Assert.Equal("dev", roundTripped.Name);
            Assert.Equal("mock://dev:1/1", roundTripped.Base);
            Assert.Equal("round-trip", roundTripped.Description);
            Assert.Equal("nosec", roundTripped.SecurityDefinitions.NosecSc.Scheme);
            Assert.Equal(TypeEnum.Number, roundTripped.Properties["temperature"].Type);
            Assert.True(roundTripped.Properties["temperature"].ReadOnly);
            Assert.True(roundTripped.Properties["temperature"].Observable);
            Assert.Single(roundTripped.Properties["temperature"].Forms);
            Assert.Equal(TypeEnum.Object, roundTripped.Actions["reset"].Input.Type);
            Assert.Contains("force", roundTripped.Actions["reset"].Input.Required);
        }

        [Theory]
        [InlineData(TypeEnum.Number, "\"number\"")]
        [InlineData(TypeEnum.Boolean, "\"boolean\"")]
        [InlineData(TypeEnum.Integer, "\"integer\"")]
        [InlineData(TypeEnum.String, "\"string\"")]
        [InlineData(TypeEnum.Object, "\"object\"")]
        public void TypeEnum_serializes_to_lowercase_string(TypeEnum value, string expectedJson)
        {
            string json = JsonConvert.SerializeObject(value);
            Assert.Equal(expectedJson, json);
        }

        [Theory]
        [InlineData(TypeString.Float, "\"xsd:float\"")]
        [InlineData(TypeString.Double, "\"xsd:double\"")]
        [InlineData(TypeString.Boolean, "\"xsd:boolean\"")]
        [InlineData(TypeString.Short, "\"xsd:short\"")]
        [InlineData(TypeString.Integer, "\"xsd:integer\"")]
        [InlineData(TypeString.String, "\"xsd:string\"")]
        [InlineData(TypeString.Byte, "\"xsd:byte\"")]
        [InlineData(TypeString.TimedCommand, "\"xsd:timedCommand\"")]
        [InlineData(TypeString.Long, "\"xsd:long\"")]
        [InlineData(TypeString.UnsignedLong, "\"xsd:unsignedLong\"")]
        [InlineData(TypeString.DateTime, "\"xsd:dateTime\"")]
        [InlineData(TypeString.Duration, "\"xsd:duration\"")]
        public void TypeString_serializes_to_xsd_label(TypeString value, string expectedJson)
        {
            string json = JsonConvert.SerializeObject(value);
            Assert.Equal(expectedJson, json);
        }

        [Theory]
        [InlineData(Op.Observeproperty, "\"observeproperty\"")]
        [InlineData(Op.Readproperty, "\"readproperty\"")]
        [InlineData(Op.Writeproperty, "\"writeproperty\"")]
        public void Op_serializes_to_lowercase_string(Op value, string expectedJson)
        {
            string json = JsonConvert.SerializeObject(value);
            Assert.Equal(expectedJson, json);
        }

        [Fact]
        public void Property_const_round_trips()
        {
            Property property = new()
            {
                Type = TypeEnum.String,
                Const = "factory-default"
            };

            string json = JsonConvert.SerializeObject(property);
            Property hydrated = JsonConvert.DeserializeObject<Property>(json);

            Assert.Equal(TypeEnum.String, hydrated.Type);
            Assert.Equal("factory-default", hydrated.Const?.ToString());
        }

        [Fact]
        public void ThingDescription_round_trips_opc_ae_event_filters()
        {
            ThingDescription td = new()
            {
                Name = "MatrikonAe",
                Base = "opc.ae://localhost/Matrikon.OPC.Simulation.1",
                Events = new Dictionary<string, TDEvent>
                {
                    ["alarms"] = new()
                    {
                        Forms = new object[]
                        {
                            new
                            {
                                Href = "opc.ae://localhost/Matrikon.OPC.Simulation.1",
                                Categories = new[] { 1, 2 },
                                Areas = new[] { "Simulation" },
                                Sources = new[] { "Simulation.Source" },
                                EventTypes = 7,
                                LowSeverity = 100,
                                HighSeverity = 900,
                                BufferTime = 500,
                                MaxEvents = 100,
                                RefreshOnConnect = true
                            }
                        }
                    }
                }
            };

            ThingDescription roundTripped = JsonConvert.DeserializeObject<ThingDescription>(JsonConvert.SerializeObject(td));
            JObject form = JObject.Parse(roundTripped.Events["alarms"].Forms[0].ToString());
            JToken categories = form["Categories"] ?? form["categories"];

            Assert.Equal(td.Base, form["Href"]?.ToString() ?? form["href"]?.ToString());
            Assert.Equal(2, categories?.Values().Count());
            Assert.Equal("Simulation", form["Areas"]?[0]?.ToString() ?? form["areas"]?[0]?.ToString());
            Assert.Equal("100", form["LowSeverity"]?.ToString() ?? form["lowSeverity"]?.ToString());
            Assert.True((form["RefreshOnConnect"] ?? form["refreshOnConnect"])?.Value<bool>() ?? false);
        }

        [Fact]
        public void TDEvent_round_trips_extended_td11_fields()
        {
            ThingDescription td = new()
            {
                Name = "EventThing",
                Base = "mock://event-thing",
                Events = new Dictionary<string, TDEvent>
                {
                    ["statusChanged"] = new()
                    {
                        Type = new[] { "StatusEvent" },
                        Title = "Status changed",
                        Titles = new Dictionary<string, string>
                        {
                            ["en"] = "Status changed"
                        },
                        Description = "Notifies when status changes.",
                        Descriptions = new Dictionary<string, string>
                        {
                            ["en"] = "Notifies when status changes."
                        },
                        UriVariables = new Dictionary<string, object>
                        {
                            ["subscriptionId"] = new { type = "string" }
                        },
                        Data = new { type = "object", properties = new { state = new { type = "string" } } },
                        Subscription = new { type = "object", properties = new { threshold = new { type = "number" } } },
                        Cancellation = new { type = "object", properties = new { subscriptionId = new { type = "string" } } },
                        DataResponse = new { type = "object", properties = new { accepted = new { type = "boolean" } } },
                        Forms = new object[]
                        {
                            new { href = "mock://event-thing/events/status", op = new[] { "subscribeevent", "unsubscribeevent" } }
                        }
                    }
                }
            };

            ThingDescription roundTripped = JsonConvert.DeserializeObject<ThingDescription>(JsonConvert.SerializeObject(td));
            TDEvent ev = roundTripped.Events["statusChanged"];

            Assert.Equal("StatusEvent", ev.Type[0]);
            Assert.Equal("Status changed", ev.Title);
            Assert.Equal("Status changed", ev.Titles["en"]);
            Assert.Equal("Notifies when status changes.", ev.Description);
            Assert.NotNull(ev.Data);
            Assert.NotNull(ev.Subscription);
            Assert.NotNull(ev.Cancellation);
            Assert.NotNull(ev.DataResponse);

            JObject data = (JObject)ev.Data;
            JObject subscription = (JObject)ev.Subscription;
            JObject cancellation = (JObject)ev.Cancellation;
            JObject dataResponse = (JObject)ev.DataResponse;

            Assert.Equal("object", data["type"]?.ToString());
            Assert.Equal("object", subscription["type"]?.ToString());
            Assert.Equal("object", cancellation["type"]?.ToString());
            Assert.Equal("object", dataResponse["type"]?.ToString());
        }
    }
}
