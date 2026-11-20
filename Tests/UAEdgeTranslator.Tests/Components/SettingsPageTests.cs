namespace Opc.Ua.Edge.Translator.Tests.Components
{
    using AngleSharp.Dom;
    using Bunit;
    using Microsoft.Extensions.DependencyInjection;
    using Opc.Ua.Edge.Translator.Components.Pages;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Interaction tests for the Settings page: listing the settings with their
    /// source, editing a value through the dialog, validation errors and
    /// resetting a saved value. Every setting's environment variable is
    /// snapshotted and restored, as saving a value applies it to the process.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class SettingsPageTests : BunitContext, IAsyncLifetime
    {
        private readonly Dictionary<string, string> _previous = new(StringComparer.Ordinal);

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "uaet-settings-page-" + Guid.NewGuid().ToString("N"));

        private TranslatorSettings _settings;

        private string FilePath => Path.Combine(_directory, TranslatorSettings.FileName);

        public Task InitializeAsync()
        {
            foreach (SettingDefinition definition in TranslatorSettings.Definitions)
            {
                _previous[definition.Name] = Environment.GetEnvironmentVariable(definition.Name);
                Environment.SetEnvironmentVariable(definition.Name, null);
            }

            return Task.CompletedTask;
        }

        Task IAsyncLifetime.DisposeAsync()
        {
            foreach (KeyValuePair<string, string> pair in _previous)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return Task.CompletedTask;
        }

        private IRenderedComponent<Settings> RenderPage(IDictionary<string, string> persisted = null)
        {
            if (persisted != null)
            {
                Directory.CreateDirectory(_directory);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(persisted));
            }

            _settings = new TranslatorSettings(FilePath);
            _settings.Load();
            Services.AddSingleton(_settings);

            return Render<Settings>();
        }

        private static IElement Row(IRenderedComponent<Settings> page, string name) =>
            page.FindAll("tr").Single(row => row.QuerySelector(".mono.strong")?.TextContent == name);

        private static void ClickEdit(IRenderedComponent<Settings> page, string name) =>
            Row(page, name).QuerySelectorAll("button").Single(b => b.TextContent == "Edit").Click();

        [Fact]
        public void Page_lists_every_category_and_setting_with_its_source()
        {
            Environment.SetEnvironmentVariable("UACLUsername", "env-user");
            Environment.SetEnvironmentVariable("OPCUA_PASSWORD", "env-secret");

            IRenderedComponent<Settings> page = RenderPage();

            foreach (string category in TranslatorSettings.Categories)
            {
                Assert.Contains(page.FindAll("h2.section-title"), h => h.TextContent == category);
            }

            Assert.Equal(TranslatorSettings.Definitions.Count, page.FindAll("tbody tr").Count);
            Assert.Contains(FilePath, page.Markup, StringComparison.Ordinal);

            IElement environmentRow = Row(page, "UACLUsername");
            Assert.Contains("env-user", environmentRow.QuerySelector(".setting-value").TextContent, StringComparison.Ordinal);
            Assert.Equal("Environment", environmentRow.QuerySelector(".pill-green").TextContent);

            IElement secretRow = Row(page, "OPCUA_PASSWORD");
            Assert.DoesNotContain("env-secret", secretRow.TextContent, StringComparison.Ordinal);
            Assert.Contains("\u2022\u2022\u2022\u2022", secretRow.QuerySelector(".setting-value").TextContent, StringComparison.Ordinal);

            IElement defaultRow = Row(page, "APP_NAME");
            Assert.Contains("default: UAEdgeTranslator", defaultRow.TextContent, StringComparison.Ordinal);
            Assert.Equal("Not set", defaultRow.QuerySelector(".pill-gray").TextContent);
            Assert.Equal("Restart", defaultRow.QuerySelector(".pill-orange").TextContent);

            Assert.Contains("not set", Row(page, "LICENSE_KEY").QuerySelector(".setting-value").TextContent, StringComparison.Ordinal);
            Assert.Empty(page.FindAll(".banner-warning"));
            Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent == "Reset");
        }

        [Fact]
        public void Saving_a_value_persists_it_and_offers_a_reset()
        {
            IRenderedComponent<Settings> page = RenderPage();

            ClickEdit(page, "WOTCON_MANAGEMENT_ROLE");
            Assert.Contains("Edit WOTCON_MANAGEMENT_ROLE", page.Find(".dialog-title").TextContent, StringComparison.Ordinal);
            Assert.Equal("text", page.Find(".dialog input").GetAttribute("type"));
            Assert.Contains("Saving an empty value stores the setting as unset", page.Find(".dialog").TextContent, StringComparison.Ordinal);

            page.Find(".dialog input").Input("ns=1;s=Operators");
            page.Find("form.dialog").Submit();

            Assert.Empty(page.FindAll(".dialog"));
            Assert.Contains("WOTCON_MANAGEMENT_ROLE saved. The change is in effect.", page.Find(".banner-success").TextContent, StringComparison.Ordinal);
            Assert.Equal("ns=1;s=Operators", Environment.GetEnvironmentVariable("WOTCON_MANAGEMENT_ROLE"));
            Assert.True(File.Exists(FilePath));

            IElement row = Row(page, "WOTCON_MANAGEMENT_ROLE");
            Assert.Equal("Saved", row.QuerySelector(".pill-blue").TextContent);
            Assert.Contains("ns=1;s=Operators", row.QuerySelector(".setting-value").TextContent, StringComparison.Ordinal);
            Assert.Contains(row.QuerySelectorAll("button"), b => b.TextContent == "Reset");
        }

        [Fact]
        public void Saving_a_restart_setting_shows_the_restart_banner()
        {
            IRenderedComponent<Settings> page = RenderPage();

            ClickEdit(page, "APP_NAME");
            Assert.Contains("This setting cannot be empty.", page.Find(".dialog").TextContent, StringComparison.Ordinal);
            Assert.Contains("Takes effect after a restart.", page.Find(".dialog").TextContent, StringComparison.Ordinal);

            page.Find(".dialog input").Input("RenamedTranslator");
            page.Find("form.dialog").Submit();

            Assert.Contains("Restart UA Edge Translator for the change to take effect.", page.Find(".banner-success").TextContent, StringComparison.Ordinal);
            Assert.Contains("APP_NAME", page.Find(".banner-warning").TextContent, StringComparison.Ordinal);
            Assert.Equal("Restart pending", Row(page, "APP_NAME").QuerySelector(".pill-orange").TextContent);
        }

        [Fact]
        public void Saving_an_empty_required_value_shows_the_error_in_the_dialog()
        {
            IRenderedComponent<Settings> page = RenderPage();

            ClickEdit(page, "APP_NAME");
            page.Find(".dialog input").Input("   ");
            Assert.True(page.Find(".dialog button[type=submit]").HasAttribute("disabled"));

            page.Find("form.dialog").Submit();

            Assert.Contains("APP_NAME cannot be empty.", page.Find(".dialog .banner-warning").TextContent, StringComparison.Ordinal);
            Assert.False(File.Exists(FilePath));
        }

        [Fact]
        public void Editing_a_secret_starts_empty_and_uses_a_password_field()
        {
            IRenderedComponent<Settings> page = RenderPage(new Dictionary<string, string> { ["LICENSE_KEY"] = "persisted-key" });

            ClickEdit(page, "LICENSE_KEY");

            IElement input = page.Find(".dialog input");
            Assert.Equal("password", input.GetAttribute("type"));
            Assert.Equal("enter a new value", input.GetAttribute("placeholder"));
            Assert.True(string.IsNullOrEmpty(input.GetAttribute("value")));
        }

        [Fact]
        public void Cancel_and_escape_close_the_dialog_without_saving()
        {
            IRenderedComponent<Settings> page = RenderPage();

            ClickEdit(page, "UACLUsername");
            page.Find(".dialog-actions button[type=button]").Click();
            Assert.Empty(page.FindAll(".dialog"));

            ClickEdit(page, "UACLUsername");
            page.Find("form.dialog").KeyDown("Enter");
            Assert.NotEmpty(page.FindAll(".dialog"));
            page.Find("form.dialog").KeyDown("Escape");
            Assert.Empty(page.FindAll(".dialog"));

            ClickEdit(page, "UACLUsername");
            page.Find(".dialog-backdrop").Click();
            Assert.Empty(page.FindAll(".dialog"));

            Assert.False(File.Exists(FilePath));
        }

        [Fact]
        public void Persisted_values_describe_the_environment_value_they_override()
        {
            Environment.SetEnvironmentVariable("UACLUsername", "env-user");
            Environment.SetEnvironmentVariable("UACLPassword", "env-password");

            IRenderedComponent<Settings> page = RenderPage(new Dictionary<string, string>
            {
                ["UACLUsername"] = "saved-user",
                ["UACLPassword"] = "saved-password",
            });

            IElement plainRow = Row(page, "UACLUsername");
            Assert.Contains("saved-user", plainRow.TextContent, StringComparison.Ordinal);
            Assert.Contains("Environment: env-user", plainRow.TextContent, StringComparison.Ordinal);

            IElement secretRow = Row(page, "UACLPassword");
            Assert.Contains("Overrides an environment value", secretRow.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("password", secretRow.QuerySelector(".setting-value").TextContent, StringComparison.Ordinal);
        }

        [Fact]
        public void Reset_restores_the_environment_value()
        {
            Environment.SetEnvironmentVariable("UACLUsername", "env-user");

            IRenderedComponent<Settings> page = RenderPage(new Dictionary<string, string> { ["UACLUsername"] = "saved-user" });

            Row(page, "UACLUsername").QuerySelectorAll("button").Single(b => b.TextContent == "Reset").Click();

            Assert.Contains("UACLUsername reset to its environment value.", page.Find(".banner-success").TextContent, StringComparison.Ordinal);
            Assert.Equal("env-user", Environment.GetEnvironmentVariable("UACLUsername"));
            Assert.Equal("Environment", Row(page, "UACLUsername").QuerySelector(".pill-green").TextContent);
        }

        [Fact]
        public void Reset_of_a_required_setting_without_environment_value_is_refused()
        {
            IRenderedComponent<Settings> page = RenderPage(new Dictionary<string, string> { ["OPCUA_USERNAME"] = "saved-admin" });

            Row(page, "OPCUA_USERNAME").QuerySelectorAll("button").Single(b => b.TextContent == "Reset").Click();

            Assert.Contains("is required and is not set in the environment", page.Find(".banner-warning").TextContent, StringComparison.Ordinal);
            Assert.Equal("saved-admin", Environment.GetEnvironmentVariable("OPCUA_USERNAME"));
        }

        [Fact]
        public void Refresh_reloads_values_saved_elsewhere()
        {
            IRenderedComponent<Settings> page = RenderPage();
            Assert.Equal("Not set", Row(page, "UACLUsername").QuerySelector(".pill-gray").TextContent);

            Assert.True(_settings.Set("UACLUsername", "other-user").Success);
            page.Find(".page-actions button.btn-refresh").Click();

            Assert.Equal("Saved", Row(page, "UACLUsername").QuerySelector(".pill-blue").TextContent);
        }
    }
}
