namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using Xunit;

    /// <summary>
    /// Additional reflection-based coverage for <see cref="DriverLoadContext"/>
    /// helpers that talk to environment-driven defaults: <c>BuildIdentityPolicy</c>,
    /// <c>DescribeIdentity</c>, and the allow-list "valid manifest passes through"
    /// path under offline mode.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class DriverLoadContextHelperTests
    {
        private static readonly Type _sut = typeof(DriverLoadContext);

        [Fact]
        public void BuildIdentityPolicy_uses_default_issuer_and_repo_when_env_unset()
        {
            using EnvScope _i = new("DRIVER_ALLOWLIST_OIDC_ISSUER", null);
            using EnvScope _r = new("DRIVER_ALLOWLIST_OIDC_REPO", null);

            object policy = InvokeStatic("BuildIdentityPolicy");
            Assert.NotNull(policy);

            object identity = policy.GetType()
                .GetProperty("CertificateIdentity")
                .GetValue(policy);
            Assert.NotNull(identity);

            string issuer = (string)identity.GetType().GetProperty("Issuer").GetValue(identity);
            string san = (string)identity.GetType().GetProperty("SubjectAlternativeNamePattern").GetValue(identity);

            Assert.Equal("https://token.actions.githubusercontent.com", issuer);
            Assert.Contains("OPCFoundation/UA-EdgeTranslator", san);
            Assert.Contains("driver-pack", san);
            Assert.StartsWith("^https://github\\.com/", san);
        }

        [Fact]
        public void BuildIdentityPolicy_honors_environment_overrides()
        {
            using EnvScope _i = new("DRIVER_ALLOWLIST_OIDC_ISSUER", "https://my-issuer/");
            using EnvScope _r = new("DRIVER_ALLOWLIST_OIDC_REPO", "MyOrg/MyRepo");

            object policy = InvokeStatic("BuildIdentityPolicy");
            object identity = policy.GetType().GetProperty("CertificateIdentity").GetValue(policy);
            string issuer = (string)identity.GetType().GetProperty("Issuer").GetValue(identity);
            string san = (string)identity.GetType().GetProperty("SubjectAlternativeNamePattern").GetValue(identity);

            Assert.Equal("https://my-issuer/", issuer);
            Assert.Contains("MyOrg/MyRepo", san);
        }

        [Fact]
        public void DescribeIdentity_returns_unknown_for_null_result()
        {
            string description = (string)InvokeStatic("DescribeIdentity", new object[] { null });
            Assert.Equal("<unknown>", description);
        }

        [Fact]
        public void LoadAllowList_returns_populated_hash_set_under_offline_mode_with_valid_manifest()
        {
            const string Var = "DRIVER_ALLOWLIST_OFFLINE_MODE";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, "allow-hash-only");

                using TestWorkingDirectory tmp = new();
                string driversRoot = Path.Combine(tmp.Path, "drivers");
                Directory.CreateDirectory(driversRoot);
                File.WriteAllText(
                    Path.Combine(driversRoot, "drivers.allowlist.json"),
                    "{\"allowed\":[" +
                        "{\"name\":\"a.dll\",\"sha256\":\"AABB\"}," +
                        "{\"name\":\"b.dll\",\"sha256\":\"CCDD\"}," +
                        "{\"name\":\"c.dll\",\"sha256\":\"   \"}" +
                    "]}");

                HashSet<string> hashes = InvokeLoadAllowList(driversRoot, out bool enforce);

                Assert.True(enforce);
                Assert.Equal(2, hashes.Count);
                Assert.Contains("AABB", hashes);
                Assert.Contains("ccdd", hashes); // case-insensitive
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        [Fact]
        public void LoadAllowList_treats_empty_allow_array_as_fail_closed_under_offline_mode()
        {
            const string Var = "DRIVER_ALLOWLIST_OFFLINE_MODE";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, "allow-hash-only");

                using TestWorkingDirectory tmp = new();
                string driversRoot = Path.Combine(tmp.Path, "drivers");
                Directory.CreateDirectory(driversRoot);
                File.WriteAllText(
                    Path.Combine(driversRoot, "drivers.allowlist.json"),
                    "{\"allowed\":[]}");

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
        public void LoadProtocolDrivers_with_allowlist_skips_dll_whose_hash_is_not_listed()
        {
            const string Var = "DRIVER_ALLOWLIST_OFFLINE_MODE";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, "allow-hash-only");

                using TestWorkingDirectory tmp = new();
                string driversRoot = Path.Combine(tmp.Path, "drivers");
                string driverDir = Path.Combine(driversRoot, "any");
                Directory.CreateDirectory(driverDir);

                // Plant a junk DLL whose hash will not be in the allow-list.
                File.WriteAllBytes(Path.Combine(driverDir, "junk.dll"), new byte[] { 1, 2, 3, 4 });
                File.WriteAllText(
                    Path.Combine(driversRoot, "drivers.allowlist.json"),
                    "{\"allowed\":[{\"name\":\"x.dll\",\"sha256\":\"DEADBEEF\"}]}");

                // Must not throw; the loader should refuse to load the DLL because its hash isn't listed.
                DriverLoadContext.LoadProtocolDrivers();
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        private static object InvokeStatic(string method, object[] args = null)
            => _sut
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, args);

        private static HashSet<string> InvokeLoadAllowList(string driversRoot, out bool enforce)
        {
            object[] args = new object[] { driversRoot, false };
            HashSet<string> result = (HashSet<string>)_sut
                .GetMethod("LoadAllowList", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, args);
            enforce = (bool)args[1];
            return result;
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
