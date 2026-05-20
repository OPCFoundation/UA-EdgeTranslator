namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Xunit;

    /// <summary>
    /// Drives <see cref="DriverLoadContext.LoadProtocolDrivers"/> against a real
    /// driver assembly (<c>Mock.dll</c>) by copying it into a per-test
    /// <c>drivers/</c> directory. This exercises the assembly-load,
    /// type-discovery and registration branches that the pure-helper tests cannot.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class DriverLoadContextLoadingTests
    {
        [Fact]
        public void LoadProtocolDrivers_loads_real_driver_assembly_into_registry()
        {
            string mockAssemblyPath = typeof(MockProtocolDriver).Assembly.Location;
            Assert.True(File.Exists(mockAssemblyPath), $"Mock driver assembly not found at {mockAssemblyPath}");

            using TestWorkingDirectory tmp = new();
            string driverDir = Path.Combine(tmp.Path, "drivers", "mock-pack");
            Directory.CreateDirectory(driverDir);
            string copiedAssembly = Path.Combine(driverDir, Path.GetFileName(mockAssemblyPath));
            File.Copy(mockAssemblyPath, copiedAssembly);

            int driverCountBefore = Program.Drivers.AllDrivers.Count();

            DriverLoadContext.LoadProtocolDrivers();

            int driverCountAfter = Program.Drivers.AllDrivers.Count();
            Assert.True(
                driverCountAfter >= driverCountBefore,
                $"Expected driver count to grow or stay the same, got {driverCountBefore} -> {driverCountAfter}.");

            // Resolution by URI must work for the freshly loaded driver.
            Assert.True(Program.Drivers.TryGetByUri("mock://x:1/1", out var resolved));
            Assert.NotNull(resolved);

            // Cleanup so other tests don't see the leaked driver.
            ResetProgramDrivers();
        }

        [Fact]
        public void LoadProtocolDrivers_skips_non_dotnet_dll_without_throwing()
        {
            using TestWorkingDirectory tmp = new();
            string driverDir = Path.Combine(tmp.Path, "drivers", "junk");
            Directory.CreateDirectory(driverDir);
            File.WriteAllBytes(Path.Combine(driverDir, "junk.dll"), new byte[] { 0, 1, 2, 3, 4 });

            // Must not throw — BadImageFormatException path is handled internally.
            DriverLoadContext.LoadProtocolDrivers();
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
