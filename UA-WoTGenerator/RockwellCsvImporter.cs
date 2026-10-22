namespace Opc.Ua.Edge.Translator.Tools
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using Property = Models.Property;

    /// <summary>
    /// Builds a Thing Model from a Studio 5000 / RSLogix 5000 tag CSV export, following
    /// the W3C WoT EtherNet/IP binding
    /// (https://w3c.github.io/wot-binding-templates/bindings/protocols/ethernetip/).
    /// <para>
    /// The export names each tag and its type, but carries no UDT member layout. The
    /// binding lets a form name a structured tag only, and has a Consumer that can browse
    /// the controller prefer the live definition, so structured tags are written without
    /// <c>eipv:structureDefinition</c> and the Rockwell driver reads the layout from the
    /// controller.
    /// </para>
    /// </summary>
    public static class RockwellCsvImporter
    {
        /// <summary>Default CIP route: backplane, slot 0. Placeholders are filled in on onboarding.</summary>
        public const string BaseTemplate = "eip://{{address}}:{{port}}/1,0/";

        /// <summary>Why a tag row did not become a property, for the console summary.</summary>
        public sealed class Result
        {
            public ThingDescription ThingModel { get; set; }

            public List<string> Skipped { get; } = new List<string>();
        }

        /// <summary>Builds the Thing Model for the CSV export in <paramref name="filename"/>.</summary>
        public static Result Import(string filename)
        {
            string title = Path.GetFileNameWithoutExtension(filename);
            Result result = new Result
            {
                ThingModel = new ThingDescription()
                {
                    Context = new object[] { "https://www.w3.org/2022/wot/td/v1.1", new Dictionary<string, string> { { EIPForm.Prefix, EIPForm.BindingNamespace } } },
                    Id = "urn:" + title,
                    SecurityDefinitions = new SecurityDefinitions() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                    Security = new string[1] { "nosec_sc" },
                    Type = new string[1] { "tm:ThingModel" },
                    Name = "{{name}}",
                    Base = BaseTemplate,
                    Title = title,
                    Properties = new Dictionary<string, Property>(),
                    Actions = new Dictionary<string, TDAction>()
                }
            };

            foreach (string line in File.ReadLines(filename))
            {
                string[] tokens = ParseCsvLine(line);

                // TYPE,SCOPE,NAME,DESCRIPTION,DATATYPE,SPECIFIER,ATTRIBUTES; only TAG rows are tags.
                if (tokens.Length <= 6 || tokens[0] != "TAG")
                {
                    continue;
                }

                AddTag(result, tokens[1], tokens[2], StripQuotes(tokens[4]), StripQuotes(tokens[6]));
            }

            return result;
        }

        private static void AddTag(Result result, string scope, string name, string datatype, string attributes)
        {
            string displayName = string.IsNullOrEmpty(scope) ? name : scope + "." + name;

            // An Add-On Instruction definition (scope "X:AOI") declares parameters, not tags;
            // they only exist inside instances and cannot be addressed as written.
            if (scope.EndsWith(":AOI", StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped.Add(displayName + ": Add-On Instruction parameter, not a controller tag");
                return;
            }

            string access = Attribute(attributes, "ExternalAccess");
            if (string.Equals(access, "None", StringComparison.OrdinalIgnoreCase))
            {
                result.Skipped.Add(displayName + ": ExternalAccess := None");
                return;
            }

            // Proposed array extension: TYPE[d1,d2,d3] becomes eipv:dimensions on a form of
            // the element type, or of the structure the driver reads from the controller.
            // Arrays of BOOL and STRING are not described.
            int[] dimensions = null;
            int bracket = datatype.IndexOf('[');
            if (bracket >= 0)
            {
                dimensions = ParseDimensions(datatype.Substring(bracket));
                datatype = datatype.Substring(0, bracket);
                if (dimensions == null)
                {
                    result.Skipped.Add(displayName + ": array " + datatype + " with dimensions that cannot be read");
                    return;
                }

                if (EIPTypes.TryParse(datatype, out EIPTypeString elementType)
                    && (elementType == EIPTypeString.BOOL || elementType == EIPTypeString.STRING))
                {
                    result.Skipped.Add(displayName + ": array of " + datatype + " (arrays of BOOL and STRING are not described)");
                    return;
                }
            }

            bool readOnly = string.Equals(access, "Read Only", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Attribute(attributes, "Constant"), "true", StringComparison.OrdinalIgnoreCase);

            // Program-scoped tags are addressed Program:<program>.<tag> (1756-PM020).
            string tagPath = string.IsNullOrEmpty(scope) ? name : "Program:" + scope + "." + name;

            EIPForm form = new EIPForm()
            {
                Href = ToHref(tagPath),
                Op = readOnly
                    ? new[] { Op.Readproperty, Op.Observeproperty }
                    : new[] { Op.Readproperty, Op.Observeproperty, Op.Writeproperty },
                PollingTime = 1000
            };

            Property property = new Property()
            {
                ReadOnly = readOnly,
                Observable = true,
                Forms = new object[] { form }
            };

            if (EIPTypes.TryParse(datatype, out EIPTypeString type))
            {
                form.Type = type;
                property.Type = WotTypeOf(type);

                if (dimensions != null)
                {
                    form.Dimensions = dimensions;
                    property.Items = new ArrayItems { Type = property.Type };
                    ApplyBounds(type, property.Items);
                    property.Type = TypeEnum.Array;
                    property.MinItems = form.EffectiveElementCount;
                    property.MaxItems = form.EffectiveElementCount;
                }
                else
                {
                    ApplyBounds(type, property);
                }
            }
            else
            {
                // A UDT, AOI instance or predefined structure (TIMER, COUNTER, ALARM_*):
                // named only, decoded from the definition the driver reads from the controller.
                property.Type = TypeEnum.Object;

                // An array of structures: the export has no UDT layout or eipv:byteSize, and a
                // form with eipv:dimensions must name its element type, so the form names the
                // tag only. The driver reads the layout and the dimensions from the controller;
                // the data schema keeps the array shape from the export.
                if (dimensions != null)
                {
                    int count = 1;
                    foreach (int dimension in dimensions)
                    {
                        count *= dimension;
                    }

                    property.Items = new ArrayItems { Type = TypeEnum.Object };
                    property.Type = TypeEnum.Array;
                    property.MinItems = count;
                    property.MaxItems = count;
                }
            }

            string problem = form.Validate(EIPController.ControlLogix);
            if (problem != null)
            {
                result.Skipped.Add(displayName + ": " + problem);
                return;
            }

            if (!result.ThingModel.Properties.ContainsKey(displayName))
            {
                result.ThingModel.Properties.Add(displayName, property);
            }
        }

        /// <summary>
        /// Reads Logix array dimensions such as <c>[10]</c> or <c>[4,5]</c> (outermost first);
        /// null when there are more than three or any is not a positive integer.
        /// </summary>
        public static int[] ParseDimensions(string text)
        {
            string inner = text.Trim().TrimStart('[').TrimEnd(']');
            string[] parts = inner.Split(',');
            if (parts.Length == 0 || parts.Length > 3)
            {
                return null;
            }

            int[] dimensions = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out dimensions[i]) || dimensions[i] <= 0)
                {
                    return null;
                }
            }

            return dimensions;
        }

        /// <summary>The WoT data schema type the binding recommends for a Logix type.</summary>
        public static TypeEnum WotTypeOf(EIPTypeString type)
        {
            switch (type)
            {
                case EIPTypeString.BOOL: return TypeEnum.Boolean;
                case EIPTypeString.REAL:
                case EIPTypeString.LREAL: return TypeEnum.Number;
                case EIPTypeString.STRING: return TypeEnum.String;
                default: return TypeEnum.Integer;
            }
        }

        /// <summary>The minimum and maximum the binding recommends for a Logix integer type.</summary>
        public static void ApplyBounds(EIPTypeString type, Property schema)
        {
            Bounds(type, out long? minimum, out long? maximum);
            schema.Minimum = minimum;
            schema.Maximum = maximum;
        }

        /// <summary>The minimum and maximum the binding recommends for array elements.</summary>
        public static void ApplyBounds(EIPTypeString type, ArrayItems schema)
        {
            Bounds(type, out long? minimum, out long? maximum);
            schema.Minimum = minimum;
            schema.Maximum = maximum;
        }

        private static void Bounds(EIPTypeString type, out long? minimum, out long? maximum)
        {
            EIPTypes.Bounds(type, out minimum, out maximum);
        }

        /// <summary>
        /// A tag href as the binding requires: reserved characters percent-encoded and a
        /// path containing a colon (such as Program:Line.Speed) starting with "./" so it is
        /// not read as a URI scheme.
        /// </summary>
        public static string ToHref(string tagPath)
        {
            string encoded = Uri.EscapeDataString(tagPath ?? string.Empty).Replace("%3A", ":").Replace("%2E", ".");
            return encoded.Contains(":") ? "./" + encoded : encoded;
        }

        /// <summary>Reads one "Key := Value" entry of an ATTRIBUTES cell; null when absent.</summary>
        public static string Attribute(string attributes, string key)
        {
            if (string.IsNullOrEmpty(attributes))
            {
                return null;
            }

            foreach (string part in attributes.Trim('(', ')', ' ').Split(','))
            {
                int separator = part.IndexOf(":=", StringComparison.Ordinal);
                if (separator > 0 && string.Equals(part.Substring(0, separator).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring(separator + 2).Trim();
                }
            }

            return null;
        }

        /// <summary>Removes surrounding double quotes and un-doubles embedded ones.</summary>
        public static string StripQuotes(string value)
        {
            if (value != null && value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2).Replace("\"\"", "\"");
            }

            return value ?? string.Empty;
        }

        /// <summary>
        /// Splits a CSV line, keeping commas inside double-quoted cells. RSLogix 5000 quotes
        /// descriptions and attributes, which contain commas.
        /// </summary>
        public static string[] ParseCsvLine(string line)
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
                    tokens.Add(line.Substring(start, i - start));
                    start = i + 1;
                }
            }

            tokens.Add(line.Substring(start));
            return tokens.ToArray();
        }

        /// <summary>Controller family term of the binding; the export comes from Studio 5000 (Logix).</summary>
        public const string ControllerFamily = nameof(EIPController.ControlLogix);

        /// <summary>
        /// Serializes a Thing Model without the null terms the shared model would write,
        /// adding the binding's Thing-level <c>eipv:controller</c> after <c>base</c>.
        /// </summary>
        public static string Serialize(ThingDescription thingModel)
        {
            JsonSerializer serializer = JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            Newtonsoft.Json.Linq.JObject json = Newtonsoft.Json.Linq.JObject.FromObject(thingModel, serializer);
            if (json.Property(EIPControllers.Term) == null)
            {
                json.Property("base")?.AddAfterSelf(new Newtonsoft.Json.Linq.JProperty(EIPControllers.Term, ControllerFamily));
            }
            return json.ToString(Formatting.Indented);
        }
    }
}
