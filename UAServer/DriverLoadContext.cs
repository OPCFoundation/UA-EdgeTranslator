namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using Sigstore;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Loader;
    using System.Security.Cryptography;
    using System.Threading;

    public class DriverLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _hostAssemblyName;

        // File name of the optional allow-list manifest. If it exists in the
        // drivers root, ONLY DLLs whose SHA-256 hash is listed will be loaded.
        // If it is absent, the loader stays in legacy "load everything" mode
        // for backwards compatibility — production deployments are expected
        // to ship this manifest (see README).
        private const string _allowListFileName = "drivers.allowlist.json";

        // Sigstore bundle that signs the allow-list manifest (cosign sign-blob
        // --bundle drivers.allowlist.sigstore.json drivers.allowlist.json). When
        // this file is present we additionally verify it with the Sigstore .NET
        // client and pin the OIDC identity to the GitHub workflow that produced
        // the driver pack, so an attacker who swaps the manifest cannot defeat
        // the allow-list without also forging a Fulcio-issued certificate and
        // a Rekor inclusion proof.
        private const string _allowListBundleFileName = "drivers.allowlist.sigstore.json";

        // Defaults match the OPC Foundation driver-pack workflow. Override via
        // env vars in deployments that publish the driver pack from a fork.
        private const string _defaultOidcIssuer = "https://token.actions.githubusercontent.com";
        private const string _defaultOidcRepo = "OPCFoundation/UA-EdgeTranslator";
        private const string _allowListWorkflowFile = ".github/workflows/driver-pack.yml";

        public DriverLoadContext(string pluginMainAssemblyPath) : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginMainAssemblyPath);
            _hostAssemblyName = typeof(IProtocolDriver).Assembly.GetName().Name;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // prevent loading a second copy of the host assembly into the plugin context
            if (string.Equals(assemblyName.Name, _hostAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }


        static public void LoadProtocolDrivers()
        {
            var driversRoot = Path.Combine(Directory.GetCurrentDirectory(), "drivers");

            if (!Directory.Exists(driversRoot))
            {
                Log.Logger.Information("Plugin directory not found: {DriversRoot}", driversRoot);
                return;
            }

            HashSet<string> allowedHashes = LoadAllowList(driversRoot, out bool enforceAllowList);

            foreach (var pluginDir in Directory.EnumerateDirectories(driversRoot))
            {
                foreach (var dll in Directory.EnumerateFiles(pluginDir, "*.dll"))
                {
                    try
                    {
                        if (enforceAllowList)
                        {
                            string hash = ComputeSha256(dll);
                            if (!allowedHashes.Contains(hash))
                            {
                                Log.Logger.Error(
                                    "Refusing to load protocol driver assembly {Dll}: SHA-256 {Hash} not in allow-list ({AllowList}).",
                                    dll,
                                    hash,
                                    Path.Combine(driversRoot, _allowListFileName));

                                continue;
                            }
                        }

                        var alc = new DriverLoadContext(dll);
                        var asm = alc.LoadFromAssemblyPath(dll);

                        var driverTypes = asm.GetTypes().Where(t => !t.IsAbstract && typeof(IProtocolDriver).IsAssignableFrom(t));
                        foreach (var dt in driverTypes)
                        {
                            // Use a clear error path when a driver type lacks a public parameterless
                            // constructor or fails to materialize as IProtocolDriver, instead of letting
                            // a NullReferenceException bubble up from Activator.CreateInstance(...)!.
                            object instance;
                            try
                            {
                                instance = Activator.CreateInstance(dt);
                            }
                            catch (Exception ex)
                            {
                                Log.Logger.Error(ex, "Could not instantiate protocol driver type {DriverType}", dt.FullName);
                                continue;
                            }

                            if (instance is not IProtocolDriver driver)
                            {
                                Log.Logger.Error("Protocol driver type {DriverType} does not implement IProtocolDriver or has no public parameterless constructor.", dt.FullName);
                                continue;
                            }

                            Program.Drivers.Register(driver);
                            Log.Logger.Information("Loaded protocol driver: {DriverScheme} ({DriverType})", driver.Scheme, dt.FullName);
                        }
                    }
                    catch (BadImageFormatException)
                    {
                        // ignore pInvoke/native DLLs that come with the driver
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error(ex, "Failed to load protocol driver from: {PluginDirectory}", pluginDir);
                    }
                }
            }
        }

        private static HashSet<string> LoadAllowList(string driversRoot, out bool enforceAllowList)
        {
            string manifestPath = Path.Combine(driversRoot, _allowListFileName);
            if (!File.Exists(manifestPath))
            {
                Log.Logger.Warning(
                    "Protocol driver allow-list {ManifestPath} not found; loading every *.dll under {DriversRoot}. " +
                    "Production deployments should ship a signed allow-list manifest — see README.",
                    manifestPath,
                    driversRoot);
                enforceAllowList = false;
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            // Verify the manifest's signature BEFORE we trust any of its contents.
            // If verification fails and the operator has not opted into offline mode,
            // we refuse to load any driver — fail closed.
            if (!TryVerifyManifestBundle(driversRoot, manifestPath, out string verificationFailure))
            {
                if (IsOfflineHashOnlyAllowed())
                {
                    Log.Logger.Warning(
                        "Protocol driver allow-list signature verification skipped/failed ({Reason}); " +
                        "DRIVER_ALLOWLIST_OFFLINE_MODE=allow-hash-only is set, falling back to hash-only enforcement.",
                        verificationFailure);
                }
                else
                {
                    Log.Logger.Error(
                        "Protocol driver allow-list {ManifestPath} failed signature verification ({Reason}); " +
                        "refusing to load ANY protocol driver. Set DRIVER_ALLOWLIST_OFFLINE_MODE=allow-hash-only " +
                        "to downgrade to hash-only enforcement in air-gapped deployments.",
                        manifestPath,
                        verificationFailure);

                    enforceAllowList = true;
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
            }

            try
            {
                string json = File.ReadAllText(manifestPath);
                AllowListManifest manifest = JsonConvert.DeserializeObject<AllowListManifest>(json);
                if (manifest?.Allowed == null || manifest.Allowed.Count == 0)
                {
                    Log.Logger.Error(
                        "Protocol driver allow-list {ManifestPath} is empty; refusing to load ANY protocol driver.",
                        manifestPath);

                    enforceAllowList = true;
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in manifest.Allowed)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Sha256))
                    {
                        hashes.Add(entry.Sha256.Trim());
                    }
                }

                Log.Logger.Information(
                    "Protocol driver allow-list loaded from {ManifestPath} with {Count} entries; only matching SHA-256 hashes will be loaded.",
                    manifestPath,
                    hashes.Count);

                enforceAllowList = true;
                return hashes;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(
                    ex,
                    "Failed to parse protocol driver allow-list {ManifestPath}; refusing to load ANY protocol driver.",
                    manifestPath);

                enforceAllowList = true;
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static bool TryVerifyManifestBundle(string driversRoot, string manifestPath, out string failureReason)
        {
            string bundlePath = Path.Combine(driversRoot, _allowListBundleFileName);
            if (!File.Exists(bundlePath))
            {
                failureReason = $"Sigstore bundle {bundlePath} not found";
                return false;
            }

            try
            {
                string bundleJson = File.ReadAllText(bundlePath);
                SigstoreBundle bundle = SigstoreBundle.Deserialize(bundleJson);

                VerificationPolicy policy = BuildIdentityPolicy();
                var verifier = new SigstoreVerifier();

                using FileStream payload = File.OpenRead(manifestPath);
                (bool success, VerificationResult result) = verifier
                    .TryVerifyStreamAsync(payload, bundle, policy, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                if (!success)
                {
                    failureReason = result?.FailureReason ?? "verifier reported failure";
                    return false;
                }

                Log.Logger.Information(
                    "Protocol driver allow-list {ManifestPath} verified against Sigstore bundle {BundlePath} (signer: {Identity}).",
                    manifestPath,
                    bundlePath,
                    DescribeIdentity(result));

                failureReason = null;
                return true;
            }
            catch (Exception ex)
            {
                failureReason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static VerificationPolicy BuildIdentityPolicy()
        {
            string issuer = Environment.GetEnvironmentVariable("DRIVER_ALLOWLIST_OIDC_ISSUER");
            if (string.IsNullOrWhiteSpace(issuer))
            {
                issuer = _defaultOidcIssuer;
            }

            string repo = Environment.GetEnvironmentVariable("DRIVER_ALLOWLIST_OIDC_REPO");
            if (string.IsNullOrWhiteSpace(repo))
            {
                repo = _defaultOidcRepo;
            }

            // Pin the SAN to the GitHub Actions workflow file that produces the
            // driver pack, allowing both branch (refs/heads/main) and tag
            // (refs/tags/vX.Y.Z) runs.
            string sanPattern =
                "^https://github\\.com/" + System.Text.RegularExpressions.Regex.Escape(repo) +
                "/" + System.Text.RegularExpressions.Regex.Escape(_allowListWorkflowFile) +
                "@refs/(heads/main|tags/v[^/]+)$";

            return new VerificationPolicy
            {
                CertificateIdentity = new CertificateIdentity
                {
                    Issuer = issuer,
                    SubjectAlternativeNamePattern = sanPattern
                }
            };
        }

        private static string DescribeIdentity(VerificationResult result)
        {
            try
            {
                VerifiedIdentity identity = result?.SignerIdentity;
                if (identity == null)
                {
                    return "<unknown>";
                }

                return $"issuer={identity.Issuer}, san={identity.SubjectAlternativeName}";
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static bool IsOfflineHashOnlyAllowed()
        {
            string mode = Environment.GetEnvironmentVariable("DRIVER_ALLOWLIST_OFFLINE_MODE");
            return string.Equals(mode, "allow-hash-only", StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSha256(string filePath)
        {
            using FileStream fs = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(fs);
            return Convert.ToHexString(hash);
        }

        // Layout of drivers/drivers.allowlist.json:
        // {
        //   "allowed": [
        //     { "name": "ModbusTCP.dll", "sha256": "ABCDEF…" },
        //     { "name": "Siemens.dll",   "sha256": "012345…" }
        //   ]
        // }
        private sealed class AllowListManifest
        {
            [JsonProperty("allowed")]
            public List<AllowListEntry> Allowed { get; set; }
        }

        private sealed class AllowListEntry
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("sha256")]
            public string Sha256 { get; set; }
        }
    }
}
