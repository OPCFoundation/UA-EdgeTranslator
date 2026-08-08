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

            Assert.Contains("Connected Devices", page.Markup, System.StringComparison.Ordinal);
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
