
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System.Runtime.Serialization;

#nullable enable

    public class ModbusForm
    {
        [JsonProperty("href")]
        public string? Href { get; set; }

        [JsonProperty("op")]
        public Op[]? Op { get; set; }

        [JsonProperty("modv:type")]
        public TypeString ModbusType { get; set; }

        [JsonProperty("modv:entity")]
        public ModbusEntity ModbusEntity { get; set; }

        [JsonProperty("modv:mostSignificantByte")]
        public bool MostSignificantByte { get; set; } // big endian on a per byte basis

        [JsonProperty("modv:mostSignificantWord")]
        public bool MostSignificantWord { get; set; } // big endian on a per word basis

        [JsonProperty("modv:pollingTime")]
        public long ModbusPollingTime { get; set; }

        [JsonProperty("modv:scaling")]
        public float ModbusScaler { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum ModbusEntity
    {
        [EnumMember(Value = "HoldingRegister")]
        HoldingRegister,
        [EnumMember(Value = "InputRegister")]
        InputRegister
    };
}
