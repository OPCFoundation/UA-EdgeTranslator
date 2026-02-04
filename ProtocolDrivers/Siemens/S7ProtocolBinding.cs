
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System.Runtime.Serialization;

#nullable enable

    public class S7Form
    {
        [JsonProperty("href")]
        public string? Href { get; set; }

        [JsonProperty("op")]
        public Op[]? Op { get; set; }

        [JsonProperty("s7:rack")]
        public int S7Rack { get; set; }

        [JsonProperty("s7:slot")]
        public int S7Slot { get; set; }

        [JsonProperty("s7:dbnumber")]
        public int S7DBNumber { get; set; }

        [JsonProperty("s7:start")]
        public int S7Start { get; set; }

        [JsonProperty("s7:size")]
        public int S7Size { get; set; }

        [JsonProperty("s7:pos")]
        public int S7Pos { get; set; }

        [JsonProperty("s7:maxlen")]
        public int S7MaxLen { get; set; }

        [JsonProperty("type")]
        public TypeString Type { get; set; }

        [JsonProperty("s7:target")]
        public S7Target S7Target { get; set; }

        [JsonProperty("s7:address")]
        public string? S7Address { get; set; }

        [JsonProperty("pollingTime")]
        public long PollingTime { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum S7Target
    {
        [EnumMember(Value = "DB")]
        DataBlock,

        [EnumMember(Value = "MB")]
        Merker,

        [EnumMember(Value = "EB")]
        IPIProcessInput,

        [EnumMember(Value = "AB")]
        IPUProcessInput,

        [EnumMember(Value = "TM")]
        Timer,

        [EnumMember(Value = "CT")]
        Counter
    };
}
