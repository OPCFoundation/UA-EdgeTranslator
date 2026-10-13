namespace Opc.Ua.Edge.Translator.Tests.Components
{
    using Bunit;
    using Microsoft.AspNetCore.Components.Forms;
    using Microsoft.Extensions.DependencyInjection;
    using Opc.Ua.Edge.Translator.Components.Pages;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.Tests.Integration;
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Interaction tests for the WoT Files page: importing a Thing Description
    /// through the file input, selecting it, switching between the tree and raw
    /// views, and deleting it again. They run against a live server so the
    /// import and unload paths go through the real <see cref="UANodeManager"/>.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class WotFilesPageTests : BunitContext, IAsyncLifetime
    {
        private const string ThingDescription = """
            {
              "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
              "id": "urn:wotpage",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": [ "nosec_sc" ],
              "@type": [ "Thing" ],
              "name": "wotpage",
              "title": "WoT Page Asset",
              "description": "Imported by the WoT Files page tests.",
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
        public void Importing_a_file_lists_and_selects_it()
        {
            IRenderedComponent<WotFiles> page = Render<WotFiles>();

            Import(page, "wotpage.jsonld", ThingDescription);

            page.WaitForAssertion(() =>
            {
                Assert.Contains("Imported 'wotpage.jsonld' successfully.", page.Markup, StringComparison.Ordinal);
                Assert.Contains("WoT Page Asset", page.Find(".detail-title").TextContent, StringComparison.Ordinal);
            });

            // Base scheme pill, description and counts come from the parsed TD.
            Assert.Equal("mock", page.Find(".detail-header .pill").TextContent);
            Assert.Contains("Imported by the WoT Files page tests.", page.Markup, StringComparison.Ordinal);
            Assert.Contains("mock://127.0.0.1:1234/1", page.Markup, StringComparison.Ordinal);

            // The tree view renders by default for a parseable document.
            Assert.NotEmpty(page.FindAll(".json-tree"));
        }

        [Fact]
        public void Raw_and_tree_toggle_switches_the_document_view()
        {
            IRenderedComponent<WotFiles> page = Render<WotFiles>();
            Import(page, "wotpage.jsonld", ThingDescription);
            page.WaitForElement(".json-tree");

            page.FindAll(".seg-btn").Single(b => b.TextContent == "Raw").Click();

            Assert.Empty(page.FindAll(".json-tree"));
            Assert.Contains("urn:wotpage", page.Find("pre.code-block").TextContent, StringComparison.Ordinal);

            page.FindAll(".seg-btn").Single(b => b.TextContent == "Tree").Click();

            Assert.NotEmpty(page.FindAll(".json-tree"));
        }

        [Fact]
        public void Selecting_a_file_in_the_list_shows_its_details()
        {
            IRenderedComponent<WotFiles> page = Render<WotFiles>();
            Import(page, "wotpage.jsonld", ThingDescription);
            Import(page, "second.jsonld", ThingDescription.Replace("wotpage", "second", StringComparison.Ordinal).Replace("WoT Page Asset", "Second Asset", StringComparison.Ordinal));
            page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".list-item").Count));

            page.FindAll(".list-item").Single(b => b.TextContent.Contains("WoT Page Asset", StringComparison.Ordinal)).Click();

            Assert.Equal("WoT Page Asset", page.Find(".detail-title").TextContent);
            Assert.Contains("active", page.FindAll(".list-item").Single(b => b.TextContent.Contains("WoT Page Asset", StringComparison.Ordinal)).ClassName, StringComparison.Ordinal);
        }

        [Fact]
        public void Deleting_a_file_removes_it_from_the_list()
        {
            IRenderedComponent<WotFiles> page = Render<WotFiles>();
            Import(page, "wotpage.jsonld", ThingDescription);
            page.WaitForElement(".detail-unload");

            page.Find(".detail-unload").Click();

            page.WaitForAssertion(() =>
            {
                Assert.Contains("Deleted 'WoT Page Asset'.", page.Markup, StringComparison.Ordinal);
                Assert.Contains("No WoT files loaded", page.Markup, StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void Importing_an_invalid_document_shows_an_error_banner()
        {
            IRenderedComponent<WotFiles> page = Render<WotFiles>();

            Import(page, "broken.jsonld", "{ this is not json");

            page.WaitForAssertion(() =>
                Assert.Contains("Failed to import 'broken.jsonld'", page.Find(".banner-warning").InnerHtml, StringComparison.Ordinal));
        }

        [Fact]
        public void Refresh_reloads_the_list_and_keeps_the_selection()
        {
            IRenderedComponent<WotFiles> page = Render<WotFiles>();
            Import(page, "wotpage.jsonld", ThingDescription);
            page.WaitForElement(".detail-title");

            page.Find(".btn-refresh[title='Refresh']").Click();

            Assert.Equal("WoT Page Asset", page.Find(".detail-title").TextContent);
        }

        private static void Import(IRenderedComponent<WotFiles> page, string fileName, string contents)
        {
            page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(contents, fileName));
        }
    }
}
