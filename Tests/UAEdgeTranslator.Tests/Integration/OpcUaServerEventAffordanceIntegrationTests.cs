namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Onboarding coverage for Thing Descriptions that describe their values as
    /// <em>event</em> affordances rather than properties.
    /// <para>
    /// Push-oriented bindings require this shape. The W3C WoT LoRaWAN binding
    /// states that because a LoRaWAN device transmits on its own schedule, "a
    /// consumer does not poll a sensor on demand; instead it subscribes to
    /// uplinks", and so the binding "models uplink values as WoT events, not
    /// readable properties". A conformant LoRaWAN Thing Description therefore
    /// contains no <c>properties</c> at all, and the translator has to create
    /// its OPC UA variables from <c>events</c>.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class OpcUaServerEventAffordanceIntegrationTests : IAsyncLifetime
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

        [Fact]
        public async Task An_event_only_thing_description_creates_variables()
        {
            // The shape a conformant LoRaWAN TD actually has: no "properties".
            const string td = """
            {
              "@context": [
                "https://www.w3.org/2022/wot/td/v1.1",
                { "lorav": "https://www.w3.org/2026/wot/lorawan#" }
              ],
              "id": "urn:eventonly",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": [ "nosec_sc" ],
              "@type": [ "Thing" ],
              "name": "eventonly",
              "title": "Event Only Asset",
              "base": "mock://127.0.0.1:1234/1",
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

            await UANodeManager.Instance.ImportWoTFileAsync("eventonly.jsonld", td).ConfigureAwait(false);

            // The event affordance must have produced a browsable variable.
            AddressSpaceService addressSpace = new();

            NodeId assetId = FindAssetNode("eventonly");
            Assert.False(NodeId.IsNull(assetId), "The asset node was not created.");

            var children = await addressSpace.BrowseChildrenAsync(assetId.ToString()).ConfigureAwait(false);

            Assert.Contains(children, c => c.Text == "temperature");
        }

        [Fact]
        public async Task Event_data_schema_supplies_the_opc_ua_node_id_mapping()
        {
            // uav:mapToNodeId sits on the event's data schema, mirroring where a
            // property affordance carries it.
            const string td = """
            {
              "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
              "id": "urn:eventmapped",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": [ "nosec_sc" ],
              "@type": [ "Thing" ],
              "name": "eventmapped",
              "title": "Event Mapped Asset",
              "base": "mock://127.0.0.1:1234/1",
              "events": {
                "humidity": {
                  "data": {
                    "type": "number",
                    "uav:mapToNodeId": "s=MappedHumidity"
                  },
                  "forms": [
                    {
                      "href": "127.0.0.1/2?quantity=2",
                      "op": [ "subscribeevent" ],
                      "type": "xsd:short"
                    }
                  ]
                }
              }
            }
            """;

            await UANodeManager.Instance.ImportWoTFileAsync("eventmapped.jsonld", td).ConfigureAwait(false);

            AddressSpaceService addressSpace = new();
            NodeId assetId = FindAssetNode("eventmapped");

            var children = await addressSpace.BrowseChildrenAsync(assetId.ToString()).ConfigureAwait(false);

            // The variable is named from the mapping hint, not the event key.
            Assert.Contains(children, c => c.Text == "MappedHumidity");
        }

        [Fact]
        public async Task Properties_and_events_can_coexist_in_one_thing_description()
        {
            // Nothing in the WoT specification forbids both, and other bindings
            // still use properties, so adding event support must not displace
            // the property path.
            const string td = """
            {
              "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
              "id": "urn:mixed",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": [ "nosec_sc" ],
              "@type": [ "Thing" ],
              "name": "mixed",
              "title": "Mixed Asset",
              "base": "mock://127.0.0.1:1234/1",
              "properties": {
                "polled": {
                  "type": "number",
                  "readOnly": true,
                  "forms": [ { "href": "127.0.0.1/1?quantity=2", "type": "xsd:short" } ]
                }
              },
              "events": {
                "pushed": {
                  "data": { "type": "number" },
                  "forms": [ { "href": "127.0.0.1/2?quantity=2", "op": [ "subscribeevent" ], "type": "xsd:short" } ]
                }
              }
            }
            """;

            await UANodeManager.Instance.ImportWoTFileAsync("mixed.jsonld", td).ConfigureAwait(false);

            AddressSpaceService addressSpace = new();
            NodeId assetId = FindAssetNode("mixed");

            var children = await addressSpace.BrowseChildrenAsync(assetId.ToString()).ConfigureAwait(false);

            Assert.Contains(children, c => c.Text == "polled");
            Assert.Contains(children, c => c.Text == "pushed");
        }

        [Fact]
        public async Task An_event_without_forms_is_skipped_rather_than_throwing()
        {
            // A subscription-less event carries no transport information, so it
            // cannot become a variable; onboarding must still succeed.
            const string td = """
            {
              "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
              "id": "urn:noforms",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": [ "nosec_sc" ],
              "@type": [ "Thing" ],
              "name": "noforms",
              "title": "No Forms Asset",
              "base": "mock://127.0.0.1:1234/1",
              "events": {
                "ghost": { "data": { "type": "number" } },
                "real": {
                  "data": { "type": "number" },
                  "forms": [ { "href": "127.0.0.1/1?quantity=2", "op": [ "subscribeevent" ], "type": "xsd:short" } ]
                }
              }
            }
            """;

            await UANodeManager.Instance.ImportWoTFileAsync("noforms.jsonld", td).ConfigureAwait(false);

            AddressSpaceService addressSpace = new();
            NodeId assetId = FindAssetNode("noforms");

            var children = await addressSpace.BrowseChildrenAsync(assetId.ToString()).ConfigureAwait(false);

            // The usable event still produced a variable.
            Assert.Contains(children, c => c.Text == "real");
            Assert.DoesNotContain(children, c => c.Text == "ghost");
        }

        /// <summary>
        /// Resolves an onboarded asset's node by display name under the
        /// WoTAssetManagement folder.
        /// </summary>
        private static NodeId FindAssetNode(string assetName)
        {
            UANodeManager manager = UANodeManager.Instance;
            ushort wotConNamespace = (ushort)manager.Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/WoT-Con/");

            NodeState assetManagement = manager.Find(new NodeId(31, wotConNamespace));

            Assert.NotNull(assetManagement);

            System.Collections.Generic.List<IReference> references = [];
            assetManagement.GetReferences(manager.SystemContext, references, ReferenceTypeIds.Organizes, false);

            foreach (IReference reference in references)
            {
                NodeState target = manager.Find(ExpandedNodeId.ToNodeId(reference.TargetId, manager.Server.NamespaceUris));

                if (target?.DisplayName?.Text == assetName)
                {
                    return target.NodeId;
                }
            }

            return NodeId.Null;
        }
    }
}
