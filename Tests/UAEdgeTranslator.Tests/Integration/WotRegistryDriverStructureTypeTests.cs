namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Opc.Ua.Edge.Translator.WotCon;
    using Opc.Ua.Server;
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
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
