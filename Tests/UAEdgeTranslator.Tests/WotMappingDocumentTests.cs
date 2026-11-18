namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Xunit;

    /// <summary>
    /// Covers the document model behind the Projections page: reading WoT properties,
    /// writing the uav:mapToNodeId / uav:mapToType / uav:mapByFieldPath projections,
    /// declaring companion namespaces and turning a Thing Model into a Thing Description.
    /// </summary>
    public sealed class WotMappingDocumentTests
    {
        private const string ThingDescription = """
            {
              "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
              "id": "urn:meter",
              "name": "meter",
              "title": "Meter",
              "base": "mock://127.0.0.1:1234/1",
              "x-vendor": { "keep": true },
              "properties": {
                "voltage": {
                  "type": "number",
                  "description": "Line voltage",
                  "forms": [ { "href": "/v", "op": [ "readproperty" ] } ]
                },
                "serial": {
                  "type": "string",
                  "uav:mapToNodeId": "s=SerialNumber",
                  "forms": [ { "href": "/s", "op": [ "readproperty" ] } ]
                }
              }
            }
            """;

        private const string ThingModel = """
            {
              "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
              "@type": [ "tm:ThingModel" ],
              "title": "Meter {{MODEL}}",
              "base": "mock://{{HOST}}:{{PORT}}/1",
              "properties": {
                "voltage": { "type": "number", "forms": [ { "href": "/v" } ] }
              }
            }
            """;

        [Fact]
        public void Properties_default_their_node_id_to_their_name_and_keep_an_authored_one()
        {
            WotMappingDocument document = WotMappingDocument.Parse(ThingDescription);

            Assert.Equal(["voltage", "serial"], document.Properties.Select(p => p.Name));
            WotMappingProperty voltage = document.Properties[0];
            Assert.Equal("number", voltage.Type);
            Assert.Equal("Line voltage", voltage.Description);
            Assert.Equal("s=voltage", voltage.MapToNodeId);
            Assert.False(voltage.IsMapped);
            Assert.Equal("s=SerialNumber", document.Properties[1].MapToNodeId);
        }

        [Fact]
        public void Document_metadata_is_read_and_unknown_content_survives_serialization()
        {
            WotMappingDocument document = WotMappingDocument.Parse("\uFEFF" + ThingDescription);

            Assert.Equal("meter", document.Name);
            Assert.True(document.HasName);
            Assert.Equal("Meter", document.Title);
            Assert.Equal("urn:meter", document.Id);
            Assert.False(document.IsThingModel);

            JsonObject root = JsonNode.Parse(document.Serialize()).AsObject();
            Assert.True(root["x-vendor"]["keep"].GetValue<bool>());
            Assert.Equal("/v", root["properties"]["voltage"]["forms"][0]["href"].GetValue<string>());
        }

        [Fact]
        public void A_document_without_a_name_falls_back_to_its_title()
        {
            WotMappingDocument document = WotMappingDocument.Parse(ThingModel);

            Assert.False(document.HasName);
            Assert.Equal("Meter {{MODEL}}", document.Name);
        }

        [Fact]
        public void Applying_and_clearing_a_mapping_writes_the_projection_terms()
        {
            WotMappingDocument document = WotMappingDocument.Parse(ThingDescription);
            WotMappingProperty voltage = document.Properties[0];

            voltage.ApplyMapping("nsu=http://opcfoundation.org/UA/PNEM/;i=3005", "A");

            Assert.True(voltage.IsMapped);
            JsonObject property = Property(document, "voltage");
            Assert.Equal("nsu=http://opcfoundation.org/UA/PNEM/;i=3005", property["uav:mapToType"].GetValue<string>());
            Assert.Equal("A", property["uav:mapByFieldPath"].GetValue<string>());
            Assert.Equal("s=voltage", property["uav:mapToNodeId"].GetValue<string>());

            // A simple type has no field path; remapping drops the previous one.
            voltage.ApplyMapping("i=11", null);
            property = Property(document, "voltage");
            Assert.Equal("i=11", property["uav:mapToType"].GetValue<string>());
            Assert.False(property.ContainsKey("uav:mapByFieldPath"));

            voltage.SetNodeId("  s=VoltageL-N ");
            voltage.ClearMapping();
            property = Property(document, "voltage");
            Assert.False(voltage.IsMapped);
            Assert.False(property.ContainsKey("uav:mapToType"));
            Assert.Equal("s=voltage", property["uav:mapToNodeId"].GetValue<string>());
        }

        [Fact]
        public void Setting_an_empty_node_id_restores_the_default()
        {
            WotMappingProperty voltage = WotMappingDocument.Parse(ThingDescription).Properties[0];

            voltage.SetNodeId("s=Custom");
            Assert.Equal("s=Custom", voltage.MapToNodeId);

            voltage.SetNodeId(" ");
            Assert.Equal("s=voltage", voltage.MapToNodeId);
        }

        [Fact]
        public void The_first_companion_namespace_binds_uav_and_further_ones_get_their_own_entry()
        {
            WotMappingDocument document = WotMappingDocument.Parse(ThingDescription);

            document.DeclareNamespace("http://opcfoundation.org/UA/PNEM/");
            document.DeclareNamespace("http://opcfoundation.org/UA/PNEM/");
            document.DeclareNamespace("http://opcfoundation.org/UA/DI/");
            document.DeclareNamespace(string.Empty);

            JsonArray context = JsonNode.Parse(document.Serialize())["@context"].AsArray();
            Assert.Equal(3, context.Count);
            Assert.Equal("https://www.w3.org/2022/wot/td/v1.1", context[0].GetValue<string>());
            Assert.Equal("http://opcfoundation.org/UA/PNEM/", context[1]["uav"].GetValue<string>());
            Assert.Equal("http://opcfoundation.org/UA/DI/", context[2]["ns1"].GetValue<string>());
        }

        [Fact]
        public void A_namespace_is_added_next_to_an_existing_uav_binding()
        {
            WotMappingDocument document = WotMappingDocument.Parse("""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "name": "x",
                  "properties": {}
                }
                """);

            document.DeclareNamespace("http://opcfoundation.org/UA/PNEM/");
            JsonArray context = JsonNode.Parse(document.Serialize())["@context"].AsArray();
            Assert.Equal(2, context.Count);
            Assert.Equal("http://opcfoundation.org/UA/PNEM/", context[1]["uav"].GetValue<string>());

            WotMappingDocument bound = WotMappingDocument.Parse("""
                {
                  "@context": [ "https://www.w3.org/2022/wot/td/v1.1", { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "ns1": "urn:a" } ],
                  "name": "x",
                  "properties": {}
                }
                """);

            bound.DeclareNamespace("http://opcfoundation.org/UA/PNEM/");
            context = JsonNode.Parse(bound.Serialize())["@context"].AsArray();
            Assert.Equal("http://opcfoundation.org/UA/WoT-Binding/", context[1]["uav"].GetValue<string>());
            Assert.Equal("http://opcfoundation.org/UA/PNEM/", context[2]["ns2"].GetValue<string>());
        }

        [Fact]
        public void A_thing_model_becomes_a_thing_description_with_its_placeholders_filled_in()
        {
            WotMappingDocument model = WotMappingDocument.Parse(ThingModel);
            Assert.True(model.IsThingModel);
            Assert.Equal(["MODEL", "HOST", "PORT"], model.FindPlaceholders());

            string json = model.CreateThingDescriptionJson(
                new Dictionary<string, string> { ["MODEL"] = "PAC \"4200\"", ["HOST"] = "10.0.0.5", ["PORT"] = "502" },
                " meter-1 ");

            WotMappingDocument description = WotMappingDocument.Parse(json);
            Assert.False(description.IsThingModel);
            Assert.Equal("meter-1", description.Name);
            Assert.Equal("Meter PAC \"4200\"", description.Title);
            Assert.Null(JsonNode.Parse(json)["@type"]);
            Assert.Equal("mock://10.0.0.5:502/1", JsonNode.Parse(json)["base"].GetValue<string>());
        }

        [Fact]
        public void Unfilled_placeholders_are_left_in_place()
        {
            WotMappingDocument model = WotMappingDocument.Parse(ThingModel);

            string json = model.CreateThingDescriptionJson(new Dictionary<string, string> { ["HOST"] = "h" });

            Assert.Contains("{{MODEL}}", json, StringComparison.Ordinal);
            Assert.Contains("mock://h:{{PORT}}/1", json, StringComparison.Ordinal);
        }

        [Fact]
        public void A_document_that_is_not_a_json_object_is_rejected()
        {
            Assert.Throws<InvalidOperationException>(() => WotMappingDocument.Parse("[1, 2]"));
            Assert.ThrowsAny<Exception>(() => WotMappingDocument.Parse("{ not json"));
        }

        private static JsonObject Property(WotMappingDocument document, string name)
            => JsonNode.Parse(document.Serialize())["properties"][name].AsObject();
    }
}
