namespace Opc.Ua.Edge.Translator.Tests.Components
{
    using AngleSharp.Dom;
    using Bunit;
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.Forms;
    using Microsoft.AspNetCore.Components.Web;
    using Microsoft.Extensions.DependencyInjection;
    using Opc.Ua.Edge.Translator.Components.Pages;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.Tests.Integration;
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Interaction tests for the Projections page: opening a WoT document, mapping its
    /// properties onto OPC UA types from the Types tree (by drag and drop and by the
    /// "Map selected type" button), creating a Thing Description from a Thing Model and
    /// saving and applying the result through the real <see cref="UANodeManager"/>.
    /// <para>
    /// The type tree loads its branches asynchronously, so every event is dispatched on
    /// the renderer's dispatcher against a freshly found element and awaited there.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class ProjectionsPageTests : BunitContext, IAsyncLifetime
    {
        private const string ThingDescription = """
            {
              "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
              "id": "urn:projected",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": [ "nosec_sc" ],
              "@type": [ "Thing" ],
              "name": "projected",
              "title": "Projected Asset",
              "base": "mock://127.0.0.1:1234/1",
              "properties": {
                "temperature": {
                  "type": "number",
                  "description": "Boiler temperature",
                  "readOnly": true,
                  "observable": true,
                  "forms": [
                    {
                      "href": "127.0.0.1/1?quantity=2",
                      "op": [ "readproperty", "observeproperty" ],
                      "type": "xsd:short",
                      "pollingTime": 1000
                    }
                  ]
                },
                "low": {
                  "type": "number",
                  "readOnly": true,
                  "forms": [
                    {
                      "href": "127.0.0.1/3?quantity=2",
                      "op": [ "readproperty" ],
                      "type": "xsd:short",
                      "pollingTime": 1000
                    }
                  ]
                }
              }
            }
            """;

        private const string ThingModel = """
            {
              "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
              "@type": [ "tm:ThingModel" ],
              "title": "Boiler {{MODEL}}",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": [ "nosec_sc" ],
              "base": "mock://{{HOST}}:1234/1",
              "properties": {
                "temperature": {
                  "type": "number",
                  "forms": [ { "href": "127.0.0.1/1?quantity=2", "op": [ "readproperty" ], "type": "xsd:short" } ]
                }
              }
            }
            """;

        private static readonly string[] s_double = ["DataTypes", "BaseDataType", "Number", "Double"];
        private static readonly string[] s_range = ["DataTypes", "BaseDataType", "Structure", "Range"];

        private const string Pnem = "http://opcfoundation.org/UA/PNEM/";
        private const string Di = "http://opcfoundation.org/UA/DI/";

        private OpcUaServerFixture _fixture;

        public Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();

            Services.AddSingleton<DiagnosticsService>();
            Services.AddSingleton<AddressSpaceService>();

            return Task.CompletedTask;
        }

        async Task IAsyncLifetime.DisposeAsync()
        {
            if (_fixture != null)
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
                _fixture = null;
            }
        }

        [Fact]
        public void Opening_a_thing_description_lists_its_properties()
        {
            IRenderedComponent<Projections> page = RenderPage();

            Open(page, "projected.jsonld", ThingDescription);

            page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".prop-card").Count));
            Assert.Equal("Projected Asset", page.Find(".projection-wot-card .card-title").TextContent);
            Assert.Contains("Boiler temperature", page.Find(".prop-card").TextContent, StringComparison.Ordinal);
            Assert.Equal("s=temperature", page.Find(".prop-card .nodeid-value").TextContent);
            Assert.Contains("0 / 2 mapped", page.Find(".projection-file").TextContent, StringComparison.Ordinal);
            Assert.NotNull(ActionButton(page, "Save & Apply"));
            Assert.Null(ActionButton(page, "Create Thing Description"));
            Assert.StartsWith("data:application/ld+json;base64,", page.Find(".projection-download").GetAttribute("href"), StringComparison.Ordinal);
        }

        [Fact]
        public async Task Dropping_a_simple_type_onto_a_property_maps_it()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Open(page, "projected.jsonld", ThingDescription);
            page.WaitForElement(".prop-card");

            await ExpandAsync(page, s_double);
            await page.InvokeAsync(() => Label(page, "Double").DragStartAsync(new DragEventArgs()));
            await page.InvokeAsync(() => PropertyCard(page, "temperature").DragEnterAsync(new DragEventArgs()));
            Assert.Contains("drop-hover", PropertyCard(page, "temperature").ClassList);
            await page.InvokeAsync(() => PropertyCard(page, "temperature").DropAsync(new DragEventArgs()));

            page.WaitForAssertion(() => Assert.Contains("mapped", PropertyCard(page, "temperature").ClassList));
            Assert.DoesNotContain("drop-hover", PropertyCard(page, "temperature").ClassList);
            Assert.Contains("i=11", PropertyCard(page, "temperature").QuerySelector(".prop-map").TextContent, StringComparison.Ordinal);
            Assert.Contains("1 / 2 mapped", page.Find(".projection-file").TextContent, StringComparison.Ordinal);
            Assert.Contains("Unsaved changes", page.Find(".projection-file").TextContent, StringComparison.Ordinal);

            // The downloaded document carries the projection.
            JsonObject property = Downloaded(page)["properties"]["temperature"].AsObject();
            Assert.Equal("i=11", property["uav:mapToType"].GetValue<string>());
            Assert.Equal("s=temperature", property["uav:mapToNodeId"].GetValue<string>());
        }

        [Fact]
        public async Task Mapping_a_structure_asks_for_the_field_and_preselects_the_one_named_like_the_property()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Open(page, "projected.jsonld", ThingDescription);
            page.WaitForElement(".prop-card");

            await DragOntoAsync(page, s_range, "low");

            page.WaitForElement(".field-list");
            Assert.Equal(["Low", "High"], page.FindAll(".field-option span").Select(s => s.TextContent));
            Assert.True(page.FindAll(".field-option input")[0].HasAttribute("checked"), "the field named like the property is preselected");
            Assert.False(page.FindAll(".field-option input")[1].HasAttribute("checked"));

            // Pick the other field and confirm.
            await page.InvokeAsync(() => page.FindAll(".field-option input")[1].ChangeAsync(new ChangeEventArgs { Value = true }));
            await ClickAsync(page, () => DialogButton(page, "Map"));

            page.WaitForAssertion(() => Assert.Empty(page.FindAll(".field-list")));
            string map = PropertyCard(page, "low").QuerySelector(".prop-map").TextContent;
            Assert.Contains("i=884", map, StringComparison.Ordinal);
            Assert.Contains("High", map, StringComparison.Ordinal);
            Assert.Equal("High", Downloaded(page)["properties"]["low"]["uav:mapByFieldPath"].GetValue<string>());
        }

        [Fact]
        public async Task Cancelling_the_field_dialog_leaves_the_property_unmapped()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Open(page, "projected.jsonld", ThingDescription);
            page.WaitForElement(".prop-card");

            await DragOntoAsync(page, s_range, "temperature");
            page.WaitForElement(".field-list");

            // No field is named like the property, so none is preselected and Map is disabled.
            Assert.DoesNotContain(page.FindAll(".field-option input"), i => i.HasAttribute("checked"));
            Assert.True(DialogButton(page, "Map").HasAttribute("disabled"));

            await ClickAsync(page, () => DialogButton(page, "Cancel"));

            page.WaitForAssertion(() => Assert.Empty(page.FindAll(".field-list")));
            Assert.DoesNotContain("mapped", PropertyCard(page, "temperature").ClassList);
        }

        [Fact]
        public async Task The_selected_type_can_be_mapped_with_a_button_and_the_mapping_removed_again()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Open(page, "projected.jsonld", ThingDescription);
            page.WaitForElement(".prop-card");
            Assert.Null(CardButton(page, "temperature", "Map selected type"));

            await ExpandAsync(page, s_double);
            await ClickAsync(page, () => Label(page, "Double"));
            await ClickAsync(page, () => CardButton(page, "temperature", "Map selected type"));

            page.WaitForAssertion(() => Assert.Contains("mapped", PropertyCard(page, "temperature").ClassList));

            await ClickAsync(page, () => CardButton(page, "temperature", "Remove mapping"));

            page.WaitForAssertion(() => Assert.DoesNotContain("mapped", PropertyCard(page, "temperature").ClassList));
            Assert.Null(Downloaded(page)["properties"]["temperature"]["uav:mapToType"]);
        }

        [Fact]
        public async Task A_node_that_cannot_be_mapped_reports_why()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Open(page, "projected.jsonld", ThingDescription);
            page.WaitForElement(".prop-card");

            await page.InvokeAsync(() => Label(page, "DataTypes").DragStartAsync(new DragEventArgs()));
            await page.InvokeAsync(() => PropertyCard(page, "temperature").DropAsync(new DragEventArgs()));

            page.WaitForAssertion(() => Assert.Contains("drag a DataType, an ObjectType or a Variable", page.Find(".banner-warning").TextContent, StringComparison.Ordinal));
            Assert.DoesNotContain("mapped", PropertyCard(page, "temperature").ClassList);
        }

        [Fact]
        public async Task The_target_node_id_can_be_edited_inline()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Open(page, "projected.jsonld", ThingDescription);
            page.WaitForElement(".prop-card");

            await ClickAsync(page, () => PropertyCard(page, "temperature").QuerySelector(".nodeid-value"));
            await page.InvokeAsync(() => page.Find(".nodeid-edit").InputAsync(new ChangeEventArgs { Value = "s=BoilerTemperature" }));
            await page.InvokeAsync(() => page.Find(".nodeid-edit").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" }));

            page.WaitForAssertion(() => Assert.Empty(page.FindAll(".nodeid-edit")));
            Assert.Equal("s=BoilerTemperature", PropertyCard(page, "temperature").QuerySelector(".nodeid-value").TextContent);
            Assert.Equal("s=BoilerTemperature", Downloaded(page)["properties"]["temperature"]["uav:mapToNodeId"].GetValue<string>());
            Assert.Contains("Unsaved changes", page.Find(".projection-file").TextContent, StringComparison.Ordinal);

            // Escape discards an edit.
            await ClickAsync(page, () => PropertyCard(page, "temperature").QuerySelector(".nodeid-value"));
            await page.InvokeAsync(() => page.Find(".nodeid-edit").InputAsync(new ChangeEventArgs { Value = "s=Discarded" }));
            await page.InvokeAsync(() => page.Find(".nodeid-edit").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" }));

            page.WaitForAssertion(() => Assert.Empty(page.FindAll(".nodeid-edit")));
            Assert.Equal("s=BoilerTemperature", PropertyCard(page, "temperature").QuerySelector(".nodeid-value").TextContent);
        }

        [Fact]
        public async Task A_thing_model_is_turned_into_a_thing_description()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Open(page, "boiler.tm.jsonld", ThingModel);
            page.WaitForElement(".prop-card");
            Assert.Contains("Thing Model", page.Find(".projection-file").TextContent, StringComparison.Ordinal);
            Assert.Null(ActionButton(page, "Save & Apply"));

            await ClickAsync(page, () => ActionButton(page, "Create Thing Description"));

            page.WaitForElement(".td-placeholders");
            Assert.Equal(["MODEL", "HOST"], page.FindAll(".td-placeholders .dialog-field span").Select(s => s.TextContent));
            Assert.True(DialogButton(page, "Create").HasAttribute("disabled"), "a name is required");

            await page.InvokeAsync(() => page.FindAll(".td-placeholders input")[0].ChangeAsync(new ChangeEventArgs { Value = "B-200" }));
            await page.InvokeAsync(() => page.FindAll(".td-placeholders input")[1].ChangeAsync(new ChangeEventArgs { Value = "127.0.0.1" }));
            await page.InvokeAsync(() => page.FindAll(".dialog input").Last().InputAsync(new ChangeEventArgs { Value = "boiler-1" }));
            await ClickAsync(page, () => DialogButton(page, "Create"));

            page.WaitForAssertion(() => Assert.Empty(page.FindAll(".td-placeholders")));
            Assert.Contains("Created the Thing Description 'boiler-1'", page.Find(".banner-success").TextContent, StringComparison.Ordinal);
            Assert.Equal("Boiler B-200", page.Find(".projection-wot-card .card-title").TextContent);
            Assert.Contains("boiler-1.jsonld", page.Find(".projection-file").TextContent, StringComparison.Ordinal);
            Assert.NotNull(ActionButton(page, "Save & Apply"));

            JsonObject td = Downloaded(page);
            Assert.Equal("boiler-1", td["name"].GetValue<string>());
            Assert.Equal("mock://127.0.0.1:1234/1", td["base"].GetValue<string>());
            Assert.Null(td["@type"]);
        }

        [Fact]
        public async Task Save_and_apply_writes_the_settings_file_and_onboards_the_asset()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Open(page, "projected.jsonld", ThingDescription);
            page.WaitForElement(".prop-card");

            await DragOntoAsync(page, s_double, "temperature");
            page.WaitForAssertion(() => Assert.Contains("mapped", PropertyCard(page, "temperature").ClassList));

            await ClickAsync(page, () => ActionButton(page, "Save & Apply"));

            page.WaitForAssertion(
                () => Assert.Contains("Saved 'projected.jsonld' and onboarded the asset 'projected' with 1 projection(s).", page.Find(".banner-success").TextContent, StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            Assert.DoesNotContain("Unsaved changes", page.Find(".projection-file").TextContent, StringComparison.Ordinal);

            string settings = Path.Combine(_fixture.WorkingDirectory, "settings");
            string settingsFile = Path.Combine(settings, "projected.jsonld");
            JsonObject saved = JsonNode.Parse(File.ReadAllText(settingsFile)).AsObject();
            Assert.Equal("i=11", saved["properties"]["temperature"]["uav:mapToType"].GetValue<string>());
            Assert.Contains(page.Find("#wot-file-select").QuerySelectorAll("option"), o => o.GetAttribute("value") == "projected.jsonld");

            // Applying again replaces the asset rather than failing because it exists.
            await ClickAsync(page, () => CardButton(page, "temperature", "Remove mapping"));
            await ClickAsync(page, () => ActionButton(page, "Save & Apply"));

            page.WaitForAssertion(
                () => Assert.Contains("with 0 projection(s).", page.Find(".banner-success").TextContent, StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
            saved = JsonNode.Parse(File.ReadAllText(settingsFile)).AsObject();
            Assert.Null(saved["properties"]["temperature"]["uav:mapToType"]);
            Assert.Equal(["projected.jsonld"], Directory.GetFiles(settings, "*.jsonld").Select(Path.GetFileName));
        }

        [Fact]
        public async Task A_settings_file_can_be_chosen_from_the_list()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Open(page, "projected.jsonld", ThingDescription);
            page.WaitForElement(".prop-card");
            await ClickAsync(page, () => ActionButton(page, "Save & Apply"));
            page.WaitForElement(".banner-success", TimeSpan.FromSeconds(10));

            IRenderedComponent<Projections> fresh = RenderPage();
            Assert.Empty(fresh.FindAll(".prop-card"));
            Assert.Contains(fresh.FindAll("#wot-file-select option"), o => o.GetAttribute("value") == "projected.jsonld");

            await fresh.InvokeAsync(() => fresh.Find("#wot-file-select").ChangeAsync(new ChangeEventArgs { Value = "projected.jsonld" }));

            fresh.WaitForAssertion(() => Assert.Equal(2, fresh.FindAll(".prop-card").Count));
            Assert.Equal("Projected Asset", fresh.Find(".projection-wot-card .card-title").TextContent);
            Assert.Contains("projected.jsonld", fresh.Find(".projection-file").TextContent, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_thing_description_without_a_name_is_not_applied()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Open(page, "nameless.jsonld", ThingDescription.Replace("\"name\": \"projected\",", string.Empty, StringComparison.Ordinal));
            page.WaitForElement(".prop-card");

            await ClickAsync(page, () => ActionButton(page, "Save & Apply"));

            page.WaitForAssertion(() => Assert.Contains("has no 'name'", page.Find(".banner-warning").TextContent, StringComparison.Ordinal));
            Assert.Empty(Directory.GetFiles(Path.Combine(_fixture.WorkingDirectory, "settings"), "*.jsonld"));
        }

        [Fact]
        public void An_invalid_file_reports_an_error()
        {
            IRenderedComponent<Projections> page = RenderPage();

            Open(page, "broken.jsonld", "{ not json");

            page.WaitForAssertion(() => Assert.Contains("'broken.jsonld' is not a valid WoT document", page.Find(".banner-warning").TextContent, StringComparison.Ordinal));
            Assert.Empty(page.FindAll(".prop-card"));
        }

        [Fact]
        public async Task A_companion_model_is_loaded_by_namespace_without_the_registry()
        {
            CopyTestNodeSets("opcfoundation.org.UA.DI.nodeset2.xml", "opcfoundation.org.UA.PNEM.nodeset2.xml");
            IRenderedComponent<Projections> page = RenderPage();

            // The suggestions list the models in the nodesets folder.
            page.WaitForAssertion(() => Assert.Contains(page.FindAll("#projection-companion-models option"), o => o.GetAttribute("value") == Pnem));

            await page.InvokeAsync(() => page.Find(".projection-companion-uri").InputAsync(new ChangeEventArgs { Value = Pnem }));
            await ClickAsync(page, () => page.FindAll(".projection-companion button").Single(b => b.TextContent.Trim() == "Load"));

            page.WaitForAssertion(
                () => Assert.Contains($"Loaded the companion model '{Pnem}'.", page.Find(".banner-success").TextContent, StringComparison.Ordinal),
                TimeSpan.FromSeconds(30));

            // The namespace of the loaded model is highlighted, and its types can be mapped.
            Assert.Contains(Pnem, page.Find("#projection-ns-select option:checked").TextContent, StringComparison.Ordinal);
            Open(page, "projected.jsonld", ThingDescription);
            page.WaitForElement(".prop-card");
            await DragOntoAsync(page, ["DataTypes", "BaseDataType", "Structure", "AcPeDataType"], "temperature");
            page.WaitForElement(".field-list");
            Assert.Equal(["A", "B", "C"], page.FindAll(".field-option span").Select(s => s.TextContent));
            await page.InvokeAsync(() => page.FindAll(".field-option input")[0].ChangeAsync(new ChangeEventArgs { Value = true }));
            await ClickAsync(page, () => DialogButton(page, "Map"));

            JsonObject td = Downloaded(page);
            Assert.Equal("nsu=" + Pnem + ";i=3005", td["properties"]["temperature"]["uav:mapToType"].GetValue<string>());
            Assert.Equal(Pnem, td["@context"][1]["uav"].GetValue<string>());
        }

        [Fact]
        public async Task An_uploaded_nodeset_is_loaded_without_the_registry()
        {
            IRenderedComponent<Projections> page = RenderPage();
            Assert.Equal(2, page.FindComponents<InputFile>().Count);

            string xml = await File.ReadAllTextAsync(TestNodeSet("opcfoundation.org.UA.DI.nodeset2.xml"));
            page.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(xml, "di.xml"));

            page.WaitForAssertion(
                () => Assert.Contains($"Loaded {Di} from 'di.xml'.", page.Find(".banner-success").TextContent, StringComparison.Ordinal),
                TimeSpan.FromSeconds(30));
            Assert.True(File.Exists(Path.Combine(_fixture.WorkingDirectory, "nodesets", "opcfoundation.org.UA.DI.nodeset2.xml")));
            Assert.True(UANodeManager.Instance.NamespaceUris.Contains(Di));
        }

        [Fact]
        public void An_unknown_companion_model_reports_an_error()
        {
            IRenderedComponent<Projections> page = RenderPage();

            page.Find(".projection-companion-uri").Input("http://example.com/UA/Unknown/");
            page.FindAll(".projection-companion button").Single(b => b.TextContent.Trim() == "Load").Click();

            page.WaitForAssertion(() => Assert.Contains("could not be found in the nodesets folder or the UA Cloud Library", page.Find(".banner-warning").TextContent, StringComparison.Ordinal));
        }

        private void CopyTestNodeSets(params string[] names)
        {
            foreach (string name in names)
            {
                File.Copy(TestNodeSet(name), Path.Combine(_fixture.WorkingDirectory, "nodesets", name), overwrite: true);
            }
        }

        private static string TestNodeSet(string name)
        {
            string relativePath = Path.Combine("Tests", "UAEdgeTranslator.Tests", "TestData", "nodesets", name);
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

        private IRenderedComponent<Projections> RenderPage()
        {
            IRenderedComponent<Projections> page = Render<Projections>();
            page.WaitForAssertion(() => Assert.NotNull(Label(page, "DataTypes")), TimeSpan.FromSeconds(10));
            return page;
        }

        private static void Open(IRenderedComponent<Projections> page, string fileName, string contents)
        {
            page.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText(contents, fileName));
        }

        private static Task ClickAsync(IRenderedComponent<Projections> page, Func<IElement> find)
        {
            return page.InvokeAsync(() => find().ClickAsync(new MouseEventArgs()));
        }

        /// <summary>Expands each branch on the way to the last name in the path.</summary>
        private static async Task ExpandAsync(IRenderedComponent<Projections> page, string[] path)
        {
            foreach (string branch in path.Take(path.Length - 1))
            {
                page.WaitForAssertion(() => Assert.NotNull(Label(page, branch)), TimeSpan.FromSeconds(10));
                await ClickAsync(page, () => Label(page, branch).ParentElement.QuerySelector("button.as-tree-toggle"));
            }

            page.WaitForAssertion(() => Assert.NotNull(Label(page, path[^1])), TimeSpan.FromSeconds(10));
        }

        private static async Task DragOntoAsync(IRenderedComponent<Projections> page, string[] typePath, string property)
        {
            await ExpandAsync(page, typePath);
            await page.InvokeAsync(() => Label(page, typePath[^1]).DragStartAsync(new DragEventArgs()));
            await page.InvokeAsync(() => PropertyCard(page, property).DropAsync(new DragEventArgs()));
        }

        private static IElement PropertyCard(IRenderedComponent<Projections> page, string name)
        {
            return page.FindAll(".prop-card").Single(c => c.QuerySelector(".prop-name").FirstChild.TextContent.Trim() == name);
        }

        private static IElement CardButton(IRenderedComponent<Projections> page, string property, string text)
        {
            return PropertyCard(page, property).QuerySelectorAll("button").SingleOrDefault(b => b.TextContent.Trim() == text);
        }

        private static IElement ActionButton(IRenderedComponent<Projections> page, string text)
        {
            return page.FindAll(".projection-actions button").SingleOrDefault(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal));
        }

        private static IElement DialogButton(IRenderedComponent<Projections> page, string text)
        {
            return page.FindAll(".dialog-actions button").Single(b => b.TextContent.Trim() == text);
        }

        private static IElement Label(IRenderedComponent<Projections> page, string text)
        {
            return page.FindAll(".as-tree-label").FirstOrDefault(l => l.TextContent.Trim() == text);
        }

        private static JsonObject Downloaded(IRenderedComponent<Projections> page)
        {
            string href = page.Find(".projection-download").GetAttribute("href");
            string base64 = href.Substring(href.IndexOf(',', StringComparison.Ordinal) + 1);
            return JsonNode.Parse(Convert.FromBase64String(base64)).AsObject();
        }
    }
}
