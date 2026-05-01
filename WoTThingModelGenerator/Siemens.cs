namespace Opc.Ua.Edge.Translator.Tools
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Property = Opc.Ua.Edge.Translator.Models.Property;

#if SIEMENS_ENGINEERING
    using Siemens.Engineering;
    using Siemens.Engineering.HW;
    using Siemens.Engineering.HW.Features;
    using Siemens.Engineering.SW;
    using Siemens.Engineering.SW.Blocks;
#endif

#nullable disable

    /// <summary>
    /// Imports a TIA Portal project via the Openness API and emits a WoT
    /// Thing Model containing one Property + S7Form per leaf interface
    /// member of every standard-access (non-optimized) data block.
    ///
    /// Requires:
    /// - TIA Portal V21 installed locally,
    /// - The current Windows user to be a member of the
    ///   "Siemens TIA Openness" group,
    /// - The project must be runnable in TIA V21 (older projects must be
    ///   migrated first).
    ///
    /// Optimized blocks are skipped because S7Comm classic cannot address
    /// individual variables inside them.
    /// </summary>
    internal static class SiemensTIAImporter
    {
        // Default install root for TIA Portal V21. Override via env var
        // SIEMENS_TIA_PATH if the user installed it elsewhere.
        private const string DefaultTiaPath = @"C:\Program Files\Siemens\Automation\Portal V21";
        private const string OpennessApiSubPath = @"PublicAPI\V21";

        public static void Register()
        {
#if SIEMENS_ENGINEERING
            string tiaRoot = Environment.GetEnvironmentVariable("SIEMENS_TIA_PATH") ?? DefaultTiaPath;
            string apiDir = Path.Combine(tiaRoot, OpennessApiSubPath);

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                AssemblyName requested = new(args.Name);
                if (!requested.Name.StartsWith("Siemens.Engineering", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string candidate = Path.Combine(apiDir, requested.Name + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            };
#endif
        }

        public static void Import(string filename)
        {
#if SIEMENS_ENGINEERING
            Console.WriteLine($"Opening TIA Portal project: {filename}");

            using TiaPortal tia = new(TiaPortalMode.WithoutUserInterface);
            Project project;
            try
            {
                project = tia.Projects.Open(new FileInfo(filename));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open TIA project '{filename}': {ex.Message}");
                return;
            }

            try
            {
                foreach (Device device in project.Devices)
                {
                    (PlcSoftware plcSoftware, DeviceItem cpuItem) = FindPlcSoftware(device);
                    if (plcSoftware == null)
                    {
                        continue;
                    }

                    string plcName = plcSoftware.Name;
                    string ipAddress = TryGetIpAddress(device) ?? "{{address}}";
                    (int rack, int slot) = TryGetRackAndSlot(cpuItem);
                    Console.WriteLine($"  PLC '{plcName}' @ {ipAddress} (rack {rack}, slot {slot})");

                    ThingDescription td = new()
                    {
                        Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                        Id = "urn:" + plcName,
                        SecurityDefinitions = new SecurityDefinitions { NosecSc = new NosecSc { Scheme = "nosec" } },
                        Security = new string[1] { "nosec_sc" },
                        Type = new string[1] { "tm:ThingModel" },
                        Name = "{{name}}",
                        Base = $"s7://{ipAddress}:{rack}:{slot}",
                        Title = plcName,
                        Properties = new Dictionary<string, Property>(),
                        Actions = new Dictionary<string, TDAction>()
                    };

                    foreach (DataBlock db in EnumerateDataBlocks(plcSoftware.BlockGroup))
                    {
                        AddDataBlock(db, td.Properties);
                    }

                    if (td.Properties.Count == 0)
                    {
                        Console.WriteLine($"  No accessible (standard-access) DBs found for '{plcName}', skipping.");
                        continue;
                    }

                    string outputPath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        Path.GetFileNameWithoutExtension(filename) + "_" + plcName + ".tm.jsonld");

                    File.WriteAllText(outputPath, JsonConvert.SerializeObject(td, Formatting.Indented));
                    Console.WriteLine($"  Wrote {td.Properties.Count} properties to {outputPath}");
                }
            }
            finally
            {
                project.Close();
            }
#else
            Console.WriteLine(
                $"TIA Portal Openness was not available at build time, skipping '{filename}'. " +
                "Install TIA Portal V21 and rebuild with /p:SiemensTIAPortalPath=...");
#endif
        }

#if SIEMENS_ENGINEERING
        private static (PlcSoftware plc, DeviceItem cpuItem) FindPlcSoftware(Device device)
        {
            foreach (DeviceItem item in EnumerateDeviceItems(device))
            {
                SoftwareContainer container = item.GetService<SoftwareContainer>();
                if (container?.Software is PlcSoftware plc)
                {
                    return (plc, item);
                }
            }

            return (null, null);
        }

        private static (int rack, int slot) TryGetRackAndSlot(DeviceItem cpuItem)
        {
            // For S7-1200/1500 the CPU is virtually placed at rack 0, slot 1.
            // For S7-300/400 the CPU lives in a real rack ("Rail_0", "UR", ...)
            // and its slot depends on the project layout (typically slot 2,
            // sometimes preceded by a power supply at slot 1).
            //
            // TIA Openness exposes the slot as the CPU DeviceItem's
            // PositionNumber attribute, and the rack as the PositionNumber of
            // its parent DeviceItem (the rack/rail). We fall back to the
            // S7-1200/1500 default of (0, 1) if any of these attributes is
            // unavailable.
            int slot = TryGetIntAttribute(cpuItem, "PositionNumber") ?? 1;
            int rack = 0;

            IEngineeringObject parent = cpuItem.Parent;
            while (parent != null)
            {
                if (parent is DeviceItem parentItem)
                {
                    int? maybeRack = TryGetIntAttribute(parentItem, "PositionNumber");
                    if (maybeRack.HasValue)
                    {
                        rack = maybeRack.Value;
                        break;
                    }
                }
                else
                {
                    break;
                }

                parent = parent.Parent;
            }

            return (rack, slot);
        }

        private static int? TryGetIntAttribute(IEngineeringObject obj, string name)
        {
            object value = SafeGetAttribute(obj, name);
            if (value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<DeviceItem> EnumerateDeviceItems(Device device)
        {
            foreach (DeviceItem top in device.DeviceItems)
            {
                yield return top;
                foreach (DeviceItem child in EnumerateDeviceItems(top))
                {
                    yield return child;
                }
            }
        }

        private static IEnumerable<DeviceItem> EnumerateDeviceItems(DeviceItem item)
        {
            foreach (DeviceItem child in item.DeviceItems)
            {
                yield return child;
                foreach (DeviceItem grandChild in EnumerateDeviceItems(child))
                {
                    yield return grandChild;
                }
            }
        }

        private static string TryGetIpAddress(Device device)
        {
            // The IP address lives on the PROFINET network interface of the
            // CPU's communication module. We probe every device item for a
            // NetworkInterface service and return the first IPv4 address found.
            foreach (DeviceItem item in EnumerateDeviceItems(device))
            {
                NetworkInterface ni;
                try
                {
                    ni = item.GetService<NetworkInterface>();
                }
                catch
                {
                    continue;
                }

                if (ni == null)
                {
                    continue;
                }

                foreach (Node node in ni.Nodes)
                {
                    object addr = null;
                    try { addr = node.GetAttribute("Address"); } catch { }
                    if (addr is string s && !string.IsNullOrWhiteSpace(s))
                    {
                        return s;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<DataBlock> EnumerateDataBlocks(PlcBlockGroup group)
        {
            foreach (PlcBlock block in group.Blocks)
            {
                if (block is DataBlock db)
                {
                    yield return db;
                }
            }

            foreach (PlcBlockUserGroup sub in group.Groups)
            {
                foreach (DataBlock db in EnumerateDataBlocks(sub))
                {
                    yield return db;
                }
            }
        }

        private static void AddDataBlock(DataBlock db, Dictionary<string, Property> properties)
        {
            // S7-1500 / 1200 default to optimized layout. Without standard
            // (non-optimized) layout the byte/bit offsets are not stable
            // and S7Comm classic cannot read individual variables.
            object layout = SafeGetAttribute(db, "MemoryLayout");
            string layoutStr = layout?.ToString() ?? string.Empty;
            if (!string.Equals(layoutStr, "Standard", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    DB '{db.Name}' (#{db.Number}) skipped — layout is {layoutStr}, not Standard.");
                return;
            }

            int dbNumber = db.Number;
            Console.WriteLine($"    DB '{db.Name}' (#{dbNumber})");

            foreach (Member member in db.Interface.Members)
            {
                WalkMember(member, db.Name, dbNumber, properties);
            }
        }

        private static void WalkMember(Member member, string pathPrefix, int dbNumber, Dictionary<string, Property> properties)
        {
            string memberName = member.Name;
            string fullPath = string.IsNullOrEmpty(pathPrefix) ? memberName : pathPrefix + "." + memberName;
            string dataType = SafeGetAttribute(member, "DataTypeName") as string ?? string.Empty;

            // Nested struct / UDT members have their own Members composition.
            if (member.Members != null && member.Members.Count > 0)
            {
                foreach (Member child in member.Members)
                {
                    WalkMember(child, fullPath, dbNumber, properties);
                }

                return;
            }

            (int byteOffset, int bitOffset) = ParseOffset(SafeGetAttribute(member, "Offset"));
            (TypeString tdType, TypeEnum typeEnum, int sizeBytes, int maxLen) = MapType(dataType);

            if (sizeBytes == 0)
            {
                Console.WriteLine($"      {fullPath} ({dataType}) skipped — unsupported S7 type.");
                return;
            }

            S7Form form = new()
            {
                Href = $"DB{dbNumber}?{byteOffset}",
                Op = new[] { Op.Readproperty, Op.Observeproperty },
                PollingTime = 1000,
                S7Target = S7Target.DataBlock,
                S7DBNumber = dbNumber,
                S7Start = byteOffset,
                S7Pos = bitOffset,
                S7Size = sizeBytes,
                S7MaxLen = maxLen,
                Type = tdType
            };

            Property property = new()
            {
                Type = typeEnum,
                ReadOnly = false,
                Observable = true,
                Forms = new object[1] { form }
            };

            string propertyKey = fullPath.Replace('.', '_');
            if (!properties.ContainsKey(propertyKey))
            {
                properties.Add(propertyKey, property);
                Console.WriteLine($"      {fullPath}: {dataType} @ DB{dbNumber}.{byteOffset}.{bitOffset} ({sizeBytes} B)");
            }
        }

        private static object SafeGetAttribute(IEngineeringObject obj, string name)
        {
            try
            {
                return obj.GetAttribute(name);
            }
            catch
            {
                return null;
            }
        }

        private static (int byteOffset, int bitOffset) ParseOffset(object offsetAttr)
        {
            // Openness exposes the offset as a string in "byte.bit" form on
            // standard-access blocks (e.g. "0.0", "2.0", "6.4"). On
            // optimized blocks the attribute is missing; we already filter
            // those out before reaching here.
            if (offsetAttr is string s && !string.IsNullOrEmpty(s))
            {
                int dot = s.IndexOf('.');
                if (dot < 0)
                {
                    return (int.TryParse(s, out int b) ? b : 0, 0);
                }

                _ = int.TryParse(s.AsSpan(0, dot), out int byteOff);
                _ = int.TryParse(s.AsSpan(dot + 1), out int bitOff);
                return (byteOff, bitOff);
            }

            return (0, 0);
        }

        private static (TypeString tdType, TypeEnum typeEnum, int sizeBytes, int maxLen) MapType(string s7Type)
        {
            // s7Type can be e.g. "Bool", "Int", "DInt", "Real", "LReal",
            // "String", "String[80]", "WString[64]", "DTL", "Char", ...
            // Arrays and UDTs are not addressable as a single S7Form here;
            // callers should drill into them via Member.Members instead.
            if (string.IsNullOrEmpty(s7Type))
            {
                return (TypeString.String, TypeEnum.String, 0, 0);
            }

            string baseType = s7Type;
            int len = 0;
            int bracket = s7Type.IndexOf('[');
            if (bracket > 0)
            {
                baseType = s7Type.Substring(0, bracket);
                int close = s7Type.IndexOf(']', bracket);
                if (close > bracket)
                {
                    _ = int.TryParse(s7Type.AsSpan(bracket + 1, close - bracket - 1), out len);
                }
            }

            switch (baseType.Trim().ToUpperInvariant())
            {
                case "BOOL":
                    return (TypeString.Boolean, TypeEnum.Boolean, 1, 0);
                case "BYTE":
                case "SINT":
                case "USINT":
                case "CHAR":
                    return (TypeString.Byte, TypeEnum.Integer, 1, 0);
                case "WORD":
                case "INT":
                case "UINT":
                    return (TypeString.Short, TypeEnum.Integer, 2, 0);
                case "DWORD":
                case "DINT":
                case "UDINT":
                    return (TypeString.Integer, TypeEnum.Integer, 4, 0);
                case "REAL":
                    return (TypeString.Float, TypeEnum.Number, 4, 0);
                case "LREAL":
                    return (TypeString.Double, TypeEnum.Number, 8, 0);
                case "STRING":
                    if (len <= 0) len = 254;
                    return (TypeString.String, TypeEnum.String, len + 2, len);
                case "WSTRING":
                    if (len <= 0) len = 254;
                    return (TypeString.String, TypeEnum.String, (len + 2) * 2, len);
                case "DTL":
                    return (TypeString.String, TypeEnum.String, 12, 0);
                default:
                    return (TypeString.String, TypeEnum.String, 0, 0);
            }
        }
#endif
    }
}
