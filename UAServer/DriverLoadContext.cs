namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Loader;
    using System.Security.Cryptography;

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
