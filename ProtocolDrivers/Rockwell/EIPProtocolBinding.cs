
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
        LREAL
    };
}
