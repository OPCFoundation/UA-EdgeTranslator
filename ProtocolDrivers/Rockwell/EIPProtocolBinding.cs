
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System.Runtime.Serialization;

    /// <summary>
    /// Form vocabulary of the W3C WoT EtherNet/IP protocol binding
    /// (https://w3c.github.io/wot-binding-templates/bindings/protocols/ethernetip/),
    /// prefix <c>eipv</c>, namespace <see cref="BindingNamespace"/>.
    /// <para>
    /// Thing Descriptions written before the binding (<c>type</c> with <c>xsd:</c>
    /// values, <c>pollingTime</c>, <c>eip:structureDefinition</c>) are still read: the
    /// legacy terms are mapped onto the binding terms when the form is deserialized.
    /// </para>
    /// </summary>
    public class EIPForm
    {
        /// <summary>The provisional namespace of the binding vocabulary.</summary>
        public const string BindingNamespace = "https://www.w3.org/2026/wot/eip";

        /// <summary>The conventional prefix of the binding vocabulary.</summary>
        public const string Prefix = "eipv";

        [JsonProperty("href")]
        public string Href { get; set; }

        [JsonProperty("op")]
        public Op[] Op { get; set; }

        /// <summary>Media type of the payload; the binding requires <c>application/octet-stream</c>.</summary>
        [JsonProperty("contentType")]
        public string ContentType { get; set; } = OctetStream;

        /// <summary>The only content type the binding allows.</summary>
        public const string OctetStream = "application/octet-stream";

        /// <summary>Primitive type
        [JsonProperty("eipv:type", NullValueHandling = NullValueHandling.Ignore)]
        public EIPTypeString? Type { get; set; }

        /// <summary>CIP service name; the default services apply when absent.</summary>
        [JsonProperty("eipv:service", NullValueHandling = NullValueHandling.Ignore)]
        public string Service { get; set; }

        /// <summary>Requested polling interval for observeproperty, in milliseconds.</summary>
        [JsonProperty("eipv:pollingTime", NullValueHandling = NullValueHandling.Ignore)]
        public long? PollingTime { get; set; }

        /// <summary>Maximum time to wait for the controller response, in milliseconds.</summary>
        [JsonProperty("eipv:timeout", NullValueHandling = NullValueHandling.Ignore)]
        public long? Timeout { get; set; }

        /// <summary>Factor applied to the controller value: value = raw * scale + offset.</summary>
        [JsonProperty("eipv:scale", NullValueHandling = NullValueHandling.Ignore)]
        public double? Scale { get; set; }

        /// <summary>Summand applied after <see cref="Scale"/>.</summary>
        [JsonProperty("eipv:offset", NullValueHandling = NullValueHandling.Ignore)]
        public double? Offset { get; set; }

        [JsonProperty("eipv:structureDefinition", NullValueHandling = NullValueHandling.Ignore)]
        public EIPStructureDefinition StructureDefinition { get; set; }

        /// <summary>
        /// Proposed extension: the declared dimensions of an array tag, outermost first
        /// (Logix allows up to three, e.g. <c>[10]</c> or <c>[4, 5]</c>). Present when the
        /// form transfers several elements of <see cref="Type"/> instead of one value.
        /// </summary>
        [JsonProperty("eipv:dimensions", NullValueHandling = NullValueHandling.Ignore)]
        public int[] Dimensions { get; set; }

        /// <summary>
        /// Proposed extension: how many elements are transferred, starting at the element
        /// the href addresses (the CIP Read/Write Tag element count). Defaults to all
        /// elements of <see cref="Dimensions"/>.
        /// </summary>
        [JsonProperty("eipv:elementCount", NullValueHandling = NullValueHandling.Ignore)]
        public int? ElementCount { get; set; }

        /// <summary>True when the form transfers an array of elements.</summary>
        [JsonIgnore]
        public bool IsArray => Dimensions != null && Dimensions.Length > 0;

        /// <summary>The number of elements transferred; 1 for a scalar form.</summary>
        [JsonIgnore]
        public int EffectiveElementCount
        {
            get
            {
                if (!IsArray)
                {
                    return 1;
                }

                if (ElementCount.HasValue && ElementCount.Value > 0)
                {
                    return ElementCount.Value;
                }

                int total = 1;
                foreach (int dimension in Dimensions)
                {
                    total *= dimension;
                }

                return total;
            }
        }

        /// <summary>
        /// Checks the array terms against the proposed rules: one to three positive
        /// dimensions, an element that is a non-BOOL, non-STRING primitive or a structure
        /// (embedded or read from the controller), and an element count within the
        /// declared size. Returns null when
        /// the form is valid, otherwise the reason.
        /// </summary>
        public string ValidateArray()
        {
            if (!IsArray)
            {
                return ElementCount.HasValue ? "eipv:elementCount requires eipv:dimensions." : null;
            }

            if (Dimensions.Length > 3)
            {
                return "eipv:dimensions has more than three dimensions.";
            }

            foreach (int dimension in Dimensions)
            {
                if (dimension <= 0)
                {
                    return "eipv:dimensions must be positive.";
                }
            }

            if (StructureDefinition != null && Type != null)
            {
                return "An array form has an element eipv:type or an eipv:structureDefinition, not both.";
            }

            if (Type == EIPTypeString.BOOL)
            {
                return "BOOL arrays are stored as DINT bit strings; use eipv:DWORD.";
            }

            if (Type == EIPTypeString.STRING)
            {
                return "STRING arrays are not supported.";
            }

            if (Type == null && StructureDefinition == null)
            {
                return "An array form names its element type with eipv:type or eipv:structureDefinition.";
            }

            long declared = 1;
            foreach (int dimension in Dimensions)
            {
                declared *= dimension;
            }

            if (ElementCount.HasValue && (ElementCount.Value <= 0 || ElementCount.Value > declared))
            {
                return "eipv:elementCount must be between 1 and the number of declared elements.";
            }

            if (StructureDefinition != null && !(StructureDefinition.Size is int size && size >= 1))
            {
                return "An array of structures needs eipv:byteSize in its eipv:structureDefinition.";
            }

            return null;
        }

        /// <summary>
        /// Checks the form against every normative rule of the binding for the declared
        /// controller family. Returns null when the form is valid, otherwise the reason.
        /// </summary>
        public string Validate(EIPController controller)
        {
            if (string.IsNullOrWhiteSpace(Href))
            {
                return "href is required.";
            }

            if (Op == null || Op.Length == 0)
            {
                return "op is required.";
            }

            foreach (Op op in Op)
            {
                if (op != Models.Op.Readproperty && op != Models.Op.Writeproperty && op != Models.Op.Observeproperty)
                {
                    return $"The operation {op} has no EtherNet/IP mapping.";
                }
            }

            if (ContentType != OctetStream)
            {
                return $"contentType must be {OctetStream}.";
            }

            if (Type != null && StructureDefinition != null)
            {
                return "A form has an eipv:type or an eipv:structureDefinition, not both.";
            }

            if (Scale.HasValue && Scale.Value == 0.0)
            {
                return "eipv:scale must not be zero.";
            }

            if (PollingTime.HasValue && PollingTime.Value < 0)
            {
                return "eipv:pollingTime must not be negative.";
            }

            if (Timeout.HasValue && Timeout.Value < 0)
            {
                return "eipv:timeout must not be negative.";
            }

            string serviceProblem = ValidateService(controller);
            if (serviceProblem != null)
            {
                return serviceProblem;
            }

            if (EIPControllers.IsPccc(controller))
            {
                if (StructureDefinition != null || Dimensions != null || ElementCount.HasValue)
                {
                    return "eipv:structureDefinition, eipv:dimensions and eipv:elementCount apply to the symbolic families only.";
                }

                if (Href.IndexOf('?') >= 0)
                {
                    return "The ?offset= query applies to the symbolic families only.";
                }

                if (Type == null)
                {
                    return "A PCCC file address needs an eipv:type.";
                }
            }

            string arrayProblem = ValidateArray();
            if (arrayProblem != null)
            {
                return arrayProblem;
            }

            if (IsArray && StructureDefinition != null && (Scale.HasValue || Offset.HasValue))
            {
                return "eipv:scale and eipv:offset must not be used with an array of structures.";
            }

            return StructureDefinition?.Validate(IsArray);
        }

        /// <summary>Checks <see cref="Service"/> against the operations and the controller family.</summary>
        private string ValidateService(EIPController controller)
        {
            if (Service == null)
            {
                return null;
            }

            if (!EIPServices.IsKnown(Service))
            {
                return $"Unknown eipv:service {Service}.";
            }

            if (EIPControllers.IsPccc(controller) != (Service == EIPServices.ExecutePCCC))
            {
                return $"The {controller} controller family does not support eipv:service {Service}.";
            }

            foreach (Op op in Op)
            {
                bool isWrite = op == Models.Op.Writeproperty;
                if ((EIPServices.IsRead(Service) && isWrite) || (EIPServices.IsWrite(Service) && !isWrite))
                {
                    return $"eipv:service {Service} does not perform the operation {op}.";
                }
            }

            return null;
        }

        // Legacy terms, read but never written.

        /// <summary>
        /// A legacy form gave every structured tag a placeholder <c>type</c> (always
        /// <c>xsd:BOOL</c>) next to its definition. The binding allows only one of the two,
        /// so once the form is read the structure definition wins.
        /// </summary>
        [System.Runtime.Serialization.OnDeserialized]
        private void OnDeserialized(System.Runtime.Serialization.StreamingContext context)
        {
            if (StructureDefinition != null && _typeIsLegacy)
            {
                Type = null;
            }
        }

        private bool _typeIsLegacy;

        [JsonProperty("type")]
        private string LegacyType
        {
            set
            {
                if (Type == null && EIPTypes.TryParse(value, out EIPTypeString type))
                {
                    Type = type;
                    _typeIsLegacy = true;
                }
            }
        }

        [JsonProperty("pollingTime")]
        private long? LegacyPollingTime { set => PollingTime ??= value; }

        [JsonProperty("eip:structureDefinition")]
        private EIPStructureDefinition LegacyStructureDefinition { set => StructureDefinition ??= value; }

        /// <summary>The polling interval, with the driver default when none is given.</summary>
        [JsonIgnore]
        public long EffectivePollingTime => PollingTime.HasValue && PollingTime.Value > 0 ? PollingTime.Value : 1000;

        /// <summary>True when <c>eipv:scale</c> or <c>eipv:offset</c> changes the value.</summary>
        [JsonIgnore]
        public bool HasValueConversion => (Scale.HasValue && Scale.Value != 1.0) || (Offset.HasValue && Offset.Value != 0.0);

        /// <summary>Applies <c>eipv:scale</c> and <c>eipv:offset</c> to a value read from the controller.</summary>
        public object ToApplicationValue(object raw)
        {
            if (raw == null || !HasValueConversion)
            {
                return raw;
            }

            return System.Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture) * (Scale ?? 1.0) + (Offset ?? 0.0);
        }

        /// <summary>
        /// Applies the inverse conversion to a value written by the application,
        /// rounding to the nearest representable value of the declared integer type.
        /// </summary>
        public object ToControllerValue(object value)
        {
            if (value == null || !HasValueConversion)
            {
                return value;
            }

            double raw = (System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) - (Offset ?? 0.0)) / (Scale ?? 1.0);
            if (Type == null || Type == EIPTypeString.REAL || Type == EIPTypeString.LREAL)
            {
                return raw;
            }

            // The nearest representable value of the declared integer type.
            raw = System.Math.Round(raw, System.MidpointRounding.AwayFromZero);
            double min;
            double max;
            switch (Type)
            {
                case EIPTypeString.SINT: min = sbyte.MinValue; max = sbyte.MaxValue; break;
                case EIPTypeString.INT: min = short.MinValue; max = short.MaxValue; break;
                case EIPTypeString.DINT: min = int.MinValue; max = int.MaxValue; break;
                case EIPTypeString.LINT: min = long.MinValue; max = long.MaxValue; break;
                case EIPTypeString.USINT:
                case EIPTypeString.BYTE: min = byte.MinValue; max = byte.MaxValue; break;
                case EIPTypeString.UINT:
                case EIPTypeString.WORD: min = ushort.MinValue; max = ushort.MaxValue; break;
                case EIPTypeString.UDINT:
                case EIPTypeString.DWORD: min = uint.MinValue; max = uint.MaxValue; break;
                case EIPTypeString.ULINT:
                case EIPTypeString.LWORD: min = ulong.MinValue; max = ulong.MaxValue; break;
                default: return raw;
            }

            // long/ulong bounds round up as doubles, so stay just inside them.
            if (raw >= max)
            {
                return max >= long.MaxValue ? System.BitConverter.Int64BitsToDouble(System.BitConverter.DoubleToInt64Bits(max) - 1) : max;
            }

            return raw <= min ? min : raw;
        }
    }

    /// <summary>A structured Logix tag or UDT (<c>eipv:structureDefinition</c>).</summary>
    public class EIPStructureDefinition
    {
        [JsonProperty("eipv:typeName")]
        public string TypeName { get; set; }

        [JsonProperty("eipv:fields")]
        public EIPFieldDefinition[] Fields { get; set; }

        /// <summary>
        /// <c>eipv:byteSize</c>: the padded size of one instance in bytes, as the Logix
        /// template reports it; the element stride of an array of this structure.
        /// </summary>
        [JsonProperty("eipv:byteSize", NullValueHandling = NullValueHandling.Ignore)]
        public int? Size { get; set; }

        [JsonProperty("eipv:size")]
        private int? LegacySize { set => Size ??= value; }

        /// <summary>
        /// Checks the definition, recursively: a type name and fields, a byte size when it
        /// is an array element, and per field either a primitive type or a nested
        /// structure, the bit of a BOOL, and valid array dimensions.
        /// </summary>
        public string Validate(bool isArrayElement = false)
        {
            if (string.IsNullOrWhiteSpace(TypeName))
            {
                return "eipv:structureDefinition needs eipv:typeName.";
            }

            if (Fields == null)
            {
                return $"eipv:structureDefinition {TypeName} needs eipv:fields.";
            }

            if (isArrayElement && !(Size is int size && size >= 1))
            {
                return $"eipv:structureDefinition {TypeName} is an array element and needs eipv:byteSize.";
            }

            if (Size is int declared && declared < 1)
            {
                return $"eipv:byteSize of {TypeName} must be at least 1.";
            }

            foreach (EIPFieldDefinition field in Fields)
            {
                string problem = field == null ? $"{TypeName} has an empty field." : field.Validate(TypeName);
                if (problem != null)
                {
                    return problem;
                }
            }

            return null;
        }

        [JsonProperty("typeName")]
        private string LegacyTypeName { set => TypeName ??= value; }

        [JsonProperty("fields")]
        private EIPFieldDefinition[] LegacyFields { set => Fields ??= value; }
    }

    /// <summary>One member of a structure definition.</summary>
    public class EIPFieldDefinition
    {
        [JsonProperty("eipv:name")]
        public string Name { get; set; }

        /// <summary>Primitive type of the field; absent for a nested UDT field.</summary>
        [JsonProperty("eipv:type", NullValueHandling = NullValueHandling.Ignore)]
        public EIPTypeString? Type { get; set; }

        /// <summary>Byte offset of the field in the encoded structure.</summary>
        [JsonProperty("eipv:byteOffset")]
        public int ByteOffset { get; set; }

        /// <summary>Bit of a packed BOOL member inside the byte at <see cref="ByteOffset"/>.</summary>
        [JsonProperty("eipv:bitOffset", NullValueHandling = NullValueHandling.Ignore)]
        public int? BitOffset { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// <summary>Nested UDT definition, processed recursively.</summary>
        [JsonProperty("eipv:structureDefinition", NullValueHandling = NullValueHandling.Ignore)]
        public EIPStructureDefinition StructureDefinition { get; set; }

        /// <summary>Declared dimensions of an array member, outermost first.</summary>
        [JsonProperty("eipv:dimensions", NullValueHandling = NullValueHandling.Ignore)]
        public int[] Dimensions { get; set; }

        /// <summary>True when the field is an array member.</summary>
        [JsonIgnore]
        public bool IsArray => Dimensions != null && Dimensions.Length > 0;

        /// <summary>The number of elements of an array member (the product of its dimensions); 1 otherwise.</summary>
        [JsonIgnore]
        public int ElementCount
        {
            get
            {
                int total = 1;
                if (IsArray)
                {
                    foreach (int dimension in Dimensions)
                    {
                        total *= dimension;
                    }
                }

                return total;
            }
        }

        /// <summary>Checks the field; returns null when valid, otherwise the reason.</summary>
        public string Validate(string typeName)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return $"A field of {typeName} needs eipv:name.";
            }

            if ((Type == null) == (StructureDefinition == null))
            {
                return $"Field {typeName}.{Name} needs either eipv:type or eipv:structureDefinition.";
            }

            if (ByteOffset < 0)
            {
                return $"Field {typeName}.{Name} has a negative eipv:byteOffset.";
            }

            if (Type == EIPTypeString.BOOL && !BitOffset.HasValue)
            {
                return $"BOOL field {typeName}.{Name} needs eipv:bitOffset.";
            }

            if (BitOffset.HasValue && (BitOffset.Value < 0 || BitOffset.Value > 7))
            {
                return $"eipv:bitOffset of {typeName}.{Name} must be between 0 and 7.";
            }

            if (Dimensions != null)
            {
                if (Dimensions.Length < 1 || Dimensions.Length > 3)
                {
                    return $"eipv:dimensions of {typeName}.{Name} must have one to three dimensions.";
                }

                foreach (int dimension in Dimensions)
                {
                    if (dimension < 1)
                    {
                        return $"eipv:dimensions of {typeName}.{Name} must be positive.";
                    }
                }

                if (Type == EIPTypeString.BOOL || Type == EIPTypeString.STRING)
                {
                    return $"Field {typeName}.{Name}: {Type} must not be used with eipv:dimensions.";
                }
            }

            return StructureDefinition?.Validate(IsArray);
        }

        [JsonProperty("name")]
        private string LegacyName { set => Name ??= value; }

        [JsonProperty("type")]
        private string LegacyType
        {
            set
            {
                if (Type == null && EIPTypes.TryParse(value, out EIPTypeString type))
                {
                    Type = type;
                }
            }
        }

        [JsonProperty("offset")]
        private int? LegacyOffset
        {
            set
            {
                if (value.HasValue && ByteOffset == 0)
                {
                    ByteOffset = value.Value;
                }
            }
        }

        [JsonProperty("eip:structureDefinition")]
        private EIPStructureDefinition LegacyStructureDefinition { set => StructureDefinition ??= value; }
    }

    /// <summary>Logix primitive types of the binding (<c>eipv:BOOL</c> ... <c>eipv:STRING</c>).</summary>
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum EIPTypeString
    {
        [EnumMember(Value = "eipv:BOOL")]
        BOOL,

        [EnumMember(Value = "eipv:SINT")]
        SINT,

        [EnumMember(Value = "eipv:INT")]
        INT,

        [EnumMember(Value = "eipv:DINT")]
        DINT,

        [EnumMember(Value = "eipv:LINT")]
        LINT,

        [EnumMember(Value = "eipv:USINT")]
        USINT,

        [EnumMember(Value = "eipv:UINT")]
        UINT,

        [EnumMember(Value = "eipv:UDINT")]
        UDINT,

        [EnumMember(Value = "eipv:ULINT")]
        ULINT,

        [EnumMember(Value = "eipv:REAL")]
        REAL,

        [EnumMember(Value = "eipv:LREAL")]
        LREAL,

        [EnumMember(Value = "eipv:BYTE")]
        BYTE,

        [EnumMember(Value = "eipv:WORD")]
        WORD,

        [EnumMember(Value = "eipv:DWORD")]
        DWORD,

        [EnumMember(Value = "eipv:LWORD")]
        LWORD,

        [EnumMember(Value = "eipv:STRING")]
        STRING
    }

    /// <summary>Parses and names <see cref="EIPTypeString"/> values.</summary>
    public static class EIPTypes
    {
        /// <summary>
        /// Parses a type written as <c>eipv:REAL</c>, as the legacy <c>xsd:REAL</c>, or bare.
        /// </summary>
        public static bool TryParse(string text, out EIPTypeString type)
        {
            type = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int colon = text.IndexOf(':');
            string name = colon >= 0 ? text.Substring(colon + 1) : text;
            return System.Enum.TryParse(name.Trim(), true, out type) && System.Enum.IsDefined(typeof(EIPTypeString), type);
        }

        /// <summary>The binding term for <paramref name="type"/>, such as <c>eipv:REAL</c>.</summary>
        public static string ToTerm(EIPTypeString type) => EIPForm.Prefix + ":" + type;

        /// <summary>
        /// The minimum and maximum of the WoT data schema the binding recommends for a
        /// Logix integer type; null where the binding gives none.
        /// </summary>
        public static void Bounds(EIPTypeString type, out long? minimum, out long? maximum)
        {
            minimum = null;
            maximum = null;
            switch (type)
            {
                case EIPTypeString.SINT: minimum = sbyte.MinValue; maximum = sbyte.MaxValue; break;
                case EIPTypeString.INT: minimum = short.MinValue; maximum = short.MaxValue; break;
                case EIPTypeString.DINT: minimum = int.MinValue; maximum = int.MaxValue; break;
                case EIPTypeString.USINT:
                case EIPTypeString.BYTE: minimum = 0; maximum = byte.MaxValue; break;
                case EIPTypeString.UINT:
                case EIPTypeString.WORD: minimum = 0; maximum = ushort.MaxValue; break;
                case EIPTypeString.UDINT:
                case EIPTypeString.DWORD:
                case EIPTypeString.ULINT:
                case EIPTypeString.LWORD: minimum = 0; break;
            }
        }
    }

    /// <summary>The controller families of the binding (<c>eipv:controller</c>).</summary>
    public enum EIPController
    {
        ControlLogix,
        CompactLogix,
        Micro800,
        MicroLogix,
        SLC500,
        PLC5,
        Omron
    }

    /// <summary>Reads <c>eipv:controller</c> and classifies the controller families.</summary>
    public static class EIPControllers
    {
        /// <summary>The Thing-level term naming the controller family.</summary>
        public const string Term = EIPForm.Prefix + ":controller";

        /// <summary>The family assumed when <c>eipv:controller</c> is absent.</summary>
        public const EIPController Default = EIPController.ControlLogix;

        /// <summary>True for the families accessed with PCCC file addresses.</summary>
        public static bool IsPccc(EIPController controller) =>
            controller == EIPController.MicroLogix || controller == EIPController.SLC500 || controller == EIPController.PLC5;

        /// <summary>True for the families whose tag list and UDT templates can be browsed.</summary>
        public static bool IsBrowsable(EIPController controller) =>
            controller == EIPController.ControlLogix || controller == EIPController.CompactLogix;

        /// <summary>Parses a family name exactly as the binding writes it.</summary>
        public static bool TryParse(string text, out EIPController controller)
        {
            controller = Default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (EIPController value in System.Enum.GetValues(typeof(EIPController)))
            {
                if (string.Equals(value.ToString(), text.Trim(), System.StringComparison.Ordinal))
                {
                    controller = value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The family a Thing Description declares with <c>eipv:controller</c>, or
        /// <see cref="Default"/> when absent. An unknown family is rejected.
        /// </summary>
        public static EIPController FromThingDescription(ThingDescription td)
        {
            if (td?.AdditionalData == null || !td.AdditionalData.TryGetValue(Term, out object value) || value == null)
            {
                return Default;
            }

            string text = value.ToString();
            if (!TryParse(text, out EIPController controller))
            {
                throw new System.ArgumentException($"Unknown {Term} '{text}'.");
            }

            return controller;
        }
    }

    /// <summary>The CIP service names of <c>eipv:service</c>.</summary>
    public static class EIPServices
    {
        public const string ReadTag = "ReadTag";

        public const string ReadTagFragmented = "ReadTagFragmented";

        public const string WriteTag = "WriteTag";

        public const string WriteTagFragmented = "WriteTagFragmented";

        public const string ExecutePCCC = "ExecutePCCC";

        public static bool IsKnown(string service) =>
            IsRead(service) || IsWrite(service) || service == ExecutePCCC;

        public static bool IsRead(string service) => service == ReadTag || service == ReadTagFragmented;

        public static bool IsWrite(string service) => service == WriteTag || service == WriteTagFragmented;
    }
}
