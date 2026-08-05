namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Xunit;

    /// <summary>
    /// Contract tests applied uniformly to <em>every</em> protocol driver in the
    /// solution.
    /// <para>
    /// The driver-specific tests (see <c>ModbusTCPAssetEntityIntegrationTests</c>)
    /// exercise wire-level behaviour against a protocol mock and cannot be
    /// generalised — there is no shared notion of a "function code" across
    /// Modbus, BACnet, OCPP and Redfish. What <em>is</em> shared is the
    /// <see cref="IProtocolDriver"/> contract every driver must honour for the
    /// registry, onboarding and address-space code to work.
    /// </para>
    /// <para>
    /// Drivers are discovered by reflection rather than listed, so a newly
    /// added driver is covered automatically instead of silently escaping these
    /// checks.
    /// </para>
    /// <para>
    /// Deliberately NOT exercised here: <c>Discover()</c> and
    /// <c>BrowseAndGenerateTD()</c> perform live network / PLC I/O on most
    /// drivers (see e.g. <c>SiemensProtocolDriver.BrowseAndGenerateTD</c>, which
    /// browses a connected PLC). Calling them would make the suite slow and
    /// non-deterministic.
    /// </para>
    /// </summary>
    public class ProtocolDriverContractTests
    {
        /// <summary>
        /// Every concrete <see cref="IProtocolDriver"/> that ships in the
        /// solution, resolved from the referenced driver assemblies.
        /// </summary>
        public static TheoryData<Type> DriverTypes
        {
            get
            {
                TheoryData<Type> data = new();

                foreach (Type type in GetDriverTypes())
                {
                    data.Add(type);
                }

                return data;
            }
        }

        [Fact]
        public void All_referenced_driver_assemblies_are_discovered()
        {
            IReadOnlyList<Type> drivers = GetDriverTypes();

            // Guards the reflection itself: if assembly loading silently failed
            // the theories below would vacuously pass with an empty data set.
            Assert.True(drivers.Count >= 16, $"Expected at least 16 drivers, found {drivers.Count}: {string.Join(", ", drivers.Select(d => d.Name))}");

            // Spot-check a representative spread of the referenced drivers.
            string[] expected =
            [
                "ModbusTCPProtocolDriver",
                "ModbusRTUProtocolDriver",
                "BACNetProtocolDriver",
                "BeckhoffProtocolDriver",
                "SiemensProtocolDriver",
                "RockwellProtocolDriver",
                "MitsubishiProtocolDriver",
                "IEC61850ProtocolDriver",
                "OCPPProtocolDriver",
                "OPCUAProtocolDriver",
                "RedfishProtocolDriver",
                "HTTPClientProtocolDriver",
                "LoRaWANProtocolDriver",
                "MatterProtocolDriver",
                "PIProtocolDriver",
                "MockProtocolDriver"
            ];

            foreach (string name in expected)
            {
                Assert.Contains(drivers, d => d.Name == name);
            }
        }

        [Theory]
        [MemberData(nameof(DriverTypes))]
        public void Driver_can_be_constructed_with_a_parameterless_constructor(Type driverType)
        {
            // DriverLoadContext.LoadProtocolDrivers uses Activator.CreateInstance,
            // so a driver without a usable parameterless constructor would fail
            // to load at runtime rather than at build time.
            Assert.NotNull(driverType.GetConstructor(Type.EmptyTypes));

            IProtocolDriver driver = CreateDriver(driverType);

            Assert.NotNull(driver);
        }

        [Theory]
        [MemberData(nameof(DriverTypes))]
        public void Driver_advertises_a_usable_scheme(Type driverType)
        {
            IProtocolDriver driver = CreateDriver(driverType);

            string scheme = driver.Scheme;

            Assert.False(string.IsNullOrWhiteSpace(scheme), $"{driverType.Name} must advertise a scheme.");

            // The registry matches on the URI scheme, so the value has to be a
            // legal scheme: no whitespace, no separator characters.
            Assert.DoesNotContain(':', scheme);
            Assert.DoesNotContain('/', scheme);
            Assert.Equal(scheme.Trim(), scheme);
            Assert.Equal(scheme.ToLowerInvariant(), scheme);
        }

        [Theory]
        [MemberData(nameof(DriverTypes))]
        public void Driver_advertises_an_absolute_wot_binding_uri(Type driverType)
        {
            IProtocolDriver driver = CreateDriver(driverType);

            string binding = driver.WoTBindingUri;

            Assert.False(string.IsNullOrWhiteSpace(binding), $"{driverType.Name} must advertise a WoT binding URI.");
            Assert.True(
                Uri.TryCreate(binding, UriKind.Absolute, out Uri parsed),
                $"{driverType.Name} WoTBindingUri '{binding}' is not an absolute URI.");
            Assert.Contains(parsed.Scheme, new[] { "http", "https" });
        }

        [Theory]
        [MemberData(nameof(DriverTypes))]
        public void Driver_properties_are_stable_across_reads(Type driverType)
        {
            // Scheme / WoTBindingUri are read repeatedly by the registry and the
            // diagnostics UI; they must be pure.
            IProtocolDriver driver = CreateDriver(driverType);

            Assert.Equal(driver.Scheme, driver.Scheme);
            Assert.Equal(driver.WoTBindingUri, driver.WoTBindingUri);
        }

        [Theory]
        [MemberData(nameof(DriverTypes))]
        public void Driver_resolves_through_the_registry_by_its_own_scheme(Type driverType)
        {
            IProtocolDriver driver = CreateDriver(driverType);

            ProtocolDriverRegistry registry = new();
            registry.Register(driver);

            // The endpoint form the onboarding path actually passes in.
            string endpoint = driver.Scheme + "://device-1:1234/1";

            Assert.True(
                registry.TryGetByUri(endpoint, out IProtocolDriver resolved),
                $"{driverType.Name} does not resolve from its own endpoint '{endpoint}'.");

            Assert.Same(driver, resolved);
        }

        [Theory]
        [MemberData(nameof(DriverTypes))]
        public void Driver_registers_structure_types_without_throwing_for_a_null_thing_description(Type driverType)
        {
            // RegisterStructureTypes is called by UANodeManager during
            // onboarding. Most drivers use the default no-op implementation;
            // those that override it (Rockwell) must tolerate an empty TD
            // rather than throwing mid-onboarding.
            IProtocolDriver driver = CreateDriver(driverType);

            driver.RegisterStructureTypes(null, null);
        }

        [Fact]
        public void Driver_schemes_are_unique_across_all_drivers()
        {
            // Two drivers claiming the same scheme is a real defect: the
            // registry resolves by scheme, so one would silently shadow the
            // other and assets would be onboarded onto the wrong protocol.
            List<(string Scheme, string Driver)> schemes = GetDriverTypes()
                .Select(t => (CreateDriver(t).Scheme, t.Name))
                .ToList();

            string[] duplicates = schemes
                .GroupBy(s => s.Scheme, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} claimed by {string.Join(" and ", g.Select(x => x.Driver))}")
                .ToArray();

            Assert.True(duplicates.Length == 0, "Duplicate driver schemes: " + string.Join("; ", duplicates));
        }

        [Fact]
        public void Registry_resolves_every_driver_when_they_are_all_registered_together()
        {
            // The production registry holds every loaded driver at once, so
            // resolution has to stay correct in the presence of all the others
            // (e.g. "modbus+tcp" vs "modbus+rtu", "http" vs "https").
            ProtocolDriverRegistry registry = new();
            List<IProtocolDriver> drivers = GetDriverTypes().Select(CreateDriver).ToList();

            foreach (IProtocolDriver driver in drivers)
            {
                registry.Register(driver);
            }

            foreach (IProtocolDriver driver in drivers)
            {
                Assert.True(
                    registry.TryGetByUri(driver.Scheme + "://device-1:1234/1", out IProtocolDriver resolved),
                    $"{driver.GetType().Name} did not resolve once every driver was registered.");

                Assert.Equal(driver.Scheme, resolved.Scheme);
            }
        }

        private static IProtocolDriver CreateDriver(Type driverType)
        {
            return (IProtocolDriver)Activator.CreateInstance(driverType);
        }

        private static IReadOnlyList<Type> GetDriverTypes()
        {
            EnsureDriverAssembliesLoaded();

            List<Type> drivers = new();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;

                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // A partially loadable assembly still yields its good types.
                    types = ex.Types.Where(t => t is not null).ToArray();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type.IsAbstract || type.IsInterface || !typeof(IProtocolDriver).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    // Test doubles live in the test assembly; only ship-able
                    // drivers are under contract here.
                    if (type.Assembly == typeof(ProtocolDriverContractTests).Assembly)
                    {
                        continue;
                    }

                    drivers.Add(type);
                }
            }

            return drivers.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Loads every driver assembly sitting next to the test assembly.
        /// <para>
        /// A <c>ProjectReference</c> copies the driver DLL to the output folder
        /// but the CLR only loads it when a type from it is first touched, so
        /// reflecting over <c>AppDomain.CurrentDomain.GetAssemblies()</c> alone
        /// would see only the two or three assemblies already in use. This
        /// mirrors what <c>DriverLoadContext</c> does in production: enumerate
        /// the DLLs on disk and load them.
        /// </para>
        /// </summary>
        private static void EnsureDriverAssembliesLoaded()
        {
            string directory = Path.GetDirectoryName(typeof(ProtocolDriverContractTests).Assembly.Location);

            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.dll"))
            {
                try
                {
                    AssemblyName name = AssemblyName.GetAssemblyName(file);

                    if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name.Name))
                    {
                        continue;
                    }

                    Assembly.Load(name);
                }
                catch (Exception)
                {
                    // Native or non-managed DLLs sit in the same folder; skip
                    // anything that is not a loadable managed assembly.
                }
            }
        }
    }
}
