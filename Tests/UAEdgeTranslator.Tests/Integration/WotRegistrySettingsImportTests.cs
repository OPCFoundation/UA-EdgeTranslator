namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using global::Opc.Ua.WotCon;
    using global::Opc.Ua.WotCon.Server.Registry;
    using Opc.Ua.Edge.Translator.WotCon;
    using Opc.Ua.Server;
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Covers the startup import of the WoT files in <c>settings</c> into the
    /// WoT Connectivity 1.1 registry.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class WotRegistrySettingsImportTests
    {
        private const string Td = """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
              "id": "urn:imported-1",
              "title": "imported-1",
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
        public async Task WoT_files_in_settings_are_imported_once_and_updated_when_changed()
        {
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;

            string settings = Path.Combine(Directory.GetCurrentDirectory(), "settings");
            string importFolder = Path.Combine(settings, "registry-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(importFolder);
            string file = Path.Combine(importFolder, "imported-1.jsonld");
            await File.WriteAllTextAsync(file, Td);

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);

            await host.ImportSettingsFilesAsync(importFolder, default);
            WotResource imported = Assert.Single(
                host.Registry.Current.ResourcesOfKind(WoTDocumentKindEnum.ThingDescription),
                r => r.ResourceId == "imported-1");
            string firstVersion = imported.DefaultVersion.VersionId;

            // A restart with an unchanged file must not create a new version.
            await host.ImportSettingsFilesAsync(importFolder, default);
            Assert.Equal(firstVersion, Find(host).DefaultVersion.VersionId);

            // A changed file becomes the new default version.
            await File.WriteAllTextAsync(file, Td.Replace("\"readproperty\"", "\"readproperty\", \"writeproperty\"", StringComparison.Ordinal));
            await host.ImportSettingsFilesAsync(importFolder, default);
            Assert.NotEqual(firstVersion, Find(host).DefaultVersion.VersionId);
        }

        [Fact]
        public async Task A_legacy_thing_description_is_materialized_below_the_objects_folder()
        {
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;

            // Same shape as the PAC4200 sample: plain "Thing", uav bound to a
            // companion namespace, no anchor.
            string importFolder = Path.Combine(fixture.WorkingDirectory, "legacy-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(importFolder);
            await File.WriteAllTextAsync(Path.Combine(importFolder, "legacy-1.jsonld"), """
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1", { "uav": "http://opcfoundation.org/UA/PNEM/" }],
                  "@type": ["Thing"],
                  "id": "urn:legacy-1",
                  "title": "legacy-1",
                  "base": "mock://device-1:1502/1",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": ["nosec_sc"],
                  "properties": {
                    "setpoint": {
                      "type": "number",
                      "uav:mapToNodeId": "s=VoltageL-N",
                      "uav:mapToType": "nsu=http://opcfoundation.org/UA/PNEM/;i=3005",
                      "uav:mapByFieldPath": "A",
                      "forms": [ { "href": "/setpoint", "op": ["readproperty"], "type": "xsd:float" } ]
                    }
                  }
                }
                """);

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);
            await host.ImportSettingsFilesAsync(importFolder, default);

            Diagnostics.AddressSpaceService service = new();
            Diagnostics.AddressSpaceNode thing = null;
            for (int attempt = 0; attempt < 100 && thing == null; attempt++)
            {
                thing = (await service.BrowseChildrenAsync(global::Opc.Ua.ObjectIds.ObjectsFolder.ToString()))
                    .FirstOrDefault(c => c.Text == "legacy-1");
                if (thing == null)
                {
                    await Task.Delay(50);
                }
            }

            Assert.NotNull(thing);
            Assert.Contains(await service.BrowseChildrenAsync(thing.Id), c => c.Text == "setpoint");
        }

        private static WotResource Find(WotRegistryHost host) =>
            host.Registry.Current
                .ResourcesOfKind(WoTDocumentKindEnum.ThingDescription)
                .Single(r => r.ResourceId == "imported-1");

        [Fact]
        public async Task A_restart_with_a_persisted_thing_starts_and_rematerializes_it()
        {
            // Regression: with a Thing already persisted, the registry node manager
            // materialized it while the server's node-manager lifecycle was still busy
            // registering that node manager, and startup hung forever.
            string storage = Path.Combine(Path.GetTempPath(), "wot-registry-restart-" + Guid.NewGuid().ToString("N"));
            string importFolder = Path.Combine(Path.GetTempPath(), "wot-import-restart-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(importFolder);
            await File.WriteAllTextAsync(Path.Combine(importFolder, "restart-1.jsonld"), Td.Replace("imported-1", "restart-1", StringComparison.Ordinal));

            try
            {
                for (int run = 0; run < 2; run++)
                {
                    await using OpcUaServerFixture fixture = new();
                    StandardServer server = (StandardServer)fixture.App.Server;

                    Task<WotRegistryHost> start = WotRegistryHost.StartAsync(server, Program.Drivers, storage);
                    Assert.Same(start, await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(30))));
                    await using WotRegistryHost host = await start;

                    if (run == 0)
                    {
                        await host.ImportSettingsFilesAsync(importFolder, default);
                    }

                    Diagnostics.AddressSpaceService service = new();
                    Diagnostics.AddressSpaceNode thing = null;
                    for (int attempt = 0; attempt < 100 && thing == null; attempt++)
                    {
                        thing = (await service.BrowseChildrenAsync(global::Opc.Ua.ObjectIds.ObjectsFolder.ToString()))
                            .FirstOrDefault(c => c.Text == "restart-1");
                        if (thing == null)
                        {
                            await Task.Delay(50);
                        }
                    }

                    Assert.NotNull(thing);
                }
            }
            finally
            {
                try { Directory.Delete(storage, true); } catch (IOException) { }
                try { Directory.Delete(importFolder, true); } catch (IOException) { }
            }
        }
    }
}
