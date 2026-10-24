namespace Opc.Ua.Edge.Translator.Tests.Components
{
    using Bunit;
    using Microsoft.Extensions.DependencyInjection;
    using Opc.Ua.Edge.Translator.Components.Pages;
    using Opc.Ua.Edge.Translator.Components.Shared;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.Tests.Integration;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Component tests for the diagnostics dashboard pages.
    /// <para>
    /// They render against the real <see cref="DiagnosticsService"/> backed by a
    /// live server, so a page that mis-binds a model property fails here rather
    /// than only being noticed in a browser. Each page is also rendered twice
    /// where it has a refresh path, since the auto-refresh timers re-render
    /// continuously in production.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class DashboardPageTests : BunitContext, IAsyncLifetime
    {
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
        public void Overview_renders_the_application_identity_and_counters()
        {
            IRenderedComponent<Overview> page = Render<Overview>();

            Assert.Contains("UAEdgeTranslatorTest", page.Markup, System.StringComparison.Ordinal);
            Assert.Contains("Overview", page.Markup, System.StringComparison.Ordinal);

            // The endpoint list is one of the page's headline sections.
            Assert.Contains("opc.tcp://", page.Markup, System.StringComparison.Ordinal);
        }

		[Fact]
		public void Overview_renders_the_version_and_build_number()
		{
			IRenderedComponent<Overview> page = Render<Overview>();

			Assert.Matches(@"<span class=""kv-key"">Version</span><span class=""kv-value"">\d+\.\d+\.\d+</span>", page.Markup);
			Assert.Matches(@"<span class=""kv-key"">Build</span><span class=""kv-value mono"">\d+( \([0-9a-f]+\))?</span>", page.Markup);
		}
        [Fact]
        public void OpcUaSettings_renders_security_policies_and_limits()
        {
            IRenderedComponent<OpcUaSettings> page = Render<OpcUaSettings>();

            Assert.Contains("OPC UA Settings", page.Markup, System.StringComparison.Ordinal);

            // Policies come straight from the running configuration.
            Assert.Contains("None", page.Markup, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Drivers_page_lists_the_registered_driver()
        {
            IRenderedComponent<Drivers> page = Render<Drivers>();

            Assert.Contains("Protocol Drivers", page.Markup, System.StringComparison.Ordinal);

            // The fixture registers MockProtocolDriver, scheme "mock".
            Assert.Contains("mock", page.Markup, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Devices_page_renders_without_any_onboarded_asset()
        {
            // The empty state is the first thing a new operator sees.
            IRenderedComponent<Devices> page = Render<Devices>();

            Assert.Contains("Connected Assets", page.Markup, System.StringComparison.Ordinal);
        }

        private const string AddButton = "button[title^='Create an asset for an endpoint']";

        [Fact]
        public void Devices_add_dialog_suggests_a_name_from_the_endpoint()
        {
            IRenderedComponent<Devices> page = Render<Devices>();

            Assert.Empty(page.FindAll("form.dialog"));
            page.Find(AddButton).Click();

            var inputs = page.FindAll("form.dialog input");
            inputs[0].Input("modbus://192.168.1.10:502");

            Assert.Equal("modbus-192.168.1.10", page.FindAll("form.dialog input")[1].GetAttribute("value"));
        }

        [Fact]
        public void Devices_add_dialog_keeps_a_name_the_user_typed()
        {
            IRenderedComponent<Devices> page = Render<Devices>();
            page.Find(AddButton).Click();

            page.FindAll("form.dialog input")[1].Input("my-plc");
            page.FindAll("form.dialog input")[0].Input("modbus://192.168.1.10:502");

            Assert.Equal("my-plc", page.FindAll("form.dialog input")[1].GetAttribute("value"));
        }

        [Fact]
        public void Devices_add_dialog_closes_on_cancel()
        {
            IRenderedComponent<Devices> page = Render<Devices>();
            page.Find(AddButton).Click();

            page.Find("form.dialog button[type='button']").Click();

            Assert.Empty(page.FindAll("form.dialog"));
        }

        [Fact]
        public void Devices_add_dialog_stays_open_and_reports_why_creating_failed()
        {
            // No driver serves this scheme, so CreateAssetForEndpoint must reject it
            // and the dialog keeps the endpoint for correction.
            IRenderedComponent<Devices> page = Render<Devices>();
            page.Find(AddButton).Click();
            page.FindAll("form.dialog input")[0].Input("nosuchprotocol://10.0.0.1/");

            page.Find("form.dialog").Submit();

            page.WaitForAssertion(() =>
            {
                Assert.Single(page.FindAll("form.dialog"));
                Assert.Contains("failed", page.Find("form.dialog .banner-warning").TextContent, System.StringComparison.Ordinal);
            }, System.TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void Certificates_page_renders_the_application_certificate()
        {
            IRenderedComponent<Certificates> page = Render<Certificates>();

            Assert.Contains("Certificates", page.Markup, System.StringComparison.Ordinal);

            // The server generates its own certificate at fixture startup.
            Assert.Contains("UAEdgeTranslatorTest", page.Markup, System.StringComparison.Ordinal);
        }

        [Fact]
        public void WotFiles_page_renders_its_empty_state()
        {
            IRenderedComponent<WotFiles> page = Render<WotFiles>();

            Assert.Contains("WoT Files", page.Markup, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Pages_can_be_re_rendered_without_throwing()
        {
            // The dashboard re-renders on a timer; a page that only survives its
            // first render would fail seconds after being opened.
            IRenderedComponent<Overview> overview = Render<Overview>();
            overview.Render();

            IRenderedComponent<Drivers> drivers = Render<Drivers>();
            drivers.Render();

            Assert.NotEmpty(overview.Markup);
            Assert.NotEmpty(drivers.Markup);
        }

        [Fact]
        public void Disposable_pages_release_their_timers()
        {
            // Overview / Devices / Certificates own a PeriodicTimer; disposing
            // must not throw or the circuit teardown breaks.
            IRenderedComponent<Overview> overview = Render<Overview>();
            IRenderedComponent<Devices> devices = Render<Devices>();
            IRenderedComponent<Certificates> certificates = Render<Certificates>();

            overview.Instance.Dispose();
            devices.Instance.Dispose();
            certificates.Instance.Dispose();
        }

        [Theory]
        [InlineData("connected", "Connected")]
        [InlineData("disconnected", "Disconnected")]
        [InlineData("", "")]
        public void StatusPill_renders_its_label(string status, string label)
        {
            IRenderedComponent<StatusPill> pill = Render<StatusPill>(p => p
                .Add(c => c.Status, status)
                .Add(c => c.Label, label));

            Assert.NotNull(pill.Markup);

            if (!string.IsNullOrEmpty(label))
            {
                Assert.Contains(label, pill.Markup, System.StringComparison.Ordinal);
            }
        }

        [Fact]
        public void PageHeader_renders_title_subtitle_and_refresh_button()
        {
            IRenderedComponent<PageHeader> header = Render<PageHeader>(p => p
                .Add(c => c.Title, "A Title")
                .Add(c => c.Subtitle, "A subtitle")
                .Add(c => c.OnRefresh, () => { }));

            Assert.Contains("A Title", header.Markup, System.StringComparison.Ordinal);
            Assert.Contains("A subtitle", header.Markup, System.StringComparison.Ordinal);
            Assert.Contains("btn-refresh", header.Markup, System.StringComparison.Ordinal);
        }

        [Fact]
        public void PageHeader_omits_the_refresh_button_when_no_handler_is_supplied()
        {
            IRenderedComponent<PageHeader> header = Render<PageHeader>(p => p
                .Add(c => c.Title, "No Refresh"));

            Assert.Contains("No Refresh", header.Markup, System.StringComparison.Ordinal);
            Assert.DoesNotContain("btn-refresh", header.Markup, System.StringComparison.Ordinal);
        }
    }
}
