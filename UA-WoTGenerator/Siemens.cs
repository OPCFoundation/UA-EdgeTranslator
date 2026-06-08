namespace Opc.Ua.Edge.Translator.Tools
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Security;
    using System.Xml.Linq;
    using Property = Models.Property;

#if SIEMENS_ENGINEERING
    using Siemens.Engineering;
    using Siemens.Engineering.HW;
    using Siemens.Engineering.HW.Features;
    using Siemens.Engineering.SW;
    using Siemens.Engineering.SW.Blocks;
    using Siemens.Engineering.SW.Types;
    using System.Linq;
#endif

    /// <summary>
    /// Imports a TIA Portal project via the Openness API and emits a WoT
    /// Thing Description containing one Property + S7Form per leaf interface
    /// member of every standard-access (non-optimized) data block.
    ///
    /// Unlike the Thing Model produced by the other importers, the output
    /// here is a concrete Thing Description: the PLC's name, IP address,
    /// rack and slot are baked into the document at import time, so it can
    /// be consumed directly without a templating step.
    ///
    /// Requires:
    /// - TIA Portal Openness V15.1 or newer installed locally (V15.1, V16,
    ///   V17, V18, V19, V20 and V21 have all been validated against this
    ///   importer),
    /// - The current Windows user to be a member of the
    ///   "Siemens TIA Openness" group,
    /// - The project must open without migration in the installed TIA
    ///   version (a V16 install can only open V16 projects, etc.).
    ///
    /// Both modern S7-1200/1500 and classic S7-300/400 stations are
    /// supported. Classic stations always use standard-access DBs, so the
    /// optimized-layout filter is a no-op for them; rack/slot are picked up
    /// from the CPU's PositionNumber and its parent rack's PositionNumber.
    ///
    /// Optimized blocks are skipped because S7Comm classic cannot address
    /// individual variables inside them.
    /// </summary>
    internal static class SiemensTIAImporter
    {
        // Root that contains the per-version "Portal V<n>" install folders.
        // Override the install root entirely via env var SIEMENS_TIA_PATH
        // (point it at e.g. "C:\Program Files\Siemens\Automation\Portal V16",
        // or "...\Portal V15_1" for TIA V15.1 — Siemens uses an underscore
        // in the V15.1 folder name).
        private const string DefaultSiemensAutomationRoot = @"C:\Program Files\Siemens\Automation";

        public static void Register()
        {
            string[] apiDirs = ResolveOpennessApiDirectories();

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                AssemblyName requested = new AssemblyName(args.Name);
                if (!requested.Name.StartsWith("Siemens.Engineering", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                foreach (string apiDir in apiDirs)
                {
                    string candidate = Path.Combine(apiDir, requested.Name + ".dll");
                    if (File.Exists(candidate))
                    {
                        return Assembly.LoadFrom(candidate);
                    }
                }

                return null;
            };
        }

        /// <summary>
        /// Returns the candidate Openness PublicAPI folders to probe, in
        /// preference order. The same binary therefore works against any
        /// installed TIA version from V15.1 onwards (V15.1 ships .NET
        /// 4.6.2 assemblies and V16/V17 ship .NET 4.7.2 assemblies under
        /// PublicAPI\V&lt;n&gt;, V18+ ship .NET 4.8 assemblies under
        /// PublicAPI\V&lt;n&gt;\net48).
        /// </summary>
        private static string[] ResolveOpennessApiDirectories()
        {
            List<string> dirs = new List<string>();

            // 1) Explicit override: a single "Portal V<n>" folder.
            string overrideRoot = Environment.GetEnvironmentVariable("SIEMENS_TIA_PATH");
            if (!string.IsNullOrWhiteSpace(overrideRoot) && Directory.Exists(overrideRoot))
            {
                AddPublicApiSubdirs(overrideRoot, dirs);
            }

            // 2) Auto-discover every "Portal V<n>" under the default root.
            //    Sort descending so newer installs are tried first.
            if (Directory.Exists(DefaultSiemensAutomationRoot))
            {
                string[] portalDirs = Directory.GetDirectories(DefaultSiemensAutomationRoot, "Portal V*");
                Array.Sort(portalDirs, (a, b) => string.Compare(b, a, StringComparison.OrdinalIgnoreCase));
                foreach (string portalDir in portalDirs)
                {
                    AddPublicApiSubdirs(portalDir, dirs);
                }
            }

            return dirs.ToArray();
        }

        private static void AddPublicApiSubdirs(string portalDir, List<string> dirs)
        {
            string publicApi = Path.Combine(portalDir, "PublicAPI");
            if (!Directory.Exists(publicApi))
            {
                return;
            }

            // V15.1/V16/V17 layout: PublicAPI\V<n>\Siemens.Engineering.dll (net462/net472)
            // V18+        layout: PublicAPI\V<n>\net48\Siemens.Engineering.dll
            foreach (string versionDir in Directory.GetDirectories(publicApi, "V*"))
            {
                string net48 = Path.Combine(versionDir, "net48");
                if (Directory.Exists(net48))
                {
                    dirs.Add(net48);
                }

                dirs.Add(versionDir);
            }
        }

        public static void Import(string filename)
        {
#if SIEMENS_ENGINEERING
            Console.WriteLine($"Opening TIA Portal project: {filename}");

            // UMAC (Project User Management) credentials. TIA refuses to open a
            // project that has the "Project User Management" enabled without
            // valid credentials, so we read them from the environment instead of
            // baking them into the binary or putting them on the command line:
            //
            //   SIEMENS_TIA_USERNAME = <user defined in the TIA project>
            //   SIEMENS_TIA_PASSWORD = <password for that user>
            //
            // When both are present we open the project via the UmacDelegate
            // overload; otherwise we fall through to the legacy unprotected
            // open, preserving today's behaviour for unprotected projects.
            string umacUser = Environment.GetEnvironmentVariable("SIEMENS_TIA_USERNAME");
            string umacPassword = Environment.GetEnvironmentVariable("SIEMENS_TIA_PASSWORD");
            bool useUmac = !string.IsNullOrEmpty(umacUser) && !string.IsNullOrEmpty(umacPassword);

            TiaPortal tia = new TiaPortal(TiaPortalMode.WithoutUserInterface);
            Project project;
            try
            {
                if (useUmac)
                {
                    Console.WriteLine($"  Authenticating as TIA user '{umacUser}'.");
                    using (SecureString securePassword = ToSecureString(umacPassword))
                    {
                        project = tia.Projects.Open(new FileInfo(filename), credentials =>
                        {
                            // The UmacDelegate is invoked by TIA Openness whenever
                            // the project has User Management enabled. The caller
                            // must populate the Name and password on the
                            // UmacUserCredentials object passed in. The same
                            // delegate is also invoked for UmacApplicationCredentials
                            // (auto-logon via project-defined application identifier),
                            // which we don't support here.
                            if (credentials is UmacUserCredentials userCreds)
                            {
                                userCreds.Name = umacUser;
                                userCreds.Conceal(securePassword);
                            }
                            else
                            {
                                Console.WriteLine(
                                    "  Warning: this TIA project requested credentials of an " +
                                    $"unexpected type ({credentials?.GetType().FullName}); " +
                                    "open will likely fail.");
                            }
                        });
                    }
                }
                else
                {
                    project = tia.Projects.Open(new FileInfo(filename));
                }
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

                    string ipAddress = TryGetIpAddress(device) ?? "{{address}}";
                    (int rack, int slot) = TryGetRackAndSlot(cpuItem);
                    Console.WriteLine($"  PLC '{device.Name}.{cpuItem.Name}' @ {ipAddress} (rack {rack}, slot {slot})");

                    // The UA Edge Translator's IsSafeAssetName accepts only
                    // letters, digits, '.', '_' and '-' (and rejects a leading
                    // dot). TIA device names commonly contain '/' (e.g.
                    // "S7-1500/ET200MP station_1") and spaces, both of which
                    // would cause the generated TD to be silently skipped by
                    // UANodeManager.LoadLocalWoTFilesAsync. Sanitize once and
                    // reuse for Id, Name and the output file name.
                    string assetName = SanitizeAssetName(device.Name + "." + cpuItem.Name);

                    // S7Comm base URL: only emit the rack component. TIA's
                    // CPU PositionNumber (typically 1 for an S7-1500) is the
                    // *engineering* rack slot and is NOT the same as Sharp7's
                    // "slot" connection parameter (which feeds the TSAP and
                    // is 0 for S7-1200/1500). SiemensProtocolDriver.ParseEndpoint
                    // therefore treats "s7://ip:n" as (rack 0, slot n), which
                    // matches what Sharp7 expects when n = 0. Baking a third
                    // ":slot" component would mis-route the connection, so the
                    // generator only emits "s7://ip:{rack}".
                    string baseUrl = $"s7://{ipAddress}:{rack}";

                    ThingDescription td = new ThingDescription()
                    {
                        Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                        Id = "urn:" + assetName,
                        SecurityDefinitions = new SecurityDefinitions { NosecSc = new NosecSc { Scheme = "nosec" } },
                        Security = new string[1] { "nosec_sc" },
                        Type = new string[1] { "Thing" },
                        Name = assetName,
                        Base = baseUrl,
                        Title = plcSoftware.Name,
                        Properties = new Dictionary<string, Property>(),
                        Actions = new Dictionary<string, TDAction>()
                    };

                    // Export every PLC type (UDT) exactly once and keep a
                    // name -> <Sections> catalog so that UDT-typed members
                    // inside DBs can be expanded inline below.
                    Dictionary<string, XElement> udtCatalog = BuildUdtCatalog(plcSoftware);

                    foreach (DataBlock db in EnumerateDataBlocks(plcSoftware.BlockGroup))
                    {
                        AddDataBlock(db, udtCatalog, td.Properties);
                    }

                    if (td.Properties.Count == 0)
                    {
                        Console.WriteLine($"  No accessible (standard-access) DBs found for '{plcSoftware.Name}', skipping.");
                        continue;
                    }

                    string outputPath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        SanitizeAssetName(Path.GetFileNameWithoutExtension(filename) + "_" + plcSoftware.Name) + ".td.jsonld");

                    File.WriteAllText(outputPath, JsonConvert.SerializeObject(td, Formatting.Indented));
                    Console.WriteLine($"  Wrote {td.Properties.Count} properties to {outputPath}");
                }
            }
            finally
            {
                project.Close();
                tia.Dispose();
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

        /// <summary>
        /// Copies the given clear-text password into a fresh
        /// <see cref="SecureString"/> so it can be handed to the TIA
        /// Openness <c>UmacUserCredentials.Conceal</c> API. The caller is
        /// responsible for disposing the returned instance.
        /// </summary>
        private static SecureString ToSecureString(string clearText)
        {
            SecureString secure = new SecureString();
            if (!string.IsNullOrEmpty(clearText))
            {
                foreach (char c in clearText)
                {
                    secure.AppendChar(c);
                }
            }

            secure.MakeReadOnly();
            return secure;
        }

        /// <summary>
        /// Replaces every character that the UA Edge Translator's
        /// IsSafeAssetName check would reject (anything outside letters,
        /// digits, '.', '_' and '-') with '_', and prefixes a leading dot
        /// with '_' so the result is also a valid Unix file name. Empty
        /// input falls back to "Asset".
        /// </summary>
        private static string SanitizeAssetName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "Asset";
            }

            char[] chars = raw.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                {
                    chars[i] = '_';
                }
            }

            string sanitized = new string(chars);
            if (sanitized[0] == '.')
            {
                sanitized = "_" + sanitized;
            }

            return sanitized;
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
            //
            // Note: non-Ethernet interfaces (PROFIBUS DP, MPI, ...) also
            // surface a NetworkInterface service, but their Node.Address is
            // a small integer station number (e.g. "2" for a PROFIBUS slave).
            // On many CPUs those interfaces are enumerated before the
            // Ethernet one, so we must explicitly require a valid IPv4
            // address rather than accepting the first non-empty string.
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
                    if (addr is string s
                        && !string.IsNullOrWhiteSpace(s)
                        && System.Net.IPAddress.TryParse(s, out System.Net.IPAddress parsed)
                        && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && s.Contains('.'))
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

        /// <summary>
        /// Builds a name -&gt; &lt;Sections&gt; lookup of every PLC user data
        /// type (UDT) in the project. The export is done once per UDT and
        /// reused while walking every DB, so the cost is paid up-front.
        /// </summary>
        private static Dictionary<string, XElement> BuildUdtCatalog(PlcSoftware plc)
        {
            Dictionary<string, XElement> catalog = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);

            try
            {
                CollectUdts(plc.TypeGroup, catalog);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Failed to enumerate PLC user types: {ex.Message}");
            }

            return catalog;
        }

        private static void CollectUdts(PlcTypeGroup group, Dictionary<string, XElement> catalog)
        {
            foreach (PlcType type in group.Types)
            {
                XElement sections = ExportTypeSections(type);
                if (sections != null && !string.IsNullOrEmpty(type.Name))
                {
                    catalog[type.Name] = sections;
                }
            }

            foreach (PlcTypeUserGroup sub in group.Groups)
            {
                CollectUdts(sub, catalog);
            }
        }

        private static XElement ExportTypeSections(PlcType type)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "tia_udt_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                type.Export(new FileInfo(tempFile), ExportOptions.WithDefaults);
                XDocument doc = XDocument.Load(tempFile);
                return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Sections");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Failed to export UDT '{type.Name}': {ex.Message}");
                return null;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Best-effort cleanup; ignore.
                }
            }
        }

        private static void AddDataBlock(DataBlock db, Dictionary<string, XElement> udtCatalog, Dictionary<string, Property> properties)
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

            // The live Member objects from db.Interface.Members do not expose
            // an "Offset" attribute on every Openness version (it raises
            // EngineeringNotSupportedException for struct containers and for
            // some leaf members). To stay version-independent we export the
            // DB to SimaticML and walk its <Sections>/<Member> tree, computing
            // the byte/bit offsets ourselves using S7 alignment rules — those
            // are deterministic for standard-access (non-optimized) blocks.
            string tempFile = Path.Combine(Path.GetTempPath(), "tia_db_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                db.Export(new FileInfo(tempFile), ExportOptions.WithDefaults);

                XDocument doc = XDocument.Load(tempFile);

                XElement sections = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Sections");
                if (sections == null)
                {
                    Console.WriteLine($"      DB '{db.Name}' has no <Sections> in its export — skipping.");
                    return;
                }

                XNamespace ns = sections.GetDefaultNamespace();
                XElement staticSection = sections.Elements(ns + "Section")
                    .FirstOrDefault(s => string.Equals((string)s.Attribute("Name"), "Static", StringComparison.OrdinalIgnoreCase));
                if (staticSection == null)
                {
                    Console.WriteLine($"      DB '{db.Name}' has no 'Static' section — skipping.");
                    return;
                }

                Cursor cursor = new Cursor();
                foreach (XElement memberEl in staticSection.Elements(ns + "Member"))
                {
                    WalkMemberXml(memberEl, ns, db.Name, dbNumber, ref cursor, udtCatalog, properties);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      Failed to import DB '{db.Name}': {ex.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Best-effort cleanup; ignore.
                }
            }
        }

        /// <summary>
        /// Tracks the current write position inside a DB while walking its
        /// declared interface. <see cref="Bit"/> is only ever non-zero
        /// immediately after one or more BOOL members have been emitted —
        /// every other type forces a re-alignment to a whole byte (and to
        /// an even byte for any type wider than one byte).
        /// </summary>
        private struct Cursor
        {
            public int Byte;
            public int Bit;
        }

        private static void AlignToByte(ref Cursor cursor)
        {
            if (cursor.Bit != 0)
            {
                cursor.Byte++;
                cursor.Bit = 0;
            }
        }

        private static void AlignToWord(ref Cursor cursor)
        {
            AlignToByte(ref cursor);
            if ((cursor.Byte & 1) != 0)
            {
                cursor.Byte++;
            }
        }

        private static void WalkMemberXml(
            XElement memberEl,
            XNamespace ns,
            string pathPrefix,
            int dbNumber,
            ref Cursor cursor,
            Dictionary<string, XElement> udtCatalog,
            Dictionary<string, Property> properties)
        {
            string memberName = (string)memberEl.Attribute("Name") ?? string.Empty;
            string dataType = (string)memberEl.Attribute("Datatype") ?? string.Empty;
            string fullPath = string.IsNullOrEmpty(pathPrefix) ? memberName : pathPrefix + "." + memberName;

            // Inline STRUCTs are expanded as nested <Member> children in the
            // SimaticML export. UDT references appear with a Datatype of
            // "MyUdt" or "\"MyUdt\"" and have no children — we expand them
            // from the UDT catalog instead.
            List<XElement> children = memberEl.Elements(ns + "Member").ToList();
            if (children.Count > 0 || string.Equals(dataType, "Struct", StringComparison.OrdinalIgnoreCase))
            {
                AlignToWord(ref cursor);
                foreach (XElement child in children)
                {
                    WalkMemberXml(child, ns, fullPath, dbNumber, ref cursor, udtCatalog, properties);
                }

                // Structs are always padded to an even byte boundary.
                AlignToWord(ref cursor);
                return;
            }

            (bool isArray, int low, int high, string elementType) = TryParseArray(dataType);
            if (isArray)
            {
                string udtElement = StripUdtQuotes(elementType);
                if (udtCatalog != null && udtCatalog.TryGetValue(udtElement, out XElement udtArraySections))
                {
                    EmitUdtArray(fullPath, dbNumber, ref cursor, low, high, udtArraySections, udtCatalog, properties);
                    return;
                }

                EmitArrayWithCursor(fullPath, dbNumber, ref cursor, low, high, elementType, dataType, properties);
                return;
            }

            // Scalar UDT reference — expand inline from the catalog.
            string udtName = StripUdtQuotes(dataType);
            if (udtCatalog != null && udtCatalog.TryGetValue(udtName, out XElement udtSections))
            {
                EmitUdtInstance(fullPath, dbNumber, ref cursor, udtSections, udtCatalog, properties);
                return;
            }

            EmitScalarWithCursor(fullPath, dbNumber, ref cursor, dataType, properties);
        }

        /// <summary>
        /// TIA serializes UDT-typed members as <c>"MyUdt"</c> (with the
        /// quotes embedded in the Datatype attribute). Strip them so the
        /// name matches the catalog key.
        /// </summary>
        private static string StripUdtQuotes(string datatype)
        {
            if (string.IsNullOrEmpty(datatype))
            {
                return datatype;
            }

            string s = datatype.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                s = s.Substring(1, s.Length - 2);
            }

            return s;
        }

        private static void EmitUdtInstance(
            string fullPath,
            int dbNumber,
            ref Cursor cursor,
            XElement udtSections,
            Dictionary<string, XElement> udtCatalog,
            Dictionary<string, Property> properties)
        {
            // A UDT instance behaves like an inline STRUCT: align to a word,
            // walk its "None"/"Static" section's members, then pad to a word.
            AlignToWord(ref cursor);

            XNamespace ns = udtSections.GetDefaultNamespace();
            XElement section = udtSections.Elements(ns + "Section")
                .FirstOrDefault(s =>
                {
                    string n = (string)s.Attribute("Name");
                    return string.Equals(n, "None", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(n, "Static", StringComparison.OrdinalIgnoreCase);
                })
                ?? udtSections.Elements(ns + "Section").FirstOrDefault();

            if (section == null)
            {
                Console.WriteLine($"      {fullPath}: UDT has no usable <Section> — skipping. Subsequent offsets may be incorrect.");
                return;
            }

            foreach (XElement child in section.Elements(ns + "Member"))
            {
                WalkMemberXml(child, ns, fullPath, dbNumber, ref cursor, udtCatalog, properties);
            }

            AlignToWord(ref cursor);
        }

        private static void EmitUdtArray(
            string fullPath,
            int dbNumber,
            ref Cursor cursor,
            int low,
            int high,
            XElement udtSections,
            Dictionary<string, XElement> udtCatalog,
            Dictionary<string, Property> properties)
        {
            int count = high - low + 1;
            if (count <= 0)
            {
                Console.WriteLine($"      {fullPath} skipped — invalid UDT array bounds [{low}..{high}].");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int idx = low + i;
                EmitUdtInstance($"{fullPath}[{idx}]", dbNumber, ref cursor, udtSections, udtCatalog, properties);
            }
        }

        private static void EmitScalarWithCursor(
            string fullPath,
            int dbNumber,
            ref Cursor cursor,
            string dataType,
            Dictionary<string, Property> properties)
        {
            (TypeString tdType, TypeEnum typeEnum, int sizeBytes, int maxLen, string s7TypeName) = MapType(dataType);
            if (sizeBytes == 0)
            {
                // We don't know how wide the unsupported type is, so we cannot
                // safely advance the cursor past it. All subsequent offsets
                // would be wrong, so abort this branch.
                Console.WriteLine($"      {fullPath} ({dataType}) skipped — unsupported S7 type. Subsequent offsets in this struct/DB may be incorrect.");
                return;
            }

            int byteOffset;
            int bitOffset;

            if (string.Equals(s7TypeName, "BOOL", StringComparison.Ordinal))
            {
                // BOOLs pack bit-by-bit through the current byte; no alignment.
                byteOffset = cursor.Byte;
                bitOffset = cursor.Bit;
                cursor.Bit++;
                if (cursor.Bit >= 8)
                {
                    cursor.Byte++;
                    cursor.Bit = 0;
                }
            }
            else
            {
                // Any non-BOOL leaf re-aligns to a whole byte, and to an even
                // byte if the type is wider than one byte.
                AlignToByte(ref cursor);
                if (sizeBytes >= 2 && (cursor.Byte & 1) != 0)
                {
                    cursor.Byte++;
                }

                byteOffset = cursor.Byte;
                bitOffset = 0;
                cursor.Byte += sizeBytes;
            }

            EmitProperty(fullPath, dbNumber, byteOffset, bitOffset, sizeBytes, maxLen, tdType, typeEnum, s7TypeName, dataType, properties);
        }

        private static void EmitArrayWithCursor(
            string fullPath,
            int dbNumber,
            ref Cursor cursor,
            int low,
            int high,
            string elementType,
            string originalDataType,
            Dictionary<string, Property> properties)
        {
            (TypeString tdType, TypeEnum typeEnum, int elemSize, int maxLen, string s7TypeName) = MapType(elementType);
            if (elemSize == 0)
            {
                Console.WriteLine($"      {fullPath} ({originalDataType}) skipped — unsupported array element type '{elementType}'. Subsequent offsets in this struct/DB may be incorrect.");
                return;
            }

            int count = high - low + 1;
            if (count <= 0)
            {
                Console.WriteLine($"      {fullPath} ({originalDataType}) skipped — invalid array bounds [{low}..{high}].");
                return;
            }

            bool isBool = string.Equals(s7TypeName, "BOOL", StringComparison.Ordinal);

            // Align the array's base address.
            AlignToByte(ref cursor);
            if (!isBool && elemSize >= 2 && (cursor.Byte & 1) != 0)
            {
                cursor.Byte++;
            }

            int baseByte = cursor.Byte;

            for (int i = 0; i < count; i++)
            {
                int idx = low + i;
                int elemByteOffset;
                int elemBitOffset;

                if (isBool)
                {
                    // BOOL arrays pack bit-by-bit, byte-by-byte from the base.
                    elemByteOffset = baseByte + (i / 8);
                    elemBitOffset = i % 8;
                }
                else
                {
                    elemByteOffset = baseByte + (i * elemSize);
                    elemBitOffset = 0;
                }

                EmitProperty($"{fullPath}[{idx}]", dbNumber, elemByteOffset, elemBitOffset, elemSize, maxLen, tdType, typeEnum, s7TypeName, elementType, properties);
            }

            // Advance past the array and pad to an even byte boundary.
            cursor.Byte = isBool
                ? baseByte + ((count + 7) / 8)
                : baseByte + (count * elemSize);
            cursor.Bit = 0;
            if ((cursor.Byte & 1) != 0)
            {
                cursor.Byte++;
            }
        }

        private static void EmitProperty(
            string fullPath,
            int dbNumber,
            int byteOffset,
            int bitOffset,
            int sizeBytes,
            int maxLen,
            TypeString tdType,
            TypeEnum typeEnum,
            string s7TypeName,
            string dataType,
            Dictionary<string, Property> properties)
        {
            S7Form form = new S7Form()
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
                S7S7Type = string.IsNullOrEmpty(s7TypeName) ? null : s7TypeName,
                Type = tdType
            };

            Property property = new Property()
            {
                Type = typeEnum,
                ReadOnly = false,
                Observable = true,
                Forms = new object[1] { form }
            };

            // Property keys must be JSON-friendly identifiers — strip array
            // index brackets so e.g. Foo.Bar[3] becomes Foo_Bar_3.
            string propertyKey = fullPath
                .Replace('.', '_')
                .Replace("[", "_")
                .Replace("]", string.Empty);

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
                // Avoid raising EngineeringNotSupportedException for attributes
                // that are not defined for this particular engineering object
                // (e.g. "Offset" on struct container members, InOut/Temp members,
                // or members of optimized DBs).
                foreach (EngineeringAttributeInfo info in obj.GetAttributeInfos())
                {
                    if (string.Equals(info.Name, name, StringComparison.Ordinal))
                    {
                        return obj.GetAttribute(name);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cannot get attribute {name} on object {obj.ToString()}: " + ex.Message);
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

                _ = int.TryParse(s.Substring(0, dot), out int byteOff);
                _ = int.TryParse(s.Substring(dot + 1), out int bitOff);
                return (byteOff, bitOff);
            }

            return (0, 0);
        }

        /// <summary>
        /// Detects the TIA-style "Array[lo..hi] of ElementType" syntax
        /// and returns the bounds plus the element type. Multi-dimensional
        /// arrays (with a comma in the bounds) are not supported and will
        /// be reported as not-an-array; callers will then skip them.
        /// </summary>
        private static (bool isArray, int low, int high, string elementType) TryParseArray(string s7Type)
        {
            if (string.IsNullOrEmpty(s7Type))
            {
                return (false, 0, 0, null);
            }

            string s = s7Type.Trim();
            if (!s.StartsWith("Array", StringComparison.OrdinalIgnoreCase))
            {
                return (false, 0, 0, null);
            }

            int lb = s.IndexOf('[');
            int rb = s.IndexOf(']', lb + 1);
            int ofIdx = s.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
            if (lb < 0 || rb < 0 || ofIdx < 0 || ofIdx < rb)
            {
                return (false, 0, 0, null);
            }

            string boundsStr = s.Substring(lb + 1, rb - lb - 1);
            if (boundsStr.IndexOf(',') >= 0)
            {
                // Multi-dimensional — leave it to a future iteration.
                return (false, 0, 0, null);
            }

            string elementType = s.Substring(ofIdx + 4).Trim();

            int dotdot = boundsStr.IndexOf("..", StringComparison.Ordinal);
            if (dotdot < 0)
            {
                return (false, 0, 0, null);
            }

            if (!int.TryParse(boundsStr.Substring(0, dotdot), out int low))
            {
                return (false, 0, 0, null);
            }

            if (!int.TryParse(boundsStr.Substring(dotdot + 2), out int high))
            {
                return (false, 0, 0, null);
            }

            return (true, low, high, elementType);
        }

        /// <summary>
        /// Maps a Siemens elementary type name to the corresponding WoT
        /// TypeString, JSON Schema type, fixed wire size in bytes, max
        /// declared character length (for STRING/WSTRING/CHAR/WCHAR only)
        /// and the canonical Siemens type name that the runtime dispatches
        /// on. Returns sizeBytes = 0 for unsupported types so the caller
        /// can skip them with a warning.
        /// </summary>
        private static (TypeString tdType, TypeEnum typeEnum, int sizeBytes, int maxLen, string s7TypeName) MapType(string s7Type)
        {
            if (string.IsNullOrEmpty(s7Type))
            {
                return (TypeString.String, TypeEnum.String, 0, 0, string.Empty);
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
                    _ = int.TryParse(s7Type.Substring(bracket + 1, close - bracket - 1), out len);
                }
            }

            string canonical = baseType.Trim().ToUpperInvariant();
            switch (canonical)
            {
                // ---- bit ------------------------------------------------------
                case "BOOL":
                    return (TypeString.Boolean, TypeEnum.Boolean, 1, 0, "BOOL");

                // ---- 8-bit integers ------------------------------------------
                case "BYTE":
                case "USINT":
                    return (TypeString.Byte, TypeEnum.Integer, 1, 0, canonical);
                case "SINT":
                    return (TypeString.Byte, TypeEnum.Integer, 1, 0, "SINT");

                // ---- single characters (NEW: round-tripped as 1/2-char strings)
                case "CHAR":
                    return (TypeString.String, TypeEnum.String, 1, 1, "CHAR");
                case "WCHAR":
                    return (TypeString.String, TypeEnum.String, 2, 1, "WCHAR");

                // ---- 16-bit integers -----------------------------------------
                case "WORD":
                case "UINT":
                    return (TypeString.Short, TypeEnum.Integer, 2, 0, canonical);
                case "INT":
                    return (TypeString.Short, TypeEnum.Integer, 2, 0, "INT");

                // ---- 32-bit integers -----------------------------------------
                case "DWORD":
                case "UDINT":
                    return (TypeString.Integer, TypeEnum.Integer, 4, 0, canonical);
                case "DINT":
                    return (TypeString.Integer, TypeEnum.Integer, 4, 0, "DINT");

                // ---- 64-bit integers (NEW) -----------------------------------
                case "LWORD":
                case "ULINT":
                    return (TypeString.UnsignedLong, TypeEnum.Integer, 8, 0, canonical);
                case "LINT":
                    return (TypeString.Long, TypeEnum.Integer, 8, 0, "LINT");

                // ---- floating point ------------------------------------------
                case "REAL":
                    return (TypeString.Float, TypeEnum.Number, 4, 0, "REAL");
                case "LREAL":
                    return (TypeString.Double, TypeEnum.Number, 8, 0, "LREAL");

                // ---- variable-length strings ---------------------------------
                case "STRING":
                    if (len <= 0) len = 254;
                    return (TypeString.String, TypeEnum.String, len + 2, len, "STRING");
                case "WSTRING":
                    if (len <= 0) len = 254;
                    // Header is 2 words (4 bytes), payload is len UTF-16 code units (2*len bytes).
                    return (TypeString.String, TypeEnum.String, 4 + (len * 2), len, "WSTRING");

                // ---- date / time / duration (NEW) ----------------------------
                case "DATE":
                    // Days since 1990-01-01, unsigned 16-bit.
                    return (TypeString.DateTime, TypeEnum.String, 2, 0, "DATE");
                case "TIME":
                    // Signed milliseconds, 32-bit.
                    return (TypeString.Duration, TypeEnum.String, 4, 0, "TIME");
                case "LTIME":
                    // Signed nanoseconds, 64-bit.
                    return (TypeString.Duration, TypeEnum.String, 8, 0, "LTIME");
                case "S5TIME":
                    // 16-bit BCD with time-base nibble.
                    return (TypeString.Duration, TypeEnum.String, 2, 0, "S5TIME");
                case "TOD":
                case "TIME_OF_DAY":
                    // Milliseconds since midnight, unsigned 32-bit.
                    return (TypeString.DateTime, TypeEnum.String, 4, 0, "TOD");
                case "LTOD":
                case "LTIME_OF_DAY":
                    // Nanoseconds since midnight, unsigned 64-bit.
                    return (TypeString.DateTime, TypeEnum.String, 8, 0, "LTOD");
                case "DT":
                case "DATE_AND_TIME":
                    // 8-byte BCD: year, month, day, hour, minute, second, msec(hi/lo+dow nibble).
                    return (TypeString.DateTime, TypeEnum.String, 8, 0, "DT");
                case "LDT":
                    // Nanoseconds since 1970-01-01 00:00:00 UTC, signed 64-bit.
                    return (TypeString.DateTime, TypeEnum.String, 8, 0, "LDT");
                case "DTL":
                    // 12-byte structured: year(WORD) month(BYTE) day(BYTE) dow(BYTE)
                    //                     hour(BYTE) min(BYTE) sec(BYTE) nanosec(DWORD).
                    return (TypeString.DateTime, TypeEnum.String, 12, 0, "DTL");

                // ---- everything else (VARIANT, ANY, HW_*, DB_ANY, POINTER…) --
                default:
                    return (TypeString.String, TypeEnum.String, 0, 0, string.Empty);
            }
        }
#endif
    }
}
