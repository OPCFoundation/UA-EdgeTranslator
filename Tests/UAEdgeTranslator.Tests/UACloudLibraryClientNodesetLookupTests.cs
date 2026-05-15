namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Coverage for <see cref="UACloudLibraryClient.GetDownloadedNodesetXml"/>
    /// and the previously empty edge branches of
    /// <see cref="UACloudLibraryClient.DownloadNodesetAsync"/>: empty/missing
    /// inputs, the missing nodesets directory, and the per-file
    /// "skip the broken candidate but still return the good one" branch.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UACloudLibraryClientNodesetLookupTests
    {
        private const string _envUrl = "UACLURL";

        private const string _wellFormedNodesetXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:test:lookup</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:lookup" Version="1.0.0" PublicationDate="2025-01-01T00:00:00Z" />
              </Models>
            </UANodeSet>
            """;

        [Fact]
        public void GetDownloadedNodesetXml_returns_empty_for_null_or_empty_namespace_url()
        {
            UACloudLibraryClient client = new();

            Assert.Equal(string.Empty, client.GetDownloadedNodesetXml(null));
            Assert.Equal(string.Empty, client.GetDownloadedNodesetXml(string.Empty));
        }

        [Fact]
        public void GetDownloadedNodesetXml_returns_empty_when_nodesets_directory_missing()
        {
            using TestWorkingDirectory tmp = new();
            // Deliberately do NOT create the nodesets/ folder.

            UACloudLibraryClient client = new();
            string xml = client.GetDownloadedNodesetXml("urn:test:lookup");

            Assert.Equal(string.Empty, xml);
        }

        [Fact]
        public void GetDownloadedNodesetXml_skips_corrupted_files_and_still_returns_valid_match()
        {
            using TestWorkingDirectory tmp = new();
            string nodesetsDir = Path.Combine(tmp.Path, "nodesets");
            Directory.CreateDirectory(nodesetsDir);

            // A clearly non-XML / corrupted candidate that should be skipped
            // by the parse-and-keep-going branch instead of tearing down the
            // whole onboarding pipeline.
            File.WriteAllText(Path.Combine(nodesetsDir, "broken.txt"), "not a nodeset");

            // A valid candidate that the lookup should still find.
            File.WriteAllText(Path.Combine(nodesetsDir, "ok.nodeset2.xml"), _wellFormedNodesetXml);

            UACloudLibraryClient client = new();
            string xml = client.GetDownloadedNodesetXml("urn:test:lookup");

            Assert.False(string.IsNullOrEmpty(xml));
            Assert.Contains("urn:test:lookup", xml);
        }

        [Fact]
        public void GetDownloadedNodesetXml_returns_empty_when_no_file_matches_namespace()
        {
            using TestWorkingDirectory tmp = new();
            string nodesetsDir = Path.Combine(tmp.Path, "nodesets");
            Directory.CreateDirectory(nodesetsDir);
            File.WriteAllText(Path.Combine(nodesetsDir, "ok.nodeset2.xml"), _wellFormedNodesetXml);

            UACloudLibraryClient client = new();
            string xml = client.GetDownloadedNodesetXml("urn:not:in:any:file");

            Assert.Equal(string.Empty, xml);
        }

        [Fact]
        public async Task DownloadNodesetAsync_returns_empty_for_null_or_empty_namespace_url()
        {
            using EnvScope _u = new(_envUrl, null);
            UACloudLibraryClient client = new();

            Assert.Equal(string.Empty, await client.DownloadNodesetAsync(null));
            Assert.Equal(string.Empty, await client.DownloadNodesetAsync(string.Empty));
        }

        private sealed class EnvScope : IDisposable
        {
            private readonly string _name;
            private readonly string _previous;

            public EnvScope(string name, string value)
            {
                _name = name;
                _previous = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }

            public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
        }
    }
}
