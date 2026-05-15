namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.IO;
    using System.Net.Http.Headers;
    using System.Reflection;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Exercises the deterministic edges of <see cref="UACloudLibraryClient.LoginAsync"/>
    /// and <see cref="UACloudLibraryClient.DownloadNodesetAsync"/> without
    /// requiring an actual Cloud Library HTTP endpoint. These cover the
    /// previously empty <c>LoginAsync</c> / <c>DownloadNodesetAsync</c>
    /// state-machine state transitions: missing URL, missing username,
    /// missing password, namespace not in cache and no-cloud-fallback.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UACloudLibraryClientLoginAndDownloadTests
    {
        private const string _envUrl = "UACLURL";
        private const string _envUser = "UACLUsername";
        private const string _envPass = "UACLPassword";

        private const string _wotConSampleNodesetXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>urn:test:download</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="urn:test:download" Version="1.0.0" PublicationDate="2025-01-01T00:00:00Z" />
              </Models>
            </UANodeSet>
            """;

        [Fact]
        public async Task LoginAsync_returns_when_url_missing_and_does_not_set_auth_header()
        {
            using EnvScope _u = new(_envUrl, null);
            using EnvScope _us = new(_envUser, "x");
            using EnvScope _p = new(_envPass, "y");

            UACloudLibraryClient client = new();
            await InvokeLogin(client);

            Assert.Null(GetAuthHeader(client));
        }

        [Fact]
        public async Task LoginAsync_returns_when_username_missing()
        {
            using EnvScope _u = new(_envUrl, "https://example.invalid/");
            using EnvScope _us = new(_envUser, null);
            using EnvScope _p = new(_envPass, "y");

            UACloudLibraryClient client = new();
            await InvokeLogin(client);

            Assert.Null(GetAuthHeader(client));
        }

        [Fact]
        public async Task LoginAsync_returns_when_password_missing()
        {
            using EnvScope _u = new(_envUrl, "https://example.invalid/");
            using EnvScope _us = new(_envUser, "x");
            using EnvScope _p = new(_envPass, null);

            UACloudLibraryClient client = new();
            await InvokeLogin(client);

            Assert.Null(GetAuthHeader(client));
        }

        [Fact]
        public async Task DownloadNodesetAsync_returns_local_xml_when_present_on_disk()
        {
            // No UACLURL set => LoginAsync warns and returns; download then
            // walks the local nodesets/ folder and returns the matching XML.
            using EnvScope _u = new(_envUrl, null);
            using TestWorkingDirectory tmp = new();

            string nodesetsDir = Path.Combine(tmp.Path, "nodesets");
            Directory.CreateDirectory(nodesetsDir);
            File.WriteAllText(Path.Combine(nodesetsDir, "sample.nodeset2.xml"), _wotConSampleNodesetXml);

            UACloudLibraryClient client = new();
            string xml = await client.DownloadNodesetAsync("urn:test:download");

            Assert.False(string.IsNullOrEmpty(xml));
            Assert.Contains("urn:test:download", xml);
        }

        [Fact]
        public async Task DownloadNodesetAsync_returns_empty_when_namespace_not_in_cloud_or_disk()
        {
            using EnvScope _u = new(_envUrl, null);
            using TestWorkingDirectory tmp = new();

            UACloudLibraryClient client = new();
            string xml = await client.DownloadNodesetAsync("urn:does:not:exist");

            Assert.Equal(string.Empty, xml);
        }

        [Fact]
        public async Task DownloadNodesetAsync_caches_xml_returned_from_disk()
        {
            using EnvScope _u = new(_envUrl, null);
            using TestWorkingDirectory tmp = new();

            string nodesetsDir = Path.Combine(tmp.Path, "nodesets");
            Directory.CreateDirectory(nodesetsDir);
            File.WriteAllText(Path.Combine(nodesetsDir, "sample.nodeset2.xml"), _wotConSampleNodesetXml);

            UACloudLibraryClient client = new();
            string first = await client.DownloadNodesetAsync("urn:test:download");
            Assert.False(string.IsNullOrEmpty(first));

            // Wipe the on-disk file: the cache should still serve the XML.
            File.Delete(Path.Combine(nodesetsDir, "sample.nodeset2.xml"));
            string second = await client.DownloadNodesetAsync("urn:test:download");
            Assert.Equal(first, second);
        }

        private static Task InvokeLogin(UACloudLibraryClient client)
        {
            MethodInfo m = typeof(UACloudLibraryClient).GetMethod(
                "LoginAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return (Task)m.Invoke(client, null);
        }

        private static AuthenticationHeaderValue GetAuthHeader(UACloudLibraryClient client)
        {
            FieldInfo f = typeof(UACloudLibraryClient).GetField(
                "_authHeader",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return (AuthenticationHeaderValue)f.GetValue(client);
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
