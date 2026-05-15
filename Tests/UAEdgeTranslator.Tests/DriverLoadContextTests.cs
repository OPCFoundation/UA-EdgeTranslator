namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using Xunit;

    /// <summary>
    /// DriverLoadContext is a static loader that reads from
    /// <c>{cwd}/drivers/</c>. The tests below pin a clean working directory so
    /// they can exercise the early-exit and the manifest decision tree without
    /// touching the developer's repo.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class DriverLoadContextTests
    {
        private static readonly Type _sut = typeof(DriverLoadContext);

        [Fact]
        public void LoadProtocolDrivers_returns_silently_when_drivers_directory_is_missing()
        {
            using TestWorkingDirectory tmp = new();

            // No drivers/ directory exists in the temp cwd. Must be a no-op,
            // not an exception.
            DriverLoadContext.LoadProtocolDrivers();
        }

        [Fact]
        public void LoadProtocolDrivers_skips_empty_drivers_directory()
        {
            using TestWorkingDirectory tmp = new();
            Directory.CreateDirectory(Path.Combine(tmp.Path, "drivers"));

            DriverLoadContext.LoadProtocolDrivers();
        }

        [Fact]
        public void IsOfflineHashOnlyAllowed_honors_environment_variable()
        {
            const string Var = "DRIVER_ALLOWLIST_OFFLINE_MODE";
            string previous = Environment.GetEnvironmentVariable(Var);

            try
            {
                Environment.SetEnvironmentVariable(Var, null);
                Assert.False(InvokeBool("IsOfflineHashOnlyAllowed"));

                Environment.SetEnvironmentVariable(Var, "allow-hash-only");
                Assert.True(InvokeBool("IsOfflineHashOnlyAllowed"));

                Environment.SetEnvironmentVariable(Var, "ALLOW-HASH-ONLY");
                Assert.True(InvokeBool("IsOfflineHashOnlyAllowed"));

                Environment.SetEnvironmentVariable(Var, "off");
                Assert.False(InvokeBool("IsOfflineHashOnlyAllowed"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        [Fact]
        public void ComputeSha256_returns_hex_digest_matching_System_Security_Cryptography()
        {
            using TestWorkingDirectory tmp = new();
            string file = Path.Combine(tmp.Path, "data.bin");
            byte[] payload = new byte[] { 1, 2, 3, 4, 5 };
            File.WriteAllBytes(file, payload);

            string actual = (string)InvokeStatic("ComputeSha256", file);

            string expected = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(payload));

            Assert.Equal(expected, actual, ignoreCase: true);
            Assert.Equal(64, actual.Length); // 32 bytes -> 64 hex chars
        }

        [Fact]
        public void LoadAllowList_falls_back_to_legacy_mode_when_manifest_is_missing()
        {
            using TestWorkingDirectory tmp = new();
            string driversRoot = Path.Combine(tmp.Path, "drivers");
            Directory.CreateDirectory(driversRoot);

            HashSet<string> hashes = InvokeLoadAllowList(driversRoot, out bool enforce);

            Assert.False(enforce);
            Assert.NotNull(hashes);
            Assert.Empty(hashes);
        }

        [Fact]
        public void LoadAllowList_fails_closed_when_bundle_signature_cannot_be_verified()
        {
            // Manifest is present but Sigstore bundle is missing AND offline mode
            // is not enabled, so the loader must return enforce=true with an
            // empty allow-list (refuse to load any driver).
            const string Var = "DRIVER_ALLOWLIST_OFFLINE_MODE";
            string previous = Environment.GetEnvironmentVariable(Var);

            try
            {
                Environment.SetEnvironmentVariable(Var, null);

                using TestWorkingDirectory tmp = new();
                string driversRoot = Path.Combine(tmp.Path, "drivers");
                Directory.CreateDirectory(driversRoot);
                File.WriteAllText(
                    Path.Combine(driversRoot, "drivers.allowlist.json"),
                    "{\"allowed\":[{\"name\":\"x.dll\",\"sha256\":\"deadbeef\"}]}");

                HashSet<string> hashes = InvokeLoadAllowList(driversRoot, out bool enforce);

                Assert.True(enforce);
                Assert.Empty(hashes);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        [Fact]
        public void LoadAllowList_fails_closed_when_manifest_is_malformed_under_offline_mode()
        {
            const string Var = "DRIVER_ALLOWLIST_OFFLINE_MODE";
            string previous = Environment.GetEnvironmentVariable(Var);

            try
            {
                Environment.SetEnvironmentVariable(Var, "allow-hash-only");

                using TestWorkingDirectory tmp = new();
                string driversRoot = Path.Combine(tmp.Path, "drivers");
                Directory.CreateDirectory(driversRoot);
                File.WriteAllText(Path.Combine(driversRoot, "drivers.allowlist.json"), "this is not json");

                HashSet<string> hashes = InvokeLoadAllowList(driversRoot, out bool enforce);

                Assert.True(enforce);
                Assert.Empty(hashes);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        private static bool InvokeBool(string method)
            => (bool)_sut
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null)!;

        private static object InvokeStatic(string method, params object[] args)
            => _sut
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, args)!;

        private static HashSet<string> InvokeLoadAllowList(string driversRoot, out bool enforce)
        {
            object[] args = new object[] { driversRoot, false };
            HashSet<string> result = (HashSet<string>)_sut
                .GetMethod("LoadAllowList", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, args)!;
            enforce = (bool)args[1];
            return result;
        }
    }
}
