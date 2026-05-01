namespace Opc.Ua.Edge.Translator.Tools
{
    using Aml.Engine.CAEX;
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Export;
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Xml;
    using System.Xml.Serialization;
    using WoTThingModelGenerator.TwinCAT;
    using Property = Models.Property;

#nullable disable

    internal class Program
    {
        static void Main()
        {
            SiemensTIAImporter.Register();

            foreach (string filename in Directory.GetFiles(Directory.GetCurrentDirectory()))
            {
                if (filename.ToLower().EndsWith(".nodeset2.xml"))
                {
                    ImportNodeset2Xml(filename);
                }

                if (filename.ToLower().EndsWith(".aml"))
                {
                    ImportAutomationML(filename);
                }

                if (filename.ToLower().EndsWith(".aas.json"))
                {
                    ImportAASAID(filename);
                }

                if (filename.ToLower().EndsWith(".tmc"))
                {
                    ImportTwinCAT(filename);
                }

                if (filename.ToLower().EndsWith(".csv"))
                {
                    ImportCSVFromRockwell(filename);
                    ImportCSVFromAzureForModbus(filename);
                }

                // TIA Portal project files (V18..V21 use ap18..ap21).
                string lower = filename.ToLower();
                if (lower.EndsWith(".ap21") || lower.EndsWith(".ap20") ||
                    lower.EndsWith(".ap19") || lower.EndsWith(".ap18"))
                {
                    SiemensTIAImporter.Import(filename);
                }
            }
        }

        private static void ImportCSVFromAzureForModbus(string filename)
        {
            IEnumerable<string> content = File.ReadLines(filename);

            string tdName = Path.GetFileNameWithoutExtension(filename).Replace(" ", "_");

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + tdName,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "modbus+tcp://{{address}}:{{port}}",
                Title = tdName,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            foreach (string line in content)
            {
                string[] tokens = line.Split(';');

                if ((tokens.Length > 18) && tokens[0].Contains("Points List"))
                {
                    // skip header row
                    continue;
                }

                if (tokens.Length > 18)
                {
                    string propertName = tokens[4].Replace(" ", "_");

                    ModbusForm form = new()
                    {
                        Href = tokens[12],
                        Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                        PollingTime = 1000,
                        ModbusEntity = tokens[10].Contains("Holding Register") ? ModbusEntity.HoldingRegister : throw new NotSupportedException(),
                    };

                    if (tokens[14] == "FLOAT")
                    {
                        form.ModbusType = TypeString.Float;
                        form.Href += "?quantity=2";
                    }
                    else if (tokens[14] == "INT16")
                    {
                        form.ModbusType = TypeString.Integer;
                        form.Href += "?quantity=1";
                    }
                    else if (tokens[14] == "INT32")
                    {
                        form.ModbusType = TypeString.Integer;
                        form.Href += "?quantity=2";
                    }
                    else if (tokens[14] == "BOOL")
                    {
                        form.ModbusType = TypeString.Boolean;
                        form.Href += "?quantity=1";
                    }

                    Property property = new()
                    {
                        ReadOnly = true,
                        Observable = true,
                        Forms = new object[1] { form }
                    };

                    if (!td.Properties.ContainsKey(propertName))
                    {
                        td.Properties.Add(propertName, property);
                    }
                }
            }

            if (td.Properties.Count > 0)
            {
                File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), tdName + ".tm.jsonld"), JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
            }
        }

        private static void ImportCSVFromRockwell(string filename)
        {
            string[] allLines = File.ReadAllLines(filename);

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + Path.GetFileNameWithoutExtension(filename),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "eip://{{address}}:{{port}}",
                Title = Path.GetFileNameWithoutExtension(filename),
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            // First pass: collect all TAG rows and build a map of tag name -> datatype.
            // Also collect COMMENT rows to infer UDT field names from specifier paths.
            // tag key = "scope:name" for scoped tags, ":name" for controller-scoped.
            var tagDataTypes = new Dictionary<string, string>();
            var tagDescriptions = new Dictionary<string, string>();
            var tagScopes = new Dictionary<string, string>();

            // UDT field inference: for each UDT type name, collect discovered field names
            // and their inferred types from COMMENT specifier paths.
            // Key = UDT type name (e.g., "MachineUDT"), Value = dict of field name -> inferred type.
            var udtFieldsByType = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in allLines)
            {
                string[] tokens = ParseCsvLine(line);

                if (tokens.Length > 6 && tokens[0] == "TAG")
                {
                    string scope = tokens[1];
                    string name = tokens[2];
                    string description = StripQuotes(tokens[3]);
                    string datatype = StripQuotes(tokens[4]);
                    string key = scope + ":" + name;

                    tagDataTypes[key] = datatype;
                    tagDescriptions[key] = description;
                    tagScopes[key] = scope;
                }
                else if (tokens.Length > 5 && tokens[0] == "COMMENT")
                {
                    string scope = tokens[1];
                    string tagName = tokens[2];
                    string description = StripQuotes(tokens[3]);
                    string specifier = StripQuotes(tokens[5]);

                    // Look up the tag to find its UDT type
                    string tagKey = scope + ":" + tagName;
                    if (!tagDataTypes.TryGetValue(tagKey, out string tagDataType))
                    {
                        continue;
                    }

                    // Extract the base UDT type name (strip array dimensions like "MachineUDT[10]" -> "MachineUDT")
                    string baseTypeName = StripArrayDimension(tagDataType);

                    if (IsPrimitiveType(baseTypeName))
                    {
                        // COMMENT on a primitive (e.g., DINT bit comments) — not a UDT field
                        continue;
                    }

                    // Parse the specifier to extract the first-level field name.
                    // Specifier examples:
                    //   "mMachineUDT.INTERLOCKS.0"      -> field "INTERLOCKS"
                    //   "LineWorkcell.IDEALCT"           -> field "IDEALCT"
                    //   "mStepControl[1].CONTROL.1"      -> field "CONTROL" (inside StepLogic UDT)
                    //   "LineData[0].INTERLOCKS.0"        -> field "INTERLOCKS" (inside MachineUDT)
                    string fieldPath = ExtractFieldPath(specifier, tagName);
                    if (string.IsNullOrEmpty(fieldPath))
                    {
                        continue;
                    }

                    // Get the first-level field name (before any further dots or bit indices)
                    string fieldName = fieldPath.Split('.')[0];

                    // Infer the field type from the subsequent path segments:
                    // - If there's a bit index (e.g., "INTERLOCKS.0") -> DINT
                    // - Otherwise we can't determine the type from the CSV -> leave as unknown
                    string inferredType = InferFieldType(fieldPath);

                    if (!udtFieldsByType.TryGetValue(baseTypeName, out var fields))
                    {
                        fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        udtFieldsByType[baseTypeName] = fields;
                    }

                    // Only set type if we inferred one, don't overwrite a known type with unknown
                    if (!fields.ContainsKey(fieldName))
                    {
                        fields[fieldName] = inferredType;
                    }
                    else if (fields[fieldName] == null && inferredType != null)
                    {
                        fields[fieldName] = inferredType;
                    }
                }
            }

            // Second pass: build UDT structure definitions from inferred fields.
            // These have no byte offsets (offsets are 0) — to be resolved at runtime.
            var udtStructDefs = new Dictionary<string, EIPStructureDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in udtFieldsByType)
            {
                string typeName = kvp.Key;
                var fields = kvp.Value;

                var eipFields = new List<EIPFieldDefinition>();
                foreach (var field in fields)
                {
                    // Check if this field's inferred type is itself a known UDT
                    bool fieldIsUdt = field.Value != null &&
                        !IsPrimitiveType(field.Value) &&
                        udtFieldsByType.ContainsKey(field.Value);

                    eipFields.Add(new EIPFieldDefinition {
                        Name = field.Key,
                        Type = fieldIsUdt ? null : (field.Value != null ? "xsd:" + field.Value : null),
                        Offset = 0, // unknown from CSV — resolved at runtime
                        StructureDefinition = fieldIsUdt ? GetOrCreateStructDef(field.Value, udtFieldsByType, udtStructDefs) : null
                    });
                }

                udtStructDefs[typeName] = new EIPStructureDefinition {
                    TypeName = typeName,
                    Fields = eipFields.ToArray()
                };
            }

            // Third pass: create TD properties from TAG rows.
            foreach (string line in allLines)
            {
                string[] tokens = ParseCsvLine(line);

                // ignore everything but tags in the form TYPE,SCOPE,NAME,DESCRIPTION,DATATYPE,SPECIFIER,ATTRIBUTES, where TYPE must be "TAG"
                if (tokens.Length <= 6 || tokens[0] != "TAG")
                {
                    continue;
                }

                string scope = tokens[1];
                string name = tokens[2];
                string description = StripQuotes(tokens[3]);
                string datatype = StripQuotes(tokens[4]);

                // Build the fully-qualified property name: scope.name for scoped tags
                string propertyName = string.IsNullOrEmpty(scope) ? name : scope + "." + name;

                // Strip array dimension for type lookup
                string baseTypeName = StripArrayDimension(datatype);

                if (IsPrimitiveType(baseTypeName))
                {
                    // Primitive tag — use EIPForm with EIP-specific type strings
                    EIPForm form = new() {
                        Href = propertyName,
                        Op = [Op.Readproperty, Op.Observeproperty, Op.Writeproperty],
                        PollingTime = 1000
                    };

                    Property property = new()
                    {
                        ReadOnly = false,
                        Observable = true,
                        Forms = [form]
                    };

                    switch (baseTypeName.ToUpper())
                    {
                        case "REAL":
                            form.Type = EIPTypeString.REAL;
                            property.Type = TypeEnum.Number;
                            break;
                        case "LREAL":
                            form.Type = EIPTypeString.LREAL;
                            property.Type = TypeEnum.Number;
                            break;
                        case "DINT":
                            form.Type = EIPTypeString.DINT;
                            property.Type = TypeEnum.Integer;
                            break;
                        case "INT":
                            form.Type = EIPTypeString.INT;
                            property.Type = TypeEnum.Integer;
                            break;
                        case "SINT":
                            form.Type = EIPTypeString.SINT;
                            property.Type = TypeEnum.Integer;
                            break;
                        case "LINT":
                            form.Type = EIPTypeString.LINT;
                            property.Type = TypeEnum.Integer;
                            break;
                        case "UDINT":
                            form.Type = EIPTypeString.UDINT;
                            property.Type = TypeEnum.Integer;
                            break;
                        case "UINT":
                            form.Type = EIPTypeString.UINT;
                            property.Type = TypeEnum.Integer;
                            break;
                        case "USINT":
                            form.Type = EIPTypeString.USINT;
                            property.Type = TypeEnum.Integer;
                            break;
                        case "ULINT":
                            form.Type = EIPTypeString.ULINT;
                            property.Type = TypeEnum.Integer;
                            break;
                        case "BOOL":
                            form.Type = EIPTypeString.BOOL;
                            property.Type = TypeEnum.Boolean;
                            break;
                        case "STRING":
                            form.Type = EIPTypeString.STRING;
                            property.Type = TypeEnum.String;
                            break;
                        default:
                            // TIMER, COUNTER, FBD_TIMER — skip
                            continue;
                    }

                    if (!td.Properties.ContainsKey(propertyName))
                    {
                        td.Properties.Add(propertyName, property);
                    }
                }
                else
                {
                    // Recursively build the structure definition, resolving nested UDTs.
                    // Unlike BrowseAndGenerateTD() which reads live UDT metadata from the PLC
                    // via @udt/{id}, here we use the fields inferred from COMMENT rows.
                    // Fields without COMMENT entries will be missing — they are resolved at
                    // runtime by the protocol driver's ResolveUdtDefinition().
                    EIPStructureDefinition structDef = GetOrCreateStructDef(baseTypeName, udtFieldsByType, udtStructDefs);

                    Console.WriteLine($"CSV Tag: {propertyName} -> UDT '{structDef.TypeName}' with {structDef.Fields?.Length ?? 0} inferred field(s)");

                    if (structDef.Fields != null)
                    {
                        foreach (var field in structDef.Fields)
                        {
                            if (field.StructureDefinition != null)
                            {
                                Console.WriteLine($"  Field: {field.Name} -> nested UDT '{field.StructureDefinition.TypeName}' (offset resolved at runtime)");
                            }
                            else
                            {
                                Console.WriteLine($"  Field: {field.Name} type={field.Type ?? "unknown"} (offset resolved at runtime)");
                            }
                        }
                    }

                    string assetNamespaceUri = "http://opcfoundation.org/UA/{{name}}/";

                    EIPForm form = new() {
                        Href = propertyName,
                        Op = [Op.Readproperty, Op.Observeproperty, Op.Writeproperty],
                        PollingTime = 1000,
                        StructureDefinition = structDef
                    };

                    Property property = new() {
                        Type = TypeEnum.Object,
                        ReadOnly = false,
                        Observable = true,
                        OpcUaType = $"nsu={assetNamespaceUri};s={baseTypeName}Type",
                        Forms = [form]
                    };

                    if (!td.Properties.ContainsKey(propertyName))
                    {
                        td.Properties.Add(propertyName, property);
                    }
                }
            }

            if (td.Properties.Count > 0)
            {
                File.WriteAllText(
                    Path.Combine(Directory.GetCurrentDirectory(),
                    Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"),
                    JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
            }
        }

        // Known Rockwell primitive / built-in types that should NOT be treated as UDTs.
        private static readonly HashSet<string> PrimitiveTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "REAL", "LREAL", "DINT", "INT", "SINT", "LINT",
            "BOOL", "STRING", "UDINT", "UINT", "USINT", "ULINT",
            "TIMER", "COUNTER", "FBD_TIMER"
        };

        /// <summary>
        /// Checks if a datatype name is a known Rockwell primitive / built-in type.
        /// </summary>
        private static bool IsPrimitiveType(string typeName)
        {
            return PrimitiveTypes.Contains(typeName);
        }

        /// <summary>
        /// Strips array dimension from a datatype string.
        /// E.g., "MachineUDT[10]" -> "MachineUDT", "REAL[10]" -> "REAL", "DINT" -> "DINT".
        /// </summary>
        private static string StripArrayDimension(string datatype)
        {
            int bracketIdx = datatype.IndexOf('[');
            return bracketIdx >= 0 ? datatype[..bracketIdx] : datatype;
        }

        /// <summary>
        /// Removes surrounding double-quotes from a CSV token.
        /// </summary>
        private static string StripQuotes(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                return value[1..^1];
            }

            return value;
        }

        /// <summary>
        /// Extracts the field path from a COMMENT specifier relative to the tag name.
        /// For "mMachineUDT.INTERLOCKS.0" with tag "mMachineUDT", returns "INTERLOCKS.0".
        /// For "LineData[0].INTERLOCKS.0" with tag "LineData", returns "INTERLOCKS.0".
        /// Returns null if the specifier doesn't contain a field path.
        /// </summary>
        private static string ExtractFieldPath(string specifier, string tagName)
        {
            if (string.IsNullOrEmpty(specifier))
            {
                return null;
            }

            // The specifier starts with the tag name, optionally followed by array indices,
            // then a dot and the field path. E.g.:
            //   "tagName.FIELD.sub"          -> "FIELD.sub"
            //   "tagName[0].FIELD.sub"       -> "FIELD.sub"
            //   "tagName[0].FIELD.0"         -> "FIELD.0"

            // Build a regex to match: tagName (\[\d+\])? \.(.+)
            string escaped = Regex.Escape(tagName);
            var match = Regex.Match(specifier, @"^" + escaped + @"(?:\[\d+\])?\.(.*)", RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Infers the Rockwell type of a UDT field from the remaining field path.
        /// - "INTERLOCKS.0" (ends with a numeric bit index after a DINT field) -> "DINT"
        /// - "IDEALCT" (simple name, no sub-path) -> null (unknown, needs runtime resolution)
        /// - "CONTROL.1" -> "DINT" (bit-level access to an integer field)
        /// </summary>
        private static string InferFieldType(string fieldPath)
        {
            string[] parts = fieldPath.Split('.');

            if (parts.Length >= 2)
            {
                // If the last segment is a number, it's a bit index into a DINT
                if (int.TryParse(parts[^1], out _))
                {
                    return "DINT";
                }

                // Multiple non-numeric segments suggest a nested UDT field path
                // Return the first field name as potentially a nested UDT — can't determine from CSV
                return null;
            }

            // Single segment — the field exists but we can't determine its type from comments alone
            return null;
        }

        /// <summary>
        /// Gets or creates an EIPStructureDefinition for a UDT type, handling
        /// recursive nesting by looking up inferred fields.
        /// </summary>
        private static EIPStructureDefinition GetOrCreateStructDef(
            string typeName,
            Dictionary<string, Dictionary<string, string>> udtFieldsByType,
            Dictionary<string, EIPStructureDefinition> udtStructDefs)
        {
            if (udtStructDefs.TryGetValue(typeName, out var existing))
            {
                return existing;
            }

            if (!udtFieldsByType.TryGetValue(typeName, out var fields))
            {
                var stub = new EIPStructureDefinition { TypeName = typeName, Fields = [] };
                udtStructDefs[typeName] = stub;
                return stub;
            }

            // Create a placeholder first to handle circular references
            var def = new EIPStructureDefinition { TypeName = typeName };
            udtStructDefs[typeName] = def;

            var eipFields = new List<EIPFieldDefinition>();
            foreach (var field in fields)
            {
                bool fieldIsUdt = field.Value != null &&
                                  !IsPrimitiveType(field.Value) &&
                                  udtFieldsByType.ContainsKey(field.Value);

                eipFields.Add(new EIPFieldDefinition
                {
                    Name = field.Key,
                    Type = fieldIsUdt ? null : (field.Value != null ? "xsd:" + field.Value : null),
                    Offset = 0, // offsets are read from the PLC at runtime
                    StructureDefinition = fieldIsUdt ? GetOrCreateStructDef(field.Value, udtFieldsByType, udtStructDefs) : null
                });
            }

            def.Fields = eipFields.ToArray();
            return def;
        }

        /// <summary>
        /// Parses a CSV line handling quoted fields that may contain commas.
        /// RSLogix 5000 CSV uses double-quotes for fields containing commas or special chars.
        /// </summary>
        private static string[] ParseCsvLine(string line)
        {
            var tokens = new List<string>();
            bool inQuotes = false;
            int start = 0;

            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (line[i] == ',' && !inQuotes)
                {
                    tokens.Add(line[start..i]);
                    start = i + 1;
                }
            }

            tokens.Add(line[start..]);
            return tokens.ToArray();
        }

        private static void ImportTwinCAT(string filename)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(TwinCAT));
            TwinCAT twinCAT = null;
            using (XmlReader reader = XmlReader.Create(filename))
            {
                twinCAT = serializer.Deserialize(reader) as TwinCAT;
            }

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + Path.GetFileNameWithoutExtension(filename),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "ads://{{address}}:{{port}}",
                Title = twinCAT.Modules.Module.Name,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            foreach (Symbol symbol in twinCAT.Modules.Module.DataAreas.DataArea.Symbol)
            {
                string propertyName = symbol.Name;

                GenericForm form = new()
                {
                    Href = propertyName + "?" + symbol.BitSize/8,
                    Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                    PollingTime = 1000,
                    Type = TypeString.Float
                };

                Property property = new()
                {
                    Type = TypeEnum.Number,
                    ReadOnly = true,
                    Observable = true,
                    Forms = new object[1] { form }
                };

                if (!td.Properties.ContainsKey(propertyName))
                {
                    td.Properties.Add(propertyName, property);
                }
            }

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
        }

        private static void ImportAASAID(string filename)
        {
            AAS_AID aid = JsonConvert.DeserializeObject<AAS_AID>(File.ReadAllText(filename));

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + Path.GetFileNameWithoutExtension(filename),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "{{protocol}}://{{address}}:{{port}}",
                Title = Path.GetFileNameWithoutExtension(filename),
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            Dictionary<string, string> subModelElements = new();
            if (aid?.SubmodelElements != null)
            {
                ImportSubmodelElementCollection(aid.Id, aid.SubmodelElements, subModelElements);
            }

            foreach( KeyValuePair<string, string> pair in subModelElements)
            {
                if (pair.Key.ToLower().EndsWith(":title"))
                {
                    td.Title = pair.Value;
                }

                if (pair.Key.ToLower().EndsWith(":base"))
                {
                    td.Base = pair.Value;
                }

                if (pair.Key.ToLower().EndsWith(":number"))
                {
                    string propertyName = pair.Key.Substring(0, pair.Key.LastIndexOf(":"));

                    // check if we have to create a new property
                    CreateNewProperty(td, pair.Key, propertyName);

                    GenericForm form = (GenericForm)td.Properties[propertyName].Forms[0];
                    form.Href = pair.Value;
                }

                if (pair.Key.ToLower().Contains(":properties:"))
                {
                    string propertyName = pair.Key.Substring(pair.Key.IndexOf(":properties:") + 12);
                    propertyName = propertyName.Substring(0, propertyName.IndexOf(":"));

                    // check if we have to create a new property
                    CreateNewProperty(td, pair.Key, propertyName);

                    if (pair.Key.EndsWith(":href"))
                    {
                        if (pair.Key.Contains(":InterfaceMODBUS_TCP:"))
                        {
                            ModbusForm form = (ModbusForm)td.Properties[propertyName].Forms[0];
                            form.Href = pair.Value;
                        }
                        else
                        {
                            GenericForm form = (GenericForm)td.Properties[propertyName].Forms[0];
                            form.Href = pair.Value;
                        }
                    }

                    if (pair.Key.EndsWith(":modv_pollingTime"))
                    {
                        if (pair.Key.Contains(":InterfaceMODBUS_TCP:"))
                        {
                            ModbusForm form = (ModbusForm)td.Properties[propertyName].Forms[0];
                            form.PollingTime = long.Parse(pair.Value);
                        }
                    }
                }
            }

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
        }

        private static void CreateNewProperty(ThingDescription td, string key, string propertyName)
        {
            if (!td.Properties.ContainsKey(propertyName))
            {
                object form = null;
                if (key.Contains(":InterfaceMODBUS_TCP:"))
                {
                    ModbusForm modbusForm = new()
                    {
                        Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                        ModbusEntity = ModbusEntity.HoldingRegister,
                        ModbusType = TypeString.Float
                    };
                    form = modbusForm;
                }
                else
                {
                    GenericForm genericForm = new()
                    {
                        Op = new Op[2] { Op.Readproperty, Op.Observeproperty }
                    };
                    form = genericForm;
                }

                Property property = new()
                {
                    Type = TypeEnum.Number,
                    ReadOnly = true,
                    Observable = true,
                    Forms = new object[1] { form }
                };

                if (!td.Properties.ContainsKey(propertyName))
                {
                    td.Properties.Add(propertyName, property);
                }
            }
        }

        private static void ImportSubmodelElementCollection(string parentName, List<SubmodelElement> smec, Dictionary<string, string> subModelElements)
        {
            foreach (SubmodelElement submodelElement in smec)
            {
                if (submodelElement.Value != null)
                {
                    if (submodelElement.ValueType == null)
                    {
                        try
                        {
                            List<SubmodelElement> nestedSMEC = JsonConvert.DeserializeObject<List<SubmodelElement>>(submodelElement.Value.ToString());
                            if (nestedSMEC.Count > 0)
                            {
                                ImportSubmodelElementCollection(parentName + ":" + submodelElement.IdShort, nestedSMEC, subModelElements);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Ignoring SubmodelElement " + submodelElement.IdShort + ": " + ex.Message);
                        }
                    }
                    else
                    {
                        subModelElements.Add(parentName + ":" + submodelElement.IdShort, submodelElement.Value.ToString());
                    }
                }
            }
        }

        private static void ImportAutomationML(string filename)
        {
            CAEXDocument doc = CAEXDocument.LoadFromString(File.ReadAllText(filename));

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + Path.GetFileNameWithoutExtension(filename),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "{{protocol}}://{{address}}:{{port}}",
                Title = Path.GetFileNameWithoutExtension(filename),
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            foreach (InstanceHierarchyType instanceHirarchy in doc.CAEXFile.InstanceHierarchy)
            {
                foreach (InternalElementType internalElement in instanceHirarchy.InternalElement)
                {
                    ImportInternalElement(instanceHirarchy.Name, internalElement, td.Properties);
                }
            }

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
        }

        private static void ImportInternalElement(string parent, InternalElementType internalElement, Dictionary<string, Property> tdProperties)
        {
            // check if this is a leaf element
            if (internalElement.InternalElement.Count > 0)
            {
                foreach (InternalElementType childInternalElement in internalElement.InternalElement)
                {
                    ImportInternalElement(parent + "_" + internalElement.Name, childInternalElement, tdProperties);
                }
            }
            else
            {
                foreach (AttributeType attribute in internalElement.Attribute)
                {
                    ImportAttribute(parent + "_" + internalElement.Name, attribute, tdProperties);
                }
            }
        }

        private static void ImportAttribute(string parent, AttributeType attribute, Dictionary<string, Property> tdProperties)
        {
            // check if this is a leaf attribute
            if (attribute.Attribute.Count > 0)
            {
                foreach (AttributeType childAttribute in attribute.Attribute)
                {
                    ImportAttribute(parent + "_" + attribute.Name, childAttribute, tdProperties);
                }
            }
            else
            {
                // for leaf attributes, we generate WoT properties
                string reference = parent + "_" + attribute.Name;

                GenericForm form = new()
                {
                    Href = reference,
                    Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                    Type = TypeString.Float,
                    PollingTime = 1000
                };

                Property property = new()
                {
                    Type = TypeEnum.Number,
                    ReadOnly = true,
                    Observable = true,
                    Forms = new object[1] { form }
                };

                if (!tdProperties.ContainsKey(reference))
                {
                    tdProperties.Add(reference, property);
                }
            }
        }

        // Nodeset2 files can be edited using e.g. the SIEMENS OPC UA Modeling Editor (SiOME)
        // see https://support.industry.siemens.com/cs/document/109755133/siemens-opc-ua-modeling-editor-(siome)-for-implementing-opc-ua-companion-specification
        private static void ImportNodeset2Xml(string filename)
        {
            Stream stream = new FileStream(filename, FileMode.Open);

            ThingDescription td = new() {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + Path.GetFileNameWithoutExtension(filename),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "opc.tcp://{{address}}:{{port}}",
                Title = Path.GetFileNameWithoutExtension(filename),
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            UANodeSet nodeSet = UANodeSet.Read(stream);
            foreach (UANode node in nodeSet.Items)
            {
                AddPredefinedNode(node, nodeSet.NamespaceUris[0], td.Properties);
            }

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
        }

        private static void AddPredefinedNode(UANode predefinedNode, string namespaceUri, Dictionary<string, Property> tdProperties)
        {
            if (predefinedNode is UAVariable)
            {
                UAVariable variable = (UAVariable)predefinedNode;

                GenericForm form = new()
                {
                    Href = "nsu=" + namespaceUri + ";" + variable.NodeId.Replace("ns=1;", ""),
                    Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                    Type = TypeString.Float,
                    PollingTime = 1000
                };

                foreach( Export.Reference reference in variable.References)
                {
                    if (reference.ReferenceType == "HasModellingRule")
                    {
                        // we're not interested in models, only instances
                        return;
                    }
                }

                if ((predefinedNode.DisplayName[0].Value == "InputArguments") || (predefinedNode.DisplayName[0].Value == "OuputArguments"))
                {
                    // we're not interested in method arguments
                    return;
                }

                Property property = new()
                {
                    Type = TypeEnum.Number,
                    ReadOnly = true,
                    Observable = true,
                    Forms = new object[1] { form }
                };

                if (!tdProperties.ContainsKey(predefinedNode.DisplayName[0].Value))
                {
                    tdProperties.Add(predefinedNode.DisplayName[0].Value, property);
                }
            }
        }
    }
}
