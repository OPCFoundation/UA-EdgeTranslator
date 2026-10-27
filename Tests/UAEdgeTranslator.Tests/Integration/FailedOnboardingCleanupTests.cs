namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using System;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Regression coverage for an asset whose onboarding fails part way.
    /// <para>
    /// The asset node is created before the Thing Description is onboarded into it.
    /// When onboarding then failed, the node used to stay behind: it never reached
    /// the connected-asset list, so the dashboard's Assets page did not show it, yet
    /// every retry was rejected with "An asset named '...' already exists".
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class FailedOnboardingCleanupTests : IAsyncLifetime
    {
        private OpcUaServerFixture _fixture;

        public Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();

            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            if (_fixture != null)
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
                _fixture = null;
            }
        }

        // An event affordance forces the connection test, which fails for a scheme
        // no protocol driver serves - after the asset node has been created.
        private static string ThingDescription(string scheme) => $$"""
            {
              "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
              "id": "urn:half-onboarded",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": [ "nosec_sc" ],
              "@type": [ "Thing" ],
              "name": "half-onboarded",
              "title": "Half onboarded",
              "base": "{{scheme}}://127.0.0.1:1234/1",
              "events": {
                "temperature": {
                  "data": { "type": "number" },
                  "forms": [
                    {
                      "href": "127.0.0.1/1?quantity=2",
                      "op": [ "subscribeevent", "unsubscribeevent" ],
                      "type": "xsd:short"
                    }
                  ]
                }
              }
            }
            """;

        [Fact]
        public async Task A_failed_import_can_be_retried_instead_of_reporting_the_asset_as_existing()
        {
            Exception first = await Assert.ThrowsAnyAsync<Exception>(
                () => UANodeManager.Instance.ImportWoTFileAsync("half-onboarded.jsonld", ThingDescription("nosuchscheme")));
            Assert.Contains("No driver installed", first.Message, StringComparison.Ordinal);

            // The retry must fail for the same reason, not because of a leftover node.
            Exception retry = await Assert.ThrowsAnyAsync<Exception>(
                () => UANodeManager.Instance.ImportWoTFileAsync("half-onboarded.jsonld", ThingDescription("nosuchscheme")));
            Assert.DoesNotContain("already exists", retry.Message, StringComparison.Ordinal);
            Assert.Contains("No driver installed", retry.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_corrected_thing_description_imports_under_the_name_that_failed_before()
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => UANodeManager.Instance.ImportWoTFileAsync("half-onboarded.jsonld", ThingDescription("nosuchscheme")));

            string settingsFile = await UANodeManager.Instance.ImportWoTFileAsync("half-onboarded.jsonld", ThingDescription("mock"));

            Assert.Equal("half-onboarded.jsonld", settingsFile);
            Assert.Contains(UANodeManager.Instance.GetConnectedAssets(), a => a.Name == "half-onboarded");
        }
    }
}
