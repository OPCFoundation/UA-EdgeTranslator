namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Opc.Ua.Cloud.Library.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http.Headers;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// HTTP listener-driven coverage for the previously largely-empty
    /// <see cref="UACloudLibraryClient.LoginAsync"/> and
    /// <see cref="UACloudLibraryClient.DownloadNodesetAsync"/> request paths.
    /// We spin up a loopback <see cref="HttpListener"/> that mimics the Cloud
    /// Library REST API (<c>/infomodel/namespaces</c> and
    /// <c>/infomodel/download/{id}</c>) so the full HTTP success / failure /
    /// malformed-payload branches are exercised end-to-end without needing a
    /// real Cloud Library instance.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UACloudLibraryClientHttpTests
    {
        private const string _envUrl = "UACLURL";
        private const string _envUser = "UACLUsername";
        private const string _envPass = "UACLPassword";

        private const string _wellFormedNodesetXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris>
                <Uri>http://example.org/cloud/test/</Uri>
              </NamespaceUris>
              <Models>
                <Model ModelUri="http://example.org/cloud/test/" Version="1.0.0" PublicationDate="2025-01-01T00:00:00Z" />
              </Models>
            </UANodeSet>
            """;

        [Fact]
        public async Task LoginAsync_normalizes_url_without_trailing_slash_and_loads_namespaces()
        {
            using StubCloudLibrary stub = StubCloudLibrary.Start();
            // Map two entries for the same namespace so the version-comparison
            // branch in LoginAsync is exercised. The newer (2.0.0) entry must
            // win over the older (1.0.0) one.
            stub.NamespacesPayload = JsonConvert.SerializeObject(new[]
            {
                "urn:cloud:ns,id-old,1.0.0",
                "urn:cloud:ns,id-new,2.0.0",
                "urn:cloud:other,id-other,3.0.0",
                "no-commas-here",
                "too,few"
            });

            // Provide URL WITHOUT trailing slash so the path-normalization
            // branch (`if (!url.EndsWith('/')) url += "/"`) is hit.
            using EnvScope u = new(_envUrl, stub.BaseUrl.TrimEnd('/'));
            using EnvScope us = new(_envUser, "user");
            using EnvScope p = new(_envPass, "pw");

            UACloudLibraryClient client = new();
            await InvokeLogin(client);

            AuthenticationHeaderValue auth = GetAuthHeader(client);
            Assert.NotNull(auth);
            Assert.Equal("Basic", auth.Scheme);

            Dictionary<string, Tuple<string, string>> map = GetNamespaceMap(client);
            Assert.True(map.ContainsKey("urn:cloud:ns"));
            Assert.Equal("id-new", map["urn:cloud:ns"].Item1);
            Assert.Equal("2.0.0", map["urn:cloud:ns"].Item2);
            Assert.True(map.ContainsKey("urn:cloud:other"));
        }

        [Fact]
        public async Task LoginAsync_returns_when_namespace_listing_returns_non_2xx()
        {
            using StubCloudLibrary stub = StubCloudLibrary.Start();
            stub.NamespacesStatusCode = (int)HttpStatusCode.Unauthorized;
            stub.NamespacesPayload = "denied";

            using EnvScope u = new(_envUrl, stub.BaseUrl);
            using EnvScope us = new(_envUser, "user");
            using EnvScope p = new(_envPass, "pw");

            UACloudLibraryClient client = new();
            await InvokeLogin(client);

            // Auth header is built BEFORE the failed listing call so we expect it set.
            Assert.NotNull(GetAuthHeader(client));
            // But no namespaces were ingested.
            Assert.Empty(GetNamespaceMap(client));
        }

        [Fact]
        public async Task LoginAsync_returns_when_namespace_listing_returns_malformed_json()
        {
            using StubCloudLibrary stub = StubCloudLibrary.Start();
            stub.NamespacesPayload = "{not-json";

            using EnvScope u = new(_envUrl, stub.BaseUrl);
            using EnvScope us = new(_envUser, "user");
            using EnvScope p = new(_envPass, "pw");

            UACloudLibraryClient client = new();
            await InvokeLogin(client);

            Assert.NotNull(GetAuthHeader(client));
            Assert.Empty(GetNamespaceMap(client));
        }

        [Fact]
        public async Task DownloadNodesetAsync_fetches_from_cloud_when_not_local_and_caches_result()
        {
            using StubCloudLibrary stub = StubCloudLibrary.Start();

            const string Ns = "http://example.org/cloud/test/";

            stub.NamespacesPayload = JsonConvert.SerializeObject(new[] { $"{Ns},42,1.0.0" });
            stub.NodesetById["42"] = JsonConvert.SerializeObject(new UANameSpace
            {
                Nodeset = new Nodeset { NodesetXml = _wellFormedNodesetXml }
            });

            using EnvScope u = new(_envUrl, stub.BaseUrl);
            using EnvScope us = new(_envUser, "user");
            using EnvScope p = new(_envPass, "pw");
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "nodesets"));

            UACloudLibraryClient client = new();

            string xml = await client.DownloadNodesetAsync(Ns);
            Assert.False(string.IsNullOrEmpty(xml));
            Assert.Contains(Ns, xml);

            // The download path must have written the XML to disk under nodesets/.
            string[] files = Directory.GetFiles(Path.Combine(tmp.Path, "nodesets"));
            Assert.Single(files);
            Assert.EndsWith(".nodeset2.xml", files[0]);

            // Second call must come from the cache: kill the listener and the
            // call must still succeed.
            stub.Dispose();
            string second = await client.DownloadNodesetAsync(Ns);
            Assert.Equal(xml, second);
        }

        [Fact]
        public async Task DownloadNodesetAsync_returns_empty_when_cloud_download_returns_non_2xx()
        {
            using StubCloudLibrary stub = StubCloudLibrary.Start();
            const string Ns = "urn:cloud:download-error";
            stub.NamespacesPayload = JsonConvert.SerializeObject(new[] { $"{Ns},err,1.0.0" });
            stub.DownloadStatusCode = (int)HttpStatusCode.InternalServerError;
            stub.NodesetById["err"] = "ignored";

            using EnvScope u = new(_envUrl, stub.BaseUrl);
            using EnvScope us = new(_envUser, "user");
            using EnvScope p = new(_envPass, "pw");
            using TestWorkingDirectory tmp = new();

            UACloudLibraryClient client = new();
            string xml = await client.DownloadNodesetAsync(Ns);

            Assert.Equal(string.Empty, xml);
        }

        [Fact]
        public async Task DownloadNodesetAsync_returns_empty_when_cloud_returns_malformed_payload()
        {
            using StubCloudLibrary stub = StubCloudLibrary.Start();
            const string Ns = "urn:cloud:malformed";
            stub.NamespacesPayload = JsonConvert.SerializeObject(new[] { $"{Ns},mal,1.0.0" });
            stub.NodesetById["mal"] = "{not-json";

            using EnvScope u = new(_envUrl, stub.BaseUrl);
            using EnvScope us = new(_envUser, "user");
            using EnvScope p = new(_envPass, "pw");
            using TestWorkingDirectory tmp = new();

            UACloudLibraryClient client = new();
            string xml = await client.DownloadNodesetAsync(Ns);

            Assert.Equal(string.Empty, xml);
        }

        [Fact]
        public async Task DownloadNodesetAsync_returns_empty_when_cloud_returns_payload_with_no_xml()
        {
            using StubCloudLibrary stub = StubCloudLibrary.Start();
            const string Ns = "urn:cloud:no-xml";
            stub.NamespacesPayload = JsonConvert.SerializeObject(new[] { $"{Ns},noxml,1.0.0" });
            stub.NodesetById["noxml"] = JsonConvert.SerializeObject(new UANameSpace
            {
                Nodeset = new Nodeset { NodesetXml = string.Empty }
            });

            using EnvScope u = new(_envUrl, stub.BaseUrl);
            using EnvScope us = new(_envUser, "user");
            using EnvScope p = new(_envPass, "pw");
            using TestWorkingDirectory tmp = new();

            UACloudLibraryClient client = new();
            string xml = await client.DownloadNodesetAsync(Ns);

            Assert.Equal(string.Empty, xml);
        }

        // ---------------- helpers ----------------

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

        private static Dictionary<string, Tuple<string, string>> GetNamespaceMap(UACloudLibraryClient client)
        {
            FieldInfo f = typeof(UACloudLibraryClient).GetField(
                "_namespacesInCloudLibrary",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return (Dictionary<string, Tuple<string, string>>)f.GetValue(client);
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

        // Minimal HttpListener-backed stub of the Cloud Library REST surface
        // used by UACloudLibraryClient. Picks a free loopback port at startup
        // so multiple parallel test runs don't collide. Routes:
        //   GET /infomodel/namespaces      -> NamespacesPayload (200 by default)
        //   GET /infomodel/download/{id}   -> NodesetById[id] (200 by default)
        private sealed class StubCloudLibrary : IDisposable
        {
            private readonly HttpListener _listener;
            private readonly CancellationTokenSource _cts = new();
            private readonly Task _loop;

            public string BaseUrl { get; }
            public string NamespacesPayload { get; set; } = "[]";
            public int NamespacesStatusCode { get; set; } = 200;
            public Dictionary<string, string> NodesetById { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int DownloadStatusCode { get; set; } = 200;

            private StubCloudLibrary(int port)
            {
                BaseUrl = $"http://127.0.0.1:{port}/";
                _listener = new HttpListener();
                _listener.Prefixes.Add(BaseUrl);
                _listener.Start();
                _loop = Task.Run(ServeLoopAsync);
            }

            public static StubCloudLibrary Start()
            {
                int port = GetFreeTcpPort();
                return new StubCloudLibrary(port);
            }

            private async Task ServeLoopAsync()
            {
                while (!_cts.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        return;
                    }

                    try
                    {
                        HandleRequest(ctx);
                    }
                    catch
                    {
                        try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
                    }
                }
            }

            private void HandleRequest(HttpListenerContext ctx)
            {
                string path = ctx.Request.Url?.AbsolutePath ?? string.Empty;
                if (path.StartsWith("/infomodel/namespaces", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse(ctx, NamespacesStatusCode, NamespacesPayload);
                    return;
                }

                const string DlPrefix = "/infomodel/download/";
                if (path.StartsWith(DlPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string id = Uri.UnescapeDataString(path.Substring(DlPrefix.Length));
                    if (!NodesetById.TryGetValue(id, out string body))
                    {
                        WriteResponse(ctx, 404, string.Empty);
                        return;
                    }

                    WriteResponse(ctx, DownloadStatusCode, body);
                    return;
                }

                WriteResponse(ctx, 404, string.Empty);
            }

            private static void WriteResponse(HttpListenerContext ctx, int status, string body)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.ContentType = "application/json";
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }

            private static int GetFreeTcpPort()
            {
                System.Net.Sockets.TcpListener l = new(IPAddress.Loopback, 0);
                l.Start();
                try { return ((IPEndPoint)l.LocalEndpoint).Port; }
                finally { l.Stop(); }
            }

            public void Dispose()
            {
                try { _cts.Cancel(); } catch { }
                try { _listener.Stop(); } catch { }
                try { _listener.Close(); } catch { }
                try { _loop.Wait(2_000); } catch { }
            }
        }
    }
}
