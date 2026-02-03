namespace Opc.Ua.Edge.Translator
{
    using Microsoft.Win32;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Loader;

    public class DriverLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _hostAssemblyName;

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
                Log.Logger.Information($"Plugin directory not found: {driversRoot}");
                return;
            }

            foreach (var pluginDir in Directory.EnumerateDirectories(driversRoot))
            {
                foreach (var dll in Directory.EnumerateFiles(pluginDir, "*.dll"))
                {
                    try
                    {
                        var alc = new DriverLoadContext(dll);
                        var asm = alc.LoadFromAssemblyPath(dll);

                        var driverTypes = asm.GetTypes().Where(t => !t.IsAbstract && typeof(IProtocolDriver).IsAssignableFrom(t));
                        foreach (var dt in driverTypes)
                        {
                            IProtocolDriver driver = (IProtocolDriver)Activator.CreateInstance(dt)!;
                            Program.Drivers.Register(driver);
                            Log.Logger.Information($"Loaded protocol driver: {driver.Scheme} ({dt.FullName})");
                        }
                    }
                    catch (BadImageFormatException)
                    {
                        // ignore pInvoke/native DLLs that come with the driver
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error(ex, $"Failed to load protocol driver {pluginDir}");
                    }
                }
            }
        }
    }
}
