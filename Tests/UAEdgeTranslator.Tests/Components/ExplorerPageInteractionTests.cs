namespace Opc.Ua.Edge.Translator.Tests.Components
{
    using AngleSharp.Dom;
    using Bunit;
    using Microsoft.Extensions.DependencyInjection;
    using Opc.Ua.Edge.Translator.Components.Pages;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.Tests.Integration;
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// UI-driven tests for the Address Space Explorer: they click through the
    /// rendered tree, change the namespace filter and assert on the markup, so
    /// the detail panel, value badges and tree toggles are exercised the way a
    /// user drives them rather than through private members.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class ExplorerPageInteractionTests : BunitContext, IAsyncLifetime
    {
        private const string VariableName = "temperature";
        private const string FolderName = "explorerasset";
        private const string ManagementName = "WoTAssetConnectionManagement";

        // An imported asset gives a real browse path below the initially
        // expanded Objects folder: WoTAssetConnectionManagement -> asset -> tag.
        private const string ThingDescription = """
            {
              "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
              "id": "urn:explorerasset",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": [ "nosec_sc" ],
              "@type": [ "Thing" ],
              "name": "explorerasset",
              "title": "Explorer Asset",
              "base": "mock://127.0.0.1:1234/1",
              "properties": {
                "temperature": {
                  "type": "number",
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
                }
              }
            }
            """;

        private OpcUaServerFixture _fixture;

        public async Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();

            Services.AddSingleton<DiagnosticsService>();
            Services.AddSingleton<AddressSpaceService>();

            await UANodeManager.Instance.ImportWoTFileAsync("explorerasset.jsonld", ThingDescription).ConfigureAwait(false);
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
        public void Clicking_a_variable_shows_attributes_value_and_references()
        {
            IRenderedComponent<Explorer> page = Render<Explorer>();

            ExpandToSeededFolder(page);
            Label(page, VariableName).Click();

            page.WaitForAssertion(() => Assert.Equal(VariableName, page.Find(".detail-name").TextContent));

            Assert.Equal("Variable", page.Find(".detail-title .as-node-badge").TextContent);
            Assert.False(string.IsNullOrWhiteSpace(page.Find(".detail-namespace").TextContent));

            string[] sections = page.FindAll(".detail-section").Select(s => s.TextContent).ToArray();
            Assert.Contains("Attributes", sections);
            Assert.Contains("Value", sections);
            Assert.Contains(sections, s => s.StartsWith("References (", StringComparison.Ordinal));

            // The parent reference back to the asset is listed as an inverse reference.
            Assert.Contains(page.FindAll(".detail-table td span.pill"), p => p.TextContent == "Inverse");
            // Exactly one tree label is marked as the current selection.
            page.WaitForAssertion(() => Assert.Contains("is-selected", Label(page, VariableName).ClassName, StringComparison.Ordinal));
        }

        [Fact]
        public void Expanding_then_collapsing_a_folder_toggles_its_children()
        {
            IRenderedComponent<Explorer> page = Render<Explorer>();

            ExpandToSeededFolder(page);
            Assert.NotNull(Label(page, VariableName));

            ToggleFor(page, FolderName).Click();

            page.WaitForAssertion(() => Assert.Null(TryLabel(page, VariableName)));
            Assert.Equal("Expand", ToggleFor(page, FolderName).GetAttribute("title"));
        }

        [Fact]
        public void Selecting_a_namespace_highlights_its_nodes_and_shows_a_banner()
        {
            IRenderedComponent<Explorer> page = Render<Explorer>();

            ExpandToSeededFolder(page);
            Label(page, FolderName).Click();
            page.WaitForAssertion(() => Assert.Equal(FolderName, page.Find(".detail-name").TextContent));
            string assetNamespaceUri = page.Find(".detail-namespace").TextContent;

            string option = page.FindAll("#ns-select option")
                .Single(o => o.TextContent.EndsWith(assetNamespaceUri, StringComparison.Ordinal))
                .GetAttribute("value");

            page.Find("#ns-select").Change(option);

            page.WaitForAssertion(() =>
                Assert.Contains(assetNamespaceUri, page.Find(".banner-namespace").TextContent, StringComparison.Ordinal));
            page.WaitForAssertion(() =>
                Assert.Contains("is-in-namespace", Label(page, FolderName).ClassName, StringComparison.Ordinal));

            page.Find("#ns-select").Change("-1");

            page.WaitForAssertion(() => Assert.Empty(page.FindAll(".banner-namespace")));
        }

        [Fact]
        public void Refresh_rebrowses_but_keeps_the_expansion_and_selection()
        {
            IRenderedComponent<Explorer> page = Render<Explorer>();

            ExpandToSeededFolder(page);
            Label(page, VariableName).Click();
            page.WaitForAssertion(() => Assert.Equal(VariableName, page.Find(".detail-name").TextContent));

            page.Find(".btn-refresh[title='Refresh']").Click();

            page.WaitForAssertion(() =>
            {
                Assert.NotNull(TryLabel(page, VariableName));
                Assert.Equal(VariableName, page.Find(".detail-name").TextContent);
            });
        }

        [Fact]
        public void The_legend_lists_every_node_icon()
        {
            IRenderedComponent<Explorer> page = Render<Explorer>();

            string[] labels = page.FindAll(".as-legend-label").Select(l => l.TextContent).ToArray();

            Assert.Equal(["Object", "Folder", "Variable", "Property", "Method", "ObjectType", "InterfaceType", "EventType", "Condition", "VariableType", "ReferenceType", "DataType", "View"], labels);
        }

        [Fact]
        public void Each_legend_icon_is_coloured_by_its_icon_key()
        {
            IRenderedComponent<Explorer> page = Render<Explorer>();

            // The tree colours an icon by the same as-node-<icon> class, so one key has one colour.
            string[] classes = page.FindAll(".as-legend-item .as-node-icon").Select(i => i.ClassName).ToArray();

            Assert.Equal(13, classes.Distinct().Count());
            Assert.Contains("as-node-icon as-node-dataVariable", classes);
            Assert.Contains("as-node-icon as-node-condition", classes);
        }

        private static void ExpandToSeededFolder(IRenderedComponent<Explorer> page)
        {
            // Root and Objects are expanded on first load; the management
            // object hangs under Objects and the asset under it.
            ExpandIfCollapsed(page, ManagementName, FolderName);
            ExpandIfCollapsed(page, FolderName, VariableName);
        }

        private static void ExpandIfCollapsed(IRenderedComponent<Explorer> page, string parent, string child)
        {
            page.WaitForAssertion(() => Assert.NotNull(TryLabel(page, parent)));

            if (TryLabel(page, child) == null)
            {
                ToggleFor(page, parent).Click();
                page.WaitForAssertion(() => Assert.NotNull(TryLabel(page, child)));
            }
        }

        private static IElement Label(IRenderedComponent<Explorer> page, string text)
            => TryLabel(page, text) ?? throw new InvalidOperationException($"Tree label '{text}' not rendered.");

        private static IElement TryLabel(IRenderedComponent<Explorer> page, string text)
            => page.FindAll(".as-tree-label").FirstOrDefault(l => l.TextContent.Trim() == text);

        private static IElement ToggleFor(IRenderedComponent<Explorer> page, string text)
            => Label(page, text).ParentElement.QuerySelector("button.as-tree-toggle");
    }
}
