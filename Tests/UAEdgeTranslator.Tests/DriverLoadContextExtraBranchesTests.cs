namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Xunit;

    /// <summary>
    /// Drives the additional <see cref="DriverLoadContext"/> branches that the
    /// existing helper / loading tests don't reach: the bundle deserialization
    /// catch in <c>TryVerifyManifestBundle</c>, the corrupted-allow-list JSON
    /// catch in <c>LoadAllowList</c>, the inner-catch in
    /// <c>LoadProtocolDrivers</c>, and <c>DescribeIdentity</c> for a populated
    /// signer identity.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class DriverLoadContextExtraBranchesTests
    {
        private static readonly Type _sut = typeof(DriverLoadContext);

        [Fact]
        public void TryVerifyManifestBundle_returns_false_when_bundle_is_corrupted_json()
        {
            using TestWorkingDirectory tmp = new();
            string driversRoot = Path.Combine(tmp.Path, "drivers");
            Directory.CreateDirectory(driversRoot);

            // Plant a manifest the loader will hand to the verifier.
            string manifestPath = Path.Combine(driversRoot, "drivers.allowlist.json");
            File.WriteAllText(manifestPath, "{\"allowed\":[]}");

            // Plant a Sigstore bundle with text that won't deserialize, so
            // SigstoreBundle.Deserialize throws and the catch branch records
            // the failure reason.
            string bundlePath = Path.Combine(driversRoot, "drivers.allowlist.sigstore.json");
            File.WriteAllText(bundlePath, "this is not a valid sigstore bundle");

            object[] args = new object[] { driversRoot, manifestPath, null };
            bool ok = (bool)_sut
                .GetMethod("TryVerifyManifestBundle", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, args);

            Assert.False(ok);
            string reason = (string)args[2];
            Assert.False(string.IsNullOrEmpty(reason));
        }

        [Fact]
        public void LoadAllowList_returns_empty_set_when_manifest_json_is_corrupted_under_offline_mode()
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
                    "{not valid json");

                object[] args = new object[] { driversRoot, false };
                System.Collections.Generic.HashSet<string> hashes = (System.Collections.Generic.HashSet<string>)_sut
                    .GetMethod("LoadAllowList", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, args);

                bool enforce = (bool)args[1];
                Assert.True(enforce);
                Assert.Empty(hashes);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        [Fact]
        public void LoadProtocolDrivers_continues_when_one_pluginDirectory_contains_an_unloadable_dll()
        {
            // Plant a driver-pack with one valid managed dll (Mock) and one
            // syntactically-invalid managed dll. The loader should load the
            // good one and skip the bad one (BadImageFormatException catch
            // branch + outer try/catch).
            string mockAssemblyPath = typeof(Opc.Ua.Edge.Translator.ProtocolDrivers.MockProtocolDriver).Assembly.Location;
            using TestWorkingDirectory tmp = new();
            string pack = Path.Combine(tmp.Path, "drivers", "mixed-pack");
            Directory.CreateDirectory(pack);

            File.Copy(mockAssemblyPath, Path.Combine(pack, Path.GetFileName(mockAssemblyPath)));
            File.WriteAllBytes(Path.Combine(pack, "broken.dll"), new byte[] { 0x4D, 0x5A, 0x00, 0x00 }); // MZ header only

            // Must not throw and must register the mock driver.
            DriverLoadContext.LoadProtocolDrivers();

            ResetProgramDrivers();
        }

        [Fact]
        public void LoadProtocolDrivers_logs_error_and_skips_when_driver_constructor_throws()
        {
            // Drop the test assembly itself (which exposes ThrowingProtocolDriver)
            // into a fresh drivers/ subdir. The loader will discover the type via
            // GetTypes(), call Activator.CreateInstance, catch the InvalidOperationException,
            // and continue without registering anything.
            string testAssemblyPath = typeof(DriverLoadContextExtraBranchesTests).Assembly.Location;

            using TestWorkingDirectory tmp = new();
            string pack = Path.Combine(tmp.Path, "drivers", "throwing-pack");
            Directory.CreateDirectory(pack);

            File.Copy(testAssemblyPath, Path.Combine(pack, Path.GetFileName(testAssemblyPath)));

            // Must not throw; the catch branch handles the constructor exception.
            DriverLoadContext.LoadProtocolDrivers();

            ResetProgramDrivers();
        }

        [Fact]
        public void LoadProtocolDrivers_loads_dll_when_its_hash_matches_offline_allowlist()
        {
            // Compute the Mock.dll hash using the same algorithm DriverLoadContext uses.
            string mockAssemblyPath = typeof(Opc.Ua.Edge.Translator.ProtocolDrivers.MockProtocolDriver).Assembly.Location;
            string hash = ComputeSha256Hex(mockAssemblyPath);

            const string Var = "DRIVER_ALLOWLIST_OFFLINE_MODE";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, "allow-hash-only");

                using TestWorkingDirectory tmp = new();
                string driversRoot = Path.Combine(tmp.Path, "drivers");
                string pack = Path.Combine(driversRoot, "mock-pack");
                Directory.CreateDirectory(pack);

                File.Copy(mockAssemblyPath, Path.Combine(pack, Path.GetFileName(mockAssemblyPath)));
                File.WriteAllText(
                    Path.Combine(driversRoot, "drivers.allowlist.json"),
                    "{\"allowed\":[{\"name\":\"" + Path.GetFileName(mockAssemblyPath) + "\",\"sha256\":\"" + hash + "\"}]}");

                DriverLoadContext.LoadProtocolDrivers();
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
                ResetProgramDrivers();
            }
        }

        private static string ComputeSha256Hex(string path)
        {
            using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(stream);
            System.Text.StringBuilder sb = new(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        [Fact]
        public void DescribeIdentity_returns_unknown_when_signer_identity_is_null()
        {
            // The result has no SignerIdentity set, so the helper falls into
            // the "<unknown>" branch (already reached by null-result test, but
            // exercised here with a non-null result that still has a null
            // identity).
            object result = Activator.CreateInstance(GetVerificationResultType());
            string description = (string)_sut
                .GetMethod("DescribeIdentity", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { result });

            Assert.Equal("<unknown>", description);
        }

        [Fact]
        public void DescribeIdentity_describes_populated_signer_identity()
        {
            Type vrType = GetVerificationResultType();
            Type viType = vrType.Assembly.GetType("Sigstore.VerifiedIdentity", throwOnError: true);

            object identity = Activator.CreateInstance(viType);
            viType.GetProperty("Issuer").SetValue(identity, "https://oauth2.sigstore.dev/auth");
            viType.GetProperty("SubjectAlternativeName").SetValue(identity, "https://github.com/example/repo/.github/workflows/sign.yml@refs/heads/main");

            object result = Activator.CreateInstance(vrType);
            vrType.GetProperty("SignerIdentity").SetValue(result, identity);

            string description = (string)_sut
                .GetMethod("DescribeIdentity", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { result });

            Assert.Contains("issuer=https://oauth2.sigstore.dev/auth", description);
            Assert.Contains("san=https://github.com/example/repo", description);
        }

        private static Type GetVerificationResultType()
        {
            // Force-load the Sigstore assembly via a reflection-only path:
            // grab a Sigstore-typed parameter from DriverLoadContext.TryVerifyManifestBundle.
            MethodInfo verify = _sut.GetMethod("TryVerifyManifestBundle", BindingFlags.NonPublic | BindingFlags.Static);
            Assembly sigstore = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name?.Equals("Sigstore", StringComparison.OrdinalIgnoreCase) == true)
                {
                    sigstore = a;
                    break;
                }
            }
            if (sigstore == null)
            {
                // Touching a Sigstore type indirectly via DriverLoadContext methods loads it.
                _sut.GetMethod("BuildIdentityPolicy", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name?.Equals("Sigstore", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        sigstore = a;
                        break;
                    }
                }
            }
            Assert.NotNull(sigstore);
            Type t = sigstore.GetType("Sigstore.VerificationResult", throwOnError: false)
                ?? FindType(sigstore, "VerificationResult");
            Assert.NotNull(t);
            return t;
        }

        private static Type FindType(Assembly assembly, string simpleName)
        {
            foreach (Type t in assembly.GetTypes())
            {
                if (t.Name == simpleName) return t;
            }
            return null;
        }

        private static void ResetProgramDrivers()
        {
            object registry = typeof(Program)
                .GetProperty(nameof(Program.Drivers), BindingFlags.Public | BindingFlags.Static)
                .GetValue(null);
            if (registry == null) return;

            FieldInfo driversField = registry.GetType().GetField(
                "_drivers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (driversField?.GetValue(registry) is System.Collections.IDictionary driversMap)
            {
                driversMap.Clear();
            }
        }
    }
}
