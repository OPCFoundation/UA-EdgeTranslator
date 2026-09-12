namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.IO;
    using Xunit;

    /// <summary>
    /// Tests for loading Basic Station router configurations at startup.
    /// <para>
    /// A gateway sends its <c>version</c> message as soon as the network server
    /// is listening and expects a <c>router_config</c> in reply. The
    /// configuration therefore has to be in memory when the server is
    /// instantiated, not only once an asset happens to be onboarded.
    /// </para>
    /// <para>
    /// <see cref="RouterConfigStore"/> is process-global, and constructing a
    /// <c>LoRaWANNetworkServerAsset</c> repopulates it from the current working
    /// directory. These tests therefore join the serialized
    /// <see cref="WorkingDirectoryCollection"/> so no parallel class can reload
    /// the store mid-assertion.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class RouterConfigStoreTests : IDisposable
    {
        private readonly string _folder;

        public RouterConfigStoreTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "routercfg-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            // Reset the static store so ordering between tests cannot matter.
            RouterConfigStore.LoadAll(Path.Combine(_folder, "does-not-exist"));

            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }

            GC.SuppressFinalize(this);
        }

        private string Write(string fileName, string contents)
        {
            string path = Path.Combine(_folder, fileName);

            File.WriteAllText(path, contents);

            return path;
        }

        private const string RouterConfig = /*lang=json,strict*/
            """{"msgtype":"router_config","region":"EU863","hwspec":"sx1301/1"}""";

        [Fact]
        public void A_model_named_config_becomes_the_default()
        {
            // A router configuration describes radio hardware, so a file named
            // after the model serves any gateway of that model.
            Write("SX1301EU.json", RouterConfig);

            RouterConfigStore.LoadAll(_folder);

            Assert.NotNull(RouterConfigStore.DefaultConfig);
            Assert.Equal(RouterConfig, RouterConfigStore.Get("2CCF67FFFEFB3C8F"));
        }

        [Fact]
        public void A_eui_named_config_is_served_to_that_gateway_only()
        {
            const string specific = /*lang=json,strict*/
                """{"msgtype":"router_config","region":"US902","hwspec":"sx1301/1"}""";

            Write("SX1301EU.json", RouterConfig);
            Write("2CCF67FFFEFB3C8F.json", specific);

            RouterConfigStore.LoadAll(_folder);

            // The gateway with its own file gets it; everyone else gets the default.
            Assert.Equal(specific, RouterConfigStore.Get("2CCF67FFFEFB3C8F"));
            Assert.Equal(RouterConfig, RouterConfigStore.Get("A84041B98D5CB233"));
        }

        [Fact]
        public void A_eui_named_config_is_matched_case_insensitively()
        {
            Write("2ccf67fffefb3c8f.json", RouterConfig);

            RouterConfigStore.LoadAll(_folder);

            Assert.Equal(RouterConfig, RouterConfigStore.Get("2CCF67FFFEFB3C8F"));
        }

        [Fact]
        public void Unrelated_json_in_the_settings_folder_is_ignored()
        {
            // The settings folder holds other JSON; only router_config messages
            // may be served to a gateway.
            Write("appsettings.json", /*lang=json,strict*/ """{"Logging":{"LogLevel":"Debug"}}""");

            RouterConfigStore.LoadAll(_folder);

            Assert.Null(RouterConfigStore.DefaultConfig);
            Assert.Null(RouterConfigStore.Get("2CCF67FFFEFB3C8F"));
        }

        [Fact]
        public void A_malformed_file_does_not_prevent_the_others_loading()
        {
            Write("broken.json", "{ this is not json");
            Write("SX1301EU.json", RouterConfig);

            RouterConfigStore.LoadAll(_folder);

            Assert.Equal(RouterConfig, RouterConfigStore.Get("2CCF67FFFEFB3C8F"));
        }

        [Fact]
        public void A_missing_settings_folder_is_not_an_error()
        {
            // The translator must still start when no gateway is provisioned.
            RouterConfigStore.LoadAll(Path.Combine(_folder, "nope"));

            Assert.Null(RouterConfigStore.DefaultConfig);
        }

        [Fact]
        public void Reloading_replaces_the_previous_configurations()
        {
            Write("SX1301EU.json", RouterConfig);
            RouterConfigStore.LoadAll(_folder);
            Assert.NotNull(RouterConfigStore.DefaultConfig);

            File.Delete(Path.Combine(_folder, "SX1301EU.json"));
            RouterConfigStore.LoadAll(_folder);

            // A removed configuration must not linger and be served to a gateway.
            Assert.Null(RouterConfigStore.DefaultConfig);
        }

        [Fact]
        public void The_shipped_sample_loads_as_a_router_configuration()
        {
            string sample = FindSample("SX1301EU.json");

            File.Copy(sample, Path.Combine(_folder, "SX1301EU.json"));

            RouterConfigStore.LoadAll(_folder);

            Assert.NotNull(RouterConfigStore.DefaultConfig);
            Assert.Contains("router_config", RouterConfigStore.DefaultConfig, StringComparison.Ordinal);
        }

        [Fact]
        public void Adding_a_config_binds_it_to_a_specific_gateway()
        {
            Write("SX1301EU.json", RouterConfig);
            RouterConfigStore.LoadAll(_folder);

            const string specific = /*lang=json,strict*/
                """{"msgtype":"router_config","region":"AS923","hwspec":"sx1301/1"}""";

            RouterConfigStore.Add("2CCF67FFFEFB3C8F", specific);

            Assert.Equal(specific, RouterConfigStore.Get("2CCF67FFFEFB3C8F"));
            Assert.Equal(RouterConfig, RouterConfigStore.Get("A84041B98D5CB233"));
        }

        [Fact]
        public void Adding_a_config_without_a_gateway_is_rejected()
        {
            Assert.Throws<ArgumentException>(() => RouterConfigStore.Add(string.Empty, RouterConfig));
        }

        private static string FindSample(string fileName)
        {
            DirectoryInfo directory = new(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "Samples", fileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException($"Could not locate the sample '{fileName}'.");
        }
    }
}
