namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Opc.Ua.Edge.Translator.WotCon;
    using Opc.Ua.Server;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// A Rockwell Thing Description maps its UDT properties onto structure types in
    /// the asset's own namespace, which the protocol driver creates from the
    /// <c>eipv:structureDefinition</c> embedded in the forms.
    /// <para>
    /// Only legacy onboarding used to create them, so a Thing the WoT Connectivity
    /// registry loaded on its own looked the asset's namespace up as a companion model,
    /// failed, and materialized the mapped variables without their structure type -
    /// they then never received driver data. It worked after a restart only when legacy
    /// onboarding happened to load the same file first.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class WotRegistryDriverStructureTypeTests
    {
        private static readonly string[] _udtTypes = ["AB:1756_DO:O:0Type", "UDT_Shift_DataType", "IO_ControlType"];

        [Fact]
        public async Task A_rockwell_thing_loaded_only_by_the_registry_gets_its_udt_structure_types()
        {
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;
            Program.Drivers.Register(new RockwellProtocolDriver());

            // The shipped sample, pointed at a port nothing listens on so no PLC is needed.
            string td = Regex.Replace(
                File.ReadAllText(FindRepositoryFile(Path.Combine("Samples", "Rockwell.td.jsonld"))),
                "\"base\"\\s*:\\s*\"eip://[^\"]*\"",
                "\"base\": \"eip://127.0.0.1:1/1,0/\"");

            string importFolder = Path.Combine(fixture.WorkingDirectory, "rockwell-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(importFolder);
            await File.WriteAllTextAsync(Path.Combine(importFolder, "Rockwell.td.jsonld"), td);

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);
            await host.ImportSettingsFilesAsync(importFolder, default);

            int ns = server.CurrentInstance.NamespaceUris.GetIndex("http://opcfoundation.org/UA/Rockwell/");
            Assert.True(ns >= 0, "The asset's own namespace was not created.");

            foreach (string typeName in _udtTypes)
            {
                Assert.IsType<DataTypeState>(UANodeManager.Instance.Find(new NodeId(typeName, (ushort)ns)));
            }
        }

        [Fact]
        public async Task A_udt_property_read_through_the_registry_is_a_decoded_structure()
        {
            // Regression: a driver returns a UDT as its decoded fields, which the channel
            // encoded as an opaque binary ExtensionObject. The registry only translates
            // decoded structures between namespace tables, so every read of a
            // 'uav:mapToType' structure property failed with BadNotSupported.
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;
            Program.Drivers.Register(new RockwellProtocolDriver());
            Program.Drivers.Register(new UdtDriver());

            string rockwellFolder = Path.Combine(fixture.WorkingDirectory, "rockwell-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rockwellFolder);
            await File.WriteAllTextAsync(Path.Combine(rockwellFolder, "Rockwell.td.jsonld"), Regex.Replace(
                File.ReadAllText(FindRepositoryFile(Path.Combine("Samples", "Rockwell.td.jsonld"))),
                "\"base\"\\s*:\\s*\"eip://[^\"]*\"",
                "\"base\": \"eip://127.0.0.1:1/1,0/\""));

            string udtFolder = Path.Combine(fixture.WorkingDirectory, "udt-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(udtFolder);
            await File.WriteAllTextAsync(Path.Combine(udtFolder, "udt-1.jsonld"), """
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
                  "@type": ["Thing"],
                  "id": "urn:udt-1",
                  "title": "udt-1",
                  "base": "udtmock://device-1:1/1",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": ["nosec_sc"],
                  "properties": {
                    "Production_Data": {
                      "type": "object",
                      "uav:mapToType": "nsu=http://opcfoundation.org/UA/Rockwell/;s=UDT_Shift_DataType",
                      "observable": true,
                      "forms": [ { "href": "Production_Data", "op": ["readproperty", "observeproperty"] } ]
                    }
                  }
                }
                """);

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);

            // The UDT DataType comes from the Rockwell Thing, so load it first.
            await host.ImportSettingsFilesAsync(rockwellFolder, default);
            await host.ImportSettingsFilesAsync(udtFolder, default);

            AddressSpaceService service = new();
            NodeId property = NodeId.Null;
            DataValue value = DataValue.FromStatusCode(StatusCodes.BadWaitingForInitialData);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (property.IsNull)
                {
                    AddressSpaceNode thing = (await service.BrowseChildrenAsync(ObjectIds.ObjectsFolder.ToString()))
                        .FirstOrDefault(c => c.Text == "udt-1 [xRegistry]");
                    AddressSpaceNode child = thing == null
                        ? null
                        : (await service.BrowseChildrenAsync(thing.Id)).FirstOrDefault(c => c.Text == "Production_Data");
                    property = child == null ? NodeId.Null : NodeId.Parse(child.Id);
                }

                if (!property.IsNull)
                {
                    var read = await server.CurrentInstance.NodeManager.ReadAsync(
                        new OperationContext(new RequestHeader(), null, RequestType.Read, RequestLifetime.None),
                        0,
                        TimestampsToReturn.Both,
                        new[] { new ReadValueId { NodeId = property, AttributeId = Attributes.Value } },
                        default);
                    value = read.Item1[0];
                    if (StatusCode.IsGood(value.StatusCode))
                    {
                        break;
                    }
                }

                await Task.Delay(50);
            }

            Assert.False(property.IsNull, "The Thing's structure property was never materialized.");
            Assert.True(StatusCode.IsGood(value.StatusCode), "structure read: " + value.StatusCode);
            Assert.True(value.WrappedValue.TryGetStructure(out IEncodeable body), value.WrappedValue.ToString());
            IStructure structure = Assert.IsAssignableFrom<IStructure>(body);
            Assert.Equal(42, structure["Prodution_Count"].GetInt32());
            Assert.Equal(1.5f, structure["Cycle_Time"].GetFloat());
            Assert.Equal(2, structure["Shift_Number"].GetInt32());

            // The dashboard's explorer shows the decoded fields too.
            IReadOnlyDictionary<string, string> shown = await service.ReadValuesAsync([property.ToString()]);
            Assert.StartsWith("{ Prodution_Count = 42, ", shown[property.ToString()], StringComparison.Ordinal);
        }

        /// <summary>A driver whose asset returns a UDT as its decoded fields, as the Rockwell driver does.</summary>
        private sealed class UdtDriver : IProtocolDriver
        {
            public string Scheme => "udtmock";

            public string WoTBindingUri => "https://opcfoundation.org/UA-EdgeTranslator/udtmock";

            public IEnumerable<string> Discover() => [];

            public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint) => throw new NotSupportedException();

            public Task<AssetConnection> CreateAndConnectAssetAsync(ThingDescription td, CancellationToken cancellationToken = default) =>
                Task.FromResult(new AssetConnection(new UdtAsset(), 1));

            public AssetTag CreateTag(ThingDescription td, object form, string assetId, byte unitId, string variableId, string mappedUAExpandedNodeId, string mappedUAFieldPath) =>
                new() { Name = variableId, Address = variableId, UnitID = unitId, Type = "object", PollingInterval = 1000 };
        }

        private sealed class UdtAsset : IAsset
        {
            public bool IsConnected => true;

            public string GetRemoteEndpoint() => "udtmock://device-1:1";

            public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default) =>
                Task.FromResult<object>(new Dictionary<string, object>
                {
                    ["Prodution_Count"] = 42,
                    ["Cycle_Time"] = 1.5f,
                    ["Shift_Number"] = 2
                });

            public Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default) =>
                Task.FromResult(AssetActionResult.FromOutputs("ok", new List<object>()));

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
    }
}
