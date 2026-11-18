namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.WotCon;
    using Opc.Ua.Server;
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// The server-side half of the Projections page: browsing the Types folder, reading
    /// what a dropped type node maps to, and loading companion models into the running
    /// server through the WoT registry host.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class ProjectionMappingTests
    {
        private const string Pnem = "http://opcfoundation.org/UA/PNEM/";
        private const string Di = "http://opcfoundation.org/UA/DI/";

        [Fact]
        public async Task The_types_tree_is_rooted_at_the_types_folder()
        {
            await using OpcUaServerFixture fixture = new();
            AddressSpaceService service = new();

            AddressSpaceNode root = Assert.Single(await service.GetTypesRootNodesAsync());

            Assert.Equal(ObjectIds.TypesFolder.ToString(), root.Id);
            Assert.Equal("Types", root.Text);
        }

        [Fact]
        public async Task A_core_structure_maps_to_its_fields_and_a_simple_type_to_none()
        {
            await using OpcUaServerFixture fixture = new();
            AddressSpaceService service = new();

            NodeMappingInfo range = await service.GetNodeMappingAsync(DataTypeIds.Range.ToString());
            Assert.Null(range.Error);
            Assert.Equal("i=884", range.TypeNodeId);
            Assert.Equal(string.Empty, range.NamespaceUri);
            Assert.True(range.IsComplex);
            Assert.Equal(["Low", "High"], range.Fields.Select(f => f.Path));

            NodeMappingInfo dbl = await service.GetNodeMappingAsync(DataTypeIds.Double.ToString());
            Assert.Null(dbl.Error);
            Assert.Equal("i=11", dbl.TypeNodeId);
            Assert.False(dbl.IsComplex);
            Assert.False(dbl.IsObjectType);
        }

        [Fact]
        public async Task A_variable_maps_to_its_data_type()
        {
            await using OpcUaServerFixture fixture = new();
            AddressSpaceService service = new();

            NodeMappingInfo state = await service.GetNodeMappingAsync(VariableIds.Server_ServerStatus_State.ToString());

            Assert.Null(state.Error);
            Assert.Equal("i=852", state.TypeNodeId);
        }

        [Fact]
        public async Task Nodes_that_cannot_be_mapped_report_an_error()
        {
            await using OpcUaServerFixture fixture = new();
            AddressSpaceService service = new();

            Assert.NotNull((await service.GetNodeMappingAsync(ObjectIds.ObjectsFolder.ToString())).Error);
            Assert.NotNull((await service.GetNodeMappingAsync("ns=0;i=999999")).Error);
            Assert.NotNull((await service.GetNodeMappingAsync("not a node id")).Error);
        }

        [Fact]
        public async Task A_companion_model_from_the_nodesets_folder_is_loaded_and_its_types_can_be_mapped()
        {
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;
            CopyTestNodeSets(fixture, "opcfoundation.org.UA.DI.nodeset2.xml", "opcfoundation.org.UA.PNEM.nodeset2.xml");

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);

            Assert.Contains(Pnem, await host.GetAvailableCompanionModelsAsync());
            Assert.Contains(Di, await host.GetAvailableCompanionModelsAsync());
            Assert.DoesNotContain(global::Opc.Ua.Namespaces.OpcUa, await host.GetAvailableCompanionModelsAsync());

            Assert.True(await host.AddCompanionModelAsync(Pnem));
            Assert.True(server.CurrentInstance.NamespaceUris.GetIndex(Di) >= 0, "the required DI model was not loaded");

            // Loading it again is a no-op.
            Assert.True(await host.AddCompanionModelAsync(Pnem));

            ushort ns = (ushort)server.CurrentInstance.NamespaceUris.GetIndex(Pnem);
            AddressSpaceService service = new();

            NodeMappingInfo dataType = await service.GetNodeMappingAsync(new NodeId(3005u, ns).ToString());
            Assert.Null(dataType.Error);
            Assert.Equal("nsu=" + Pnem + ";i=3005", dataType.TypeNodeId);
            Assert.Equal(Pnem, dataType.NamespaceUri);
            Assert.Equal(["A", "B", "C"], dataType.Fields.Select(f => f.Path));

            NodeMappingInfo objectType = await service.GetNodeMappingAsync(new NodeId(1006u, ns).ToString());
            Assert.Null(objectType.Error);
            Assert.True(objectType.IsObjectType);
            Assert.Equal("nsu=" + Pnem + ";i=1006", objectType.TypeNodeId);
            Assert.Contains(objectType.Fields, f => f.Path == "<MeasurementValue>");
        }

        [Fact]
        public async Task An_unknown_companion_model_is_not_loaded()
        {
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);

            Assert.False(await host.AddCompanionModelAsync("http://example.com/UA/Unknown/"));
            Assert.False(await host.AddCompanionModelAsync(" "));
        }

        [Fact]
        public async Task An_uploaded_nodeset_is_stored_in_the_nodesets_folder_and_loaded()
        {
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);

            byte[] xml = await File.ReadAllBytesAsync(TestNodeSet("opcfoundation.org.UA.DI.nodeset2.xml"));

            Assert.Equal([Di], await host.AddCompanionModelAsync(xml));
            Assert.True(server.CurrentInstance.NamespaceUris.GetIndex(Di) >= 0);
            Assert.True(File.Exists(Path.Combine(fixture.WorkingDirectory, "nodesets", "opcfoundation.org.UA.DI.nodeset2.xml")));
            Assert.Contains(Di, Opc.Ua.Edge.Translator.UACloudLibraryClient.GetLocalNamespaceUris());

            await Assert.ThrowsAnyAsync<Exception>(() => host.AddCompanionModelAsync(System.Text.Encoding.UTF8.GetBytes("<NotANodeSet/>")));
        }

        [Fact]
        public async Task Without_the_registry_a_companion_model_is_loaded_into_the_node_manager()
        {
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;
            UANodeManager manager = UANodeManager.Instance;
            CopyTestNodeSets(fixture, "opcfoundation.org.UA.DI.nodeset2.xml", "opcfoundation.org.UA.PNEM.nodeset2.xml");

            Assert.Contains(Pnem, await manager.GetAvailableCompanionModelsAsync());
            Assert.DoesNotContain(global::Opc.Ua.Namespaces.OpcUa, await manager.GetAvailableCompanionModelsAsync());

            Assert.True(await manager.AddCompanionModelAsync(Pnem));
            Assert.Contains(Di, manager.NamespaceUris);

            // Loading it again is a no-op.
            int namespaceCount = server.CurrentInstance.NamespaceUris.Count;
            Assert.True(await manager.AddCompanionModelAsync(" " + Pnem + " "));
            Assert.Equal(namespaceCount, server.CurrentInstance.NamespaceUris.Count);

            ushort ns = (ushort)server.CurrentInstance.NamespaceUris.GetIndex(Pnem);
            NodeMappingInfo dataType = await new AddressSpaceService().GetNodeMappingAsync(new NodeId(3005u, ns).ToString());
            Assert.Null(dataType.Error);
            Assert.Equal("nsu=" + Pnem + ";i=3005", dataType.TypeNodeId);
            Assert.Equal(["A", "B", "C"], dataType.Fields.Select(f => f.Path));

            Assert.False(await manager.AddCompanionModelAsync("http://example.com/UA/Unknown/"));
            Assert.False(await manager.AddCompanionModelAsync(" "));
        }

        [Fact]
        public async Task Without_the_registry_an_uploaded_nodeset_is_stored_and_loaded_into_the_node_manager()
        {
            await using OpcUaServerFixture fixture = new();
            UANodeManager manager = UANodeManager.Instance;

            byte[] xml = await File.ReadAllBytesAsync(TestNodeSet("opcfoundation.org.UA.DI.nodeset2.xml"));

            Assert.Equal([Di], await manager.AddCompanionModelAsync(xml));
            Assert.Contains(Di, manager.NamespaceUris);
            Assert.True(File.Exists(Path.Combine(fixture.WorkingDirectory, "nodesets", "opcfoundation.org.UA.DI.nodeset2.xml")));

            await Assert.ThrowsAnyAsync<Exception>(() => manager.AddCompanionModelAsync(System.Text.Encoding.UTF8.GetBytes("<NotANodeSet/>")));
        }

        private static void CopyTestNodeSets(OpcUaServerFixture fixture, params string[] names)
        {
            foreach (string name in names)
            {
                File.Copy(TestNodeSet(name), Path.Combine(fixture.WorkingDirectory, "nodesets", name), overwrite: true);
            }
        }

        private static string TestNodeSet(string name)
        {
            string relativePath = Path.Combine("Tests", "UAEdgeTranslator.Tests", "TestData", "nodesets", name);
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
    }
}
