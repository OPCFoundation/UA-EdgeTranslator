namespace Opc.Ua.Edge.Translator.Tests.WotCon
{
    using Opc.Ua.Edge.Translator.WotCon;
    using System.Linq;
    using System.Text;
    using System.Text.Json.Nodes;
    using Xunit;

    public sealed class WotImportNormalizerTests
    {
        private static JsonObject Normalize(string json) =>
            JsonNode.Parse(WotImportNormalizer.Normalize(Encoding.UTF8.GetBytes(json)))!.AsObject();

        [Fact]
        public void A_plain_thing_is_made_a_registry_thing_description_under_objects()
        {
            JsonObject td = Normalize("""
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1", { "uav": "http://opcfoundation.org/UA/PNEM/" }],
                  "@type": ["Thing"],
                  "id": "urn:pac",
                  "title": "pac",
                  "properties": {
                    "p": {
                      "uav:mapToNodeId": "s=x",
                      "uav:mapToType": "nsu=http://opcfoundation.org/UA/PNEM/;i=3005",
                      "uav:mapByFieldPath": "A",
                      "forms": [ { "href": "1" } ]
                    }
                  }
                }
                """);

            Assert.Equal(["Thing", "uav:object"], td["@type"]!.AsArray().Select(t => t!.GetValue<string>()));

            JsonObject prefixes = td["@context"]!.AsArray().OfType<JsonObject>().Single();
            Assert.Equal(WotImportNormalizer.BindingNamespace, prefixes["uav"]!.GetValue<string>());

            // Authored target mappings stay in force; a namespace-less NodeId is
            // qualified with the Thing's namespace, exactly as 1.02 resolved it.
            JsonObject p = td["properties"]!["p"]!.AsObject();
            Assert.Equal("nsu=urn:pac;s=x", p["uav:mapToNodeId"]!.GetValue<string>());
            Assert.Equal("nsu=http://opcfoundation.org/UA/PNEM/;i=3005", p["uav:mapToType"]!.GetValue<string>());
            Assert.Equal("A", p["uav:mapByFieldPath"]!.GetValue<string>());
            Assert.Null(p["uav:id"]);

            Assert.Equal(WotImportNormalizer.ObjectsFolder, td["uav:browsePathAnchor"]!.GetValue<string>());
            JsonObject link = Assert.Single(td["links"]!.AsArray().OfType<JsonObject>());
            Assert.Equal("uav:componentOf", link["rel"]!.GetValue<string>());
            Assert.Equal(WotImportNormalizer.ObjectsFolder, link["href"]!.GetValue<string>());
        }

        [Fact]
        public void A_scalar_context_and_type_are_widened()
        {
            JsonObject td = Normalize("""{ "@context": "https://www.w3.org/2022/wot/td/v1.1", "@type": "Thing", "title": "x" }""");

            Assert.Equal("https://www.w3.org/2022/wot/td/v1.1", td["@context"]![0]!.GetValue<string>());
            Assert.Equal(WotImportNormalizer.BindingNamespace, td["@context"]![1]!["uav"]!.GetValue<string>());
            Assert.Contains("uav:object", td["@type"]!.AsArray().Select(t => t!.GetValue<string>()));
        }

        [Fact]
        public void A_stated_anchor_is_kept_and_no_link_is_added()
        {
            JsonObject td = Normalize("""
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1", { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }],
                  "@type": ["Thing", "uav:object"],
                  "uav:browsePathAnchor": "nsu=urn:plant;s=Line1",
                  "title": "x"
                }
                """);

            Assert.Equal("nsu=urn:plant;s=Line1", td["uav:browsePathAnchor"]!.GetValue<string>());
            Assert.Null(td["links"]);
        }

        [Fact]
        public void A_conforming_document_is_returned_byte_for_byte()
        {
            byte[] content = Encoding.UTF8.GetBytes("""
                {"@context":["https://www.w3.org/2022/wot/td/v1.1",{"uav":"http://opcfoundation.org/UA/WoT-Binding/"}],"@type":["Thing","uav:object"],"uav:browsePathAnchor":"nsu=http://opcfoundation.org/UA/;i=85","title":"x"}
                """);

            Assert.Same(content, WotImportNormalizer.Normalize(content));
        }

        [Fact]
        public void Thing_models_are_not_turned_into_objects()
        {
            JsonObject tm = Normalize("""{ "@context": "https://www.w3.org/2022/wot/td/v1.1", "@type": "tm:ThingModel", "title": "x" }""");

            Assert.DoesNotContain("uav:object", tm["@type"]!.ToJsonString());
        }

        [Fact]
        public void An_unmapped_property_is_mapped_to_its_own_variable()
        {
            JsonObject td = Normalize("""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "@type": "Thing",
                  "id": "urn:pump",
                  "name": "pump",
                  "properties": {
                    "speed": { "type": "number", "forms": [ { "href": "1" } ] },
                    "mapped": { "type": "number", "uav:mapToNodeId": "nsu=urn:x;s=y", "forms": [ { "href": "2" } ] }
                  }
                }
                """);

            JsonObject speed = td["properties"]!["speed"]!.AsObject();
            Assert.Equal("nsu=urn:pump;s=pump.speed", speed["uav:id"]!.GetValue<string>());
            Assert.Equal("nsu=urn:pump;s=pump.speed", speed["uav:mapToNodeId"]!.GetValue<string>());

            // An authored, already-portable mapping is left exactly as written.
            JsonObject mapped = td["properties"]!["mapped"]!.AsObject();
            Assert.Equal("nsu=urn:x;s=y", mapped["uav:mapToNodeId"]!.GetValue<string>());
            Assert.Null(mapped["uav:id"]);
        }

        [Fact]
        public void An_authored_relative_id_is_qualified_before_it_is_mapped()
        {
            JsonObject td = Normalize("""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "@type": "Thing",
                  "id": "urn:pump",
                  "name": "pump",
                  "properties": {
                    "speed": { "type": "number", "uav:id": "s=Speed", "forms": [ { "href": "1" } ] },
                    "flow": { "type": "number", "uav:id": "nsu=urn:other;s=Flow", "forms": [ { "href": "2" } ] }
                  }
                }
                """);

            // "s=Speed" would otherwise resolve in namespace 0.
            JsonObject speed = td["properties"]!["speed"]!.AsObject();
            Assert.Equal("nsu=urn:pump;s=Speed", speed["uav:id"]!.GetValue<string>());
            Assert.Equal("nsu=urn:pump;s=Speed", speed["uav:mapToNodeId"]!.GetValue<string>());

            // An already qualified id is kept as written.
            JsonObject flow = td["properties"]!["flow"]!.AsObject();
            Assert.Equal("nsu=urn:other;s=Flow", flow["uav:id"]!.GetValue<string>());
            Assert.Equal("nsu=urn:other;s=Flow", flow["uav:mapToNodeId"]!.GetValue<string>());
        }

        [Fact]
        public void Invalid_json_is_passed_through_for_the_registry_to_report()
        {
            byte[] content = Encoding.UTF8.GetBytes("not json");

            Assert.Same(content, WotImportNormalizer.Normalize(content));
        }
    }
}
