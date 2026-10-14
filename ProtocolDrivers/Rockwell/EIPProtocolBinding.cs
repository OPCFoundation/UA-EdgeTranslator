
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

        /// <summary>Primitive type of the tag; absent for a structured tag.</summary>
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

            long declared = 1;
            foreach (int dimension in Dimensions)
            {
                declared *= dimension;
            }

            if (ElementCount.HasValue && (ElementCount.Value <= 0 || ElementCount.Value > declared))
            {
                return "eipv:elementCount must be between 1 and the number of declared elements.";
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
            return Type == null || Type == EIPTypeString.REAL || Type == EIPTypeString.LREAL ? raw : System.Math.Round(raw, System.MidpointRounding.AwayFromZero);
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
        /// Proposed extension: the padded size of one instance in bytes, as the Logix
        /// template reports it; the element stride of an array of this structure.
        /// </summary>
        [JsonProperty("eipv:size", NullValueHandling = NullValueHandling.Ignore)]
        public int? Size { get; set; }

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
    }
}
