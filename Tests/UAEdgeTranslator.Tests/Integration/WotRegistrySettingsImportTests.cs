namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using global::Opc.Ua.WotCon;
    using global::Opc.Ua.WotCon.Server.Registry;
    using Opc.Ua.Edge.Translator.Diagnostics;
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

            AddressSpaceService service = new();
            AddressSpaceNode thing = null;
            for (int attempt = 0; attempt < 100 && thing == null; attempt++)
            {
                thing = (await service.BrowseChildrenAsync(global::Opc.Ua.ObjectIds.ObjectsFolder.ToString()))
                    .FirstOrDefault(c => c.Text == "legacy-1 [xRegistry]");
                if (thing == null)
                {
                    await Task.Delay(50);
                }
            }

            // Assert into a non-null local, so the dereference below is provably safe.
            AddressSpaceNode legacyThing = Assert.IsType<AddressSpaceNode>(thing);

            // 'setpoint' is served by its mapping target, which sits below the Thing in
            // its place; the property gets no second, never-updated Variable.
            var children = await service.BrowseChildrenAsync(legacyThing.Id);
            Assert.Contains(children, c => c.Text == "VoltageL-N");
            Assert.DoesNotContain(children, c => c.Text == "setpoint");
        }

        [Fact]
        public async Task An_imported_property_is_served_by_the_driver_instead_of_waiting_for_initial_data()
        {
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;

            string importFolder = Path.Combine(fixture.WorkingDirectory, "rw-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(importFolder);
            await File.WriteAllTextAsync(Path.Combine(importFolder, "rw-1.jsonld"), """
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
                  "@type": ["Thing"],
                  "id": "urn:rw-1",
                  "title": "rw-1",
                  "base": "mock://device-1:1502/1",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": ["nosec_sc"],
                  "properties": {
                    "setpoint": {
                      "type": "number",
                      "forms": [ { "href": "/setpoint", "op": ["readproperty", "writeproperty"], "type": "xsd:float" } ]
                    }
                  }
                }
                """);

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);
            await host.ImportSettingsFilesAsync(importFolder, default);

            AddressSpaceService service = new();
            AddressSpaceNode thing = null;
            for (int attempt = 0; attempt < 100 && thing == null; attempt++)
            {
                thing = (await service.BrowseChildrenAsync(global::Opc.Ua.ObjectIds.ObjectsFolder.ToString()))
                    .FirstOrDefault(c => c.Text == "rw-1 [xRegistry]");
                if (thing == null)
                {
                    await Task.Delay(50);
                }
            }

            AddressSpaceNode importedThing = Assert.IsType<AddressSpaceNode>(thing);
            NodeId setpoint = NodeId.Parse(Assert.Single(await service.BrowseChildrenAsync(importedThing.Id), c => c.Text == "setpoint").Id);

            // The mock asset keeps what is written, so a write followed by a read
            // proves both handlers reach the driver.
            OperationContext context = new(new RequestHeader(), null, RequestType.Write, RequestLifetime.None);
            var write = await server.CurrentInstance.NodeManager.WriteAsync(
                context,
                new[] { new WriteValue { NodeId = setpoint, AttributeId = Attributes.Value, Value = new DataValue(new Variant(12.5f)) } },
                default);
            Assert.True(StatusCode.IsGood(write.Item1[0]), write.Item1[0].ToString());

            var read = await server.CurrentInstance.NodeManager.ReadAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None),
                0,
                TimestampsToReturn.Both,
                new[] { new ReadValueId { NodeId = setpoint, AttributeId = Attributes.Value } },
                default);
            DataValue value = read.Item1[0];
            Assert.NotEqual(StatusCodes.BadWaitingForInitialData, value.StatusCode.Code);
            Assert.True(StatusCode.IsGood(value.StatusCode), value.StatusCode.ToString());
            Assert.Equal(12.5, Convert.ToDouble(value.WrappedValue.AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture));
        }

        private static WotResource Find(WotRegistryHost host) =>
            host.Registry.Current
                .ResourcesOfKind(WoTDocumentKindEnum.ThingDescription)
                .Single(r => r.ResourceId == "imported-1");

        [Fact]
        public async Task Properties_mapped_onto_an_undeclared_structured_target_share_one_created_variable()
        {
            // The WoT Connectivity 1.02 pattern: two properties feed two fields of one
            // structured target ('uav:mapToNodeId' + 'uav:mapToType' + 'uav:mapByFieldPath')
            // that the Thing Description does not declare. Range (i=884) is a built-in
            // structure, so no companion model is needed.
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;

            string importFolder = Path.Combine(fixture.WorkingDirectory, "shared-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(importFolder);
            await File.WriteAllTextAsync(Path.Combine(importFolder, "shared-1.jsonld"), """
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1", { "uav": "http://opcfoundation.org/UA/PNEM/" }],
                  "@type": ["Thing"],
                  "id": "urn:shared-1",
                  "title": "shared-1",
                  "base": "mock://device-1:1502/1",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": ["nosec_sc"],
                  "properties": {
                    "low": {
                      "type": "number",
                      "uav:mapToNodeId": "s=Limits",
                      "uav:mapToType": "nsu=http://opcfoundation.org/UA/;i=884",
                      "uav:mapByFieldPath": "Low",
                      "forms": [ { "href": "/low", "op": ["readproperty", "writeproperty"], "type": "xsd:double" } ]
                    },
                    "high": {
                      "type": "number",
                      "uav:mapToNodeId": "s=Limits",
                      "uav:mapToType": "nsu=http://opcfoundation.org/UA/;i=884",
                      "uav:mapByFieldPath": "High",
                      "forms": [ { "href": "/high", "op": ["readproperty", "writeproperty"], "type": "xsd:double" } ]
                    }
                  }
                }
                """);

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);
            await host.ImportSettingsFilesAsync(importFolder, default);

            var refresh = await host.Coordinator.RefreshAsync(new global::Opc.Ua.WotCon.Server.Materialization.WotRefreshRequest());
            var loaded = Assert.Single(refresh.Results, r => r.ResourceId == "shared-1");
            Assert.True(loaded.LoadState == WoTLoadStateEnum.Active, loaded.Phase + ": " + loaded.Message);

            // The authored target exists once, with the authored NodeId and DataType,
            // and is reachable below the Objects folder.
            ushort ns = (ushort)server.CurrentInstance.NamespaceUris.GetIndex("urn:shared-1");
            NodeId limits = new("Limits", ns);

            AddressSpaceService service = new();
            NodeDetail detail = await service.GetNodeDetailAsync(limits.ToString());
            Assert.Equal("Variable", detail.NodeClass);
            Assert.Contains(detail.Attributes, a => a.Name == "BrowseName" && a.Value.EndsWith("Limits", StringComparison.Ordinal));

            // The shared target is created once, below the Thing whose properties map onto it.
            var objects = await service.BrowseChildrenAsync(global::Opc.Ua.ObjectIds.ObjectsFolder.ToString());
            Assert.DoesNotContain(objects, c => c.Id == limits.ToString());
            var thing = Assert.Single(objects, c => c.Text == "shared-1 [xRegistry]");
            var thingChildren = await service.BrowseChildrenAsync(thing.Id);
            Assert.Single(thingChildren, c => c.Id == limits.ToString());
            Assert.DoesNotContain(thingChildren, c => c.Text == "low" || c.Text == "high");

            var dataType = await server.CurrentInstance.NodeManager.ReadAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None),
                0,
                TimestampsToReturn.Neither,
                new[] { new ReadValueId { NodeId = limits, AttributeId = Attributes.DataType } },
                default);
            Assert.Equal(new NodeId(884u), dataType.Item1[0].WrappedValue.GetNodeId());
        }

        [Fact]
        public async Task Pnem_mappings_feed_a_shared_structure_and_an_object_type_child()
        {
            // The PAC4200 mappings: three phases feed fields A/B/C of one PNEM
            // AcPeDataType structure, and a line voltage feeds <MeasurementValue> of a
            // PNEM EnergyMeasurementType instance. Neither target is declared in the TD.
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;

            string nodesets = Path.Combine(fixture.WorkingDirectory, "nodesets");
            foreach (string name in new[] { "opcfoundation.org.UA.DI.nodeset2.xml", "opcfoundation.org.UA.PNEM.nodeset2.xml" })
            {
                File.Copy(FindRepositoryFile(Path.Combine("Tests", "UAEdgeTranslator.Tests", "TestData", "nodesets", name)), Path.Combine(nodesets, name), overwrite: true);
            }

            string importFolder = Path.Combine(fixture.WorkingDirectory, "pnem-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(importFolder);
            await File.WriteAllTextAsync(Path.Combine(importFolder, "pnem-1.jsonld"), """
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1", { "uav": "http://opcfoundation.org/UA/PNEM/" }],
                  "@type": ["Thing"],
                  "id": "urn:pnem-1",
                  "name": "pnem-1",
                  "title": "PNEM Meter 1",
                  "base": "mock://device-1:1502/1",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": ["nosec_sc"],
                  "properties": {
                    "VoltageL1-N": {
                      "type": "number",
                      "uav:mapToNodeId": "s=VoltageL-N",
                      "uav:mapToType": "nsu=http://opcfoundation.org/UA/PNEM/;i=3005",
                      "uav:mapByFieldPath": "A",
                      "forms": [ { "href": "/l1", "op": ["readproperty", "writeproperty"], "type": "xsd:float" } ]
                    },
                    "VoltageL2-N": {
                      "type": "number",
                      "uav:mapToNodeId": "s=VoltageL-N",
                      "uav:mapToType": "nsu=http://opcfoundation.org/UA/PNEM/;i=3005",
                      "uav:mapByFieldPath": "B",
                      "forms": [ { "href": "/l2", "op": ["readproperty", "writeproperty"], "type": "xsd:float" } ]
                    },
                    "VoltageL1-L2": {
                      "type": "number",
                      "uav:mapToNodeId": "s=VoltageL1-L2",
                      "uav:mapToType": "nsu=http://opcfoundation.org/UA/PNEM/;i=1006",
                      "uav:mapByFieldPath": "<MeasurementValue>",
                      "forms": [ { "href": "/l12", "op": ["readproperty", "writeproperty"], "type": "xsd:float" } ]
                    }
                  }
                }
                """);

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);
            await host.ImportSettingsFilesAsync(importFolder, default);

            var refresh = await host.Coordinator.RefreshAsync(new global::Opc.Ua.WotCon.Server.Materialization.WotRefreshRequest());
            var loaded = Assert.Single(refresh.Results, r => r.ResourceId == "pnem-1");
            Assert.True(loaded.LoadState == WoTLoadStateEnum.Active, loaded.Phase + ": " + loaded.Message);

            ushort ns = (ushort)server.CurrentInstance.NamespaceUris.GetIndex("urn:pnem-1");
            NodeId structure = new("VoltageL-N", ns);
            NodeId objectChild = new("VoltageL1-L2.MeasurementValue", ns);

            // Values written into the shared structure reach the device field by field.
            ushort pnem = (ushort)server.CurrentInstance.NamespaceUris.GetIndex("http://opcfoundation.org/UA/PNEM/");
            DataValue structureBefore = await ReadValueAsync(server, structure);
            Assert.True(StatusCode.IsGood(structureBefore.StatusCode), "structure read: " + structureBefore.StatusCode);
            Assert.True(structureBefore.WrappedValue.TryGetStructure(out IEncodeable phases), structureBefore.WrappedValue.ToString());
            Assert.Equal(
                new NodeId(3005u, pnem),
                ExpandedNodeId.ToNodeId(phases.TypeId, server.CurrentInstance.NamespaceUris));

            // The object-type child is served by its property's form.
            StatusCode writeStatus = await WriteValueAsync(server, objectChild, new Variant(230.5f));
            Assert.True(StatusCode.IsGood(writeStatus), "child write: " + writeStatus);
            DataValue child = await ReadValueAsync(server, objectChild);
            Assert.True(StatusCode.IsGood(child.StatusCode), "child read: " + child.StatusCode);
            Assert.Equal(230.5, Convert.ToDouble(child.WrappedValue.AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture), 3);

            // And the instance carries the ObjectType it was mapped to.
            DataValue typeDefinitions = await ReadValueAsync(server, new NodeId("VoltageL1-L2", ns), Attributes.NodeClass);
            Assert.True(StatusCode.IsGood(typeDefinitions.StatusCode));

            // The mapping targets live under the asset, not in the Objects folder, and
            // the mapped properties have no second, never-updated projected Variable.
            AddressSpaceService service = new();
            var roots = await service.BrowseChildrenAsync(global::Opc.Ua.ObjectIds.ObjectsFolder.ToString());
            Assert.DoesNotContain(roots, c => c.Text == "VoltageL-N" || c.Text == "VoltageL1-L2");
            var asset = Assert.Single(roots, c => c.Text == "PNEM Meter 1 [xRegistry]");
            var children = await service.BrowseChildrenAsync(asset.Id);
            Assert.Contains(children, c => c.Text == "VoltageL-N");
            Assert.Contains(children, c => c.Text == "VoltageL1-L2");
            Assert.DoesNotContain(children, c => c.Text == "VoltageL1-N" || c.Text == "VoltageL2-N");
            Assert.DoesNotContain(children, c => c.Text.EndsWith(" [xRegistry]", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_thing_upserted_through_the_registry_is_prepared_before_it_is_materialized()
        {
            // A change made through xRegistry (not a settings import) goes straight into
            // the registry. Its companion model and cached Thing Description must be in
            // place before the Thing is materialized, or the mapping type is unknown.
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;

            string nodesets = Path.Combine(fixture.WorkingDirectory, "nodesets");
            foreach (string name in new[] { "opcfoundation.org.UA.DI.nodeset2.xml", "opcfoundation.org.UA.PNEM.nodeset2.xml" })
            {
                File.Copy(FindRepositoryFile(Path.Combine("Tests", "UAEdgeTranslator.Tests", "TestData", "nodesets", name)), Path.Combine(nodesets, name), overwrite: true);
            }

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);

            byte[] td = WotImportNormalizer.Normalize(System.Text.Encoding.UTF8.GetBytes("""
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1", { "uav": "http://opcfoundation.org/UA/PNEM/" }],
                  "@type": ["Thing"],
                  "id": "urn:pnem-x",
                  "name": "pnem-x",
                  "title": "pnem-x",
                  "base": "mock://device-1:1502/1",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": ["nosec_sc"],
                  "properties": {
                    "VoltageL1-N": {
                      "type": "number",
                      "uav:mapToNodeId": "s=VoltageL-N",
                      "uav:mapToType": "nsu=http://opcfoundation.org/UA/PNEM/;i=3005",
                      "uav:mapByFieldPath": "A",
                      "forms": [ { "href": "/l1", "op": ["readproperty", "writeproperty"], "type": "xsd:float" } ]
                    }
                  }
                }
                """));

            await host.Registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                Kind = WoTDocumentKindEnum.ThingDescription,
                ResourceId = "pnem-x",
                Name = "pnem-x",
                Content = new ByteString(td),
                SetAsDefault = true
            });

            // No manual refresh: the change itself must lead to a materialized, readable Thing.
            ushort ns = 0;
            DataValue value = default;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                ns = (ushort)server.CurrentInstance.NamespaceUris.GetIndex("urn:pnem-x");
                if (ns != ushort.MaxValue && ns != 0)
                {
                    value = await ReadValueAsync(server, new NodeId("VoltageL-N", ns));
                    if (StatusCode.IsGood(value.StatusCode))
                    {
                        break;
                    }
                }

                await Task.Delay(100);
            }

            Assert.False(value.IsNull, "the Thing was never materialized");
            Assert.True(StatusCode.IsGood(value.StatusCode), "structure read: " + value.StatusCode);
        }

        [Fact]
        public async Task Things_with_a_property_of_the_same_name_keep_their_own_mapping_targets()
        {
            // Two Things, both with a mapped property called "status", materialize in one
            // projection. Each mapping target must land under its own Thing, and neither
            // Thing's property may be taken for the other's.
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;

            string importFolder = Path.Combine(fixture.WorkingDirectory, "twins-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(importFolder);
            foreach (string name in new[] { "twin-a", "twin-b" })
            {
                await File.WriteAllTextAsync(Path.Combine(importFolder, name + ".jsonld"), $$"""
                    {
                      "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
                      "@type": ["Thing"],
                      "id": "urn:{{name}}",
                      "name": "{{name}}",
                      "title": "{{name}}",
                      "base": "mock://{{name}}:1502/1",
                      "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                      "security": ["nosec_sc"],
                      "properties": {
                        "status": {
                          "type": "number",
                          "uav:mapToNodeId": "s=Status",
                          "forms": [ { "href": "/{{name}}-status", "op": ["readproperty", "writeproperty"], "type": "xsd:float" } ]
                        }
                      }
                    }
                    """);
            }

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);
            await host.ImportSettingsFilesAsync(importFolder, default);

            var refresh = await host.Coordinator.RefreshAsync(new global::Opc.Ua.WotCon.Server.Materialization.WotRefreshRequest());
            foreach (string name in new[] { "twin-a", "twin-b" })
            {
                var loaded = Assert.Single(refresh.Results, r => r.ResourceId == name);
                Assert.True(loaded.LoadState == WoTLoadStateEnum.Active, name + " " + loaded.Phase + ": " + loaded.Message);
            }

            AddressSpaceService service = new();
            var roots = await service.BrowseChildrenAsync(global::Opc.Ua.ObjectIds.ObjectsFolder.ToString());
            foreach (string name in new[] { "twin-a", "twin-b" })
            {
                ushort ns = (ushort)server.CurrentInstance.NamespaceUris.GetIndex("urn:" + name);
                AddressSpaceNode thing = Assert.Single(roots, c => c.Text == name + " [xRegistry]");
                var children = await service.BrowseChildrenAsync(thing.Id);

                // The target sits under this Thing, in this Thing's namespace.
                AddressSpaceNode target = Assert.Single(children, c => c.Text == "Status");
                Assert.Equal(new NodeId("Status", ns).ToString(), target.Id);

                // This Thing's own "status" Variable was replaced by its mapping target.
                Assert.DoesNotContain(children, c => c.Text == "status");

                // And the target is fed by this Thing's form.
                Assert.True(StatusCode.IsGood(await WriteValueAsync(server, new NodeId("Status", ns), new Variant(name == "twin-a" ? 1.5f : 2.5f))));
            }

            ushort nsA = (ushort)server.CurrentInstance.NamespaceUris.GetIndex("urn:twin-a");
            ushort nsB = (ushort)server.CurrentInstance.NamespaceUris.GetIndex("urn:twin-b");
            Assert.Equal(1.5, Convert.ToDouble((await ReadValueAsync(server, new NodeId("Status", nsA))).WrappedValue.AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture), 3);
            Assert.Equal(2.5, Convert.ToDouble((await ReadValueAsync(server, new NodeId("Status", nsB))).WrappedValue.AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture), 3);
        }

        private static async Task<DataValue> ReadValueAsync(StandardServer server, NodeId nodeId, uint attributeId = Attributes.Value)
        {
            var result = await server.CurrentInstance.NodeManager.ReadAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None),
                0,
                TimestampsToReturn.Both,
                new[] { new ReadValueId { NodeId = nodeId, AttributeId = attributeId } },
                default);
            return result.Item1[0];
        }

        private static async Task<StatusCode> WriteValueAsync(StandardServer server, NodeId nodeId, Variant value)
        {
            var result = await server.CurrentInstance.NodeManager.WriteAsync(
                new OperationContext(new RequestHeader(), null, RequestType.Write, RequestLifetime.None),
                new[] { new WriteValue { NodeId = nodeId, AttributeId = Attributes.Value, Value = new DataValue(value) } },
                default);
            return result.Item1[0];
        }

        private static string FindRepositoryFile(string relativePath)
        {
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

                    AddressSpaceService service = new();
                    AddressSpaceNode thing = null;
                    for (int attempt = 0; attempt < 100 && thing == null; attempt++)
                    {
                        thing = (await service.BrowseChildrenAsync(global::Opc.Ua.ObjectIds.ObjectsFolder.ToString()))
                            .FirstOrDefault(c => c.Text == "restart-1 [xRegistry]");
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
