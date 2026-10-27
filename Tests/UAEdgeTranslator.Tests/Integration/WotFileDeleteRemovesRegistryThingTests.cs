namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using global::Opc.Ua.WotCon;
    using Opc.Ua.Edge.Translator.WotCon;
    using Opc.Ua.Server;
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// The WoT Connectivity registry keeps its own copy of every WoT file imported from
    /// <c>settings</c> and rematerializes it on every startup. Deleting the file used to
    /// leave that copy behind, so the Thing - and the connection to its asset - came
    /// back on each restart, long after its Thing Description was gone.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class WotFileDeleteRemovesRegistryThingTests
    {
        private const string Td = """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
              "id": "urn:doomed-1",
              "title": "doomed-1",
              "base": "mock://device-1:1502/1",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": ["nosec_sc"],
              "properties": {
                "setpoint": {
                  "type": "number",
                  "forms": [ { "href": "/setpoint", "op": ["readproperty"], "type": "xsd:float" } ]
                }
              }
            }
            """;

        [Fact]
        public async Task Deleting_a_wot_file_also_removes_its_registry_thing()
        {
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;

            await File.WriteAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "settings", "doomed-1.jsonld"), Td);

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);

            // Startup imported the file into the registry.
            Assert.Contains(
                host.Registry.Current.ResourcesOfKind(WoTDocumentKindEnum.ThingDescription),
                r => string.Equals(r.ResourceId, "doomed-1", StringComparison.OrdinalIgnoreCase));

            Assert.True(await UANodeManager.Instance.UnloadWoTFileAsync("doomed-1.jsonld"));

            Assert.DoesNotContain(
                host.Registry.Current.ResourcesOfKind(WoTDocumentKindEnum.ThingDescription),
                r => string.Equals(r.ResourceId, "doomed-1", StringComparison.OrdinalIgnoreCase));
        }
    }
}
