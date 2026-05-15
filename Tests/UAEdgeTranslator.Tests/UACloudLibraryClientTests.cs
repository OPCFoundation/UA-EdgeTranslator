namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// UACloudLibraryClient is constructed without external state, so tests can
    /// instantiate it directly. Network paths require credentials; we focus on
    /// the deterministic helpers (cache lookups, version parsing, on-disk
    /// nodeset matching) that don't require an HTTP round trip.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UACloudLibraryClientTests
    {
        private const string _wotConSampleNodesetXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:test:sample</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:sample" Version="1.0.0" PublicationDate="2025-01-01T00:00:00Z" />
              </Models>
            </UANodeSet>
            """;

        [Fact]
        public void GetDownloadedNodesetXml_rejects_blank_namespace()
        {
            using TestWorkingDirectory tmp = new();
            UACloudLibraryClient client = new();

            string xml = client.GetDownloadedNodesetXml(string.Empty);
            Assert.Equal(string.Empty, xml);

            xml = client.GetDownloadedNodesetXml(null);
            Assert.Equal(string.Empty, xml);
        }

        [Fact]
        public void GetDownloadedNodesetXml_returns_empty_when_nodesets_dir_missing()
        {
            using TestWorkingDirectory tmp = new();
            UACloudLibraryClient client = new();

            string xml = client.GetDownloadedNodesetXml("urn:not:there");
            Assert.Equal(string.Empty, xml);
        }

        [Fact]
        public void GetDownloadedNodesetXml_returns_empty_when_no_match_found()
        {
            using TestWorkingDirectory tmp = new();
            string nodesetsDir = Path.Combine(tmp.Path, "nodesets");
            Directory.CreateDirectory(nodesetsDir);
            File.WriteAllText(Path.Combine(nodesetsDir, "sample.nodeset2.xml"), _wotConSampleNodesetXml);

            UACloudLibraryClient client = new();
            string xml = client.GetDownloadedNodesetXml("urn:not-in-the-file");
            Assert.Equal(string.Empty, xml);
        }

        [Fact]
        public void GetDownloadedNodesetXml_returns_xml_when_model_uri_matches()
        {
            using TestWorkingDirectory tmp = new();
            string nodesetsDir = Path.Combine(tmp.Path, "nodesets");
            Directory.CreateDirectory(nodesetsDir);
            string filePath = Path.Combine(nodesetsDir, "sample.nodeset2.xml");
            File.WriteAllText(filePath, _wotConSampleNodesetXml);

            UACloudLibraryClient client = new();
            string xml = client.GetDownloadedNodesetXml("urn:test:sample");

            Assert.False(string.IsNullOrEmpty(xml));
            Assert.Contains("urn:test:sample", xml);
        }

        [Fact]
        public void GetDownloadedNodesetXml_skips_non_nodeset_files_without_throwing()
        {
            using TestWorkingDirectory tmp = new();
            string nodesetsDir = Path.Combine(tmp.Path, "nodesets");
            Directory.CreateDirectory(nodesetsDir);

            File.WriteAllText(Path.Combine(nodesetsDir, "junk.xml"), "not really a nodeset");
            File.WriteAllText(Path.Combine(nodesetsDir, "sample.nodeset2.xml"), _wotConSampleNodesetXml);

            UACloudLibraryClient client = new();
            string xml = client.GetDownloadedNodesetXml("urn:test:sample");

            Assert.Contains("urn:test:sample", xml);
        }

        [Fact]
        public async Task DownloadNodesetAsync_returns_empty_for_blank_namespace()
        {
            using TestWorkingDirectory tmp = new();
            UACloudLibraryClient client = new();

            Assert.Equal(string.Empty, await client.DownloadNodesetAsync(string.Empty));
            Assert.Equal(string.Empty, await client.DownloadNodesetAsync(null));
        }

        [Fact]
        public async Task DownloadNodesetAsync_serves_cached_value_without_login()
        {
            using TestWorkingDirectory tmp = new();
            UACloudLibraryClient client = new();

            // Inject a value directly into the private cache via reflection to
            // exercise the cache fast-path.
            object cache = typeof(UACloudLibraryClient)
                .GetField("_nodesetCache", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(client)!;

            ((System.Collections.Generic.Dictionary<string, string>)cache)["urn:cached"] = "<cached/>";

            string xml = await client.DownloadNodesetAsync("urn:cached");
            Assert.Equal("<cached/>", xml);
        }

        [Theory]
        [InlineData("1.2.3", true)]
        [InlineData("2", true)]
        [InlineData("1.0", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("not-a-version", false)]
        public void TryParseVersion_handles_common_inputs(string raw, bool expectedSuccess)
        {
            UACloudLibraryClient client = new();
            object[] args = new object[] { raw, null };
            bool result = (bool)typeof(UACloudLibraryClient)
                .GetMethod("TryParseVersion", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(client, args)!;

            Assert.Equal(expectedSuccess, result);
            if (expectedSuccess)
            {
                Assert.NotNull(args[1]);
                Assert.IsType<Version>(args[1]);
            }
        }
    }
}
