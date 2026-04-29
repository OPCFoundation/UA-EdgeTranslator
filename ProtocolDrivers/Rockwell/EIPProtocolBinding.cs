
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System.Runtime.Serialization;

#nullable enable

    public class EIPForm
    {
        [JsonProperty("href")]
        public string? Href { get; set; }

        [JsonProperty("op")]
        public Op[]? Op { get; set; }

        [JsonProperty("type")]
        public EIPTypeString Type { get; set; }

        [JsonProperty("pollingTime")]
        public long PollingTime { get; set; }

        [JsonProperty("eip:structureDefinition")]
        public EIPStructureDefinition? StructureDefinition { get; set; }
    }

    public class EIPStructureDefinition
    {
        [JsonProperty("typeName")]
        public string? TypeName { get; set; }

        [JsonProperty("fields")]
        public EIPFieldDefinition[]? Fields { get; set; }
    }

    public class EIPFieldDefinition
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        /// <summary>
        /// For primitive fields: "xsd:DINT", "xsd:REAL", etc.
        /// For nested UDT fields: null (use structureDefinition instead).
        /// </summary>
        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("offset")]
        public int Offset { get; set; }

        [JsonProperty("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Present when this field is a nested UDT. Contains the full
        /// structure definition of the nested type, enabling recursive
        /// OPC UA StructureType creation.
        /// </summary>
        [JsonProperty("eip:structureDefinition")]
        public EIPStructureDefinition? StructureDefinition { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum EIPTypeString
    {
        [EnumMember(Value = "xsd:BOOL")]
        BOOL,

        [EnumMember(Value = "xsd:SINT")]
        SINT,

        [EnumMember(Value = "xsd:INT")]
        INT,

        [EnumMember(Value = "xsd:DINT")]
        DINT,

        [EnumMember(Value = "xsd:LINT")]
        LINT,

        [EnumMember(Value = "xsd:USINT")]
        USINT,

        [EnumMember(Value = "xsd:UINT")]
        UINT,

        [EnumMember(Value = "xsd:UDINT")]
        UDINT,

        [EnumMember(Value = "xsd:ULINT")]
        ULINT,

        [EnumMember(Value = "xsd:REAL")]
        REAL,

        [EnumMember(Value = "xsd:LREAL")]
        LREAL,

        [EnumMember(Value = "xsd:STRING")]
        STRING
    };
}
