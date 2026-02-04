
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;

#nullable enable

    public class LoRaWANForm
    {
        [JsonProperty("href")]
        public string? Href { get; set; }

        [JsonProperty("op")]
        public Op[]? Op { get; set; }

        [JsonProperty("type")]
        public TypeString Type { get; set; }

        [JsonProperty("lorav:mostSignificantByte")]
        public bool MostSignificantByte { get; set; } // big endian on a per byte basis

        [JsonProperty("lorav:mostSignificantWord")]
        public bool MostSignificantWord { get; set; } // big endian on a per word basis

        [JsonProperty("lorav:bitmask")]
        public string? BitMask { get; set; } // bitmask to apply to the value

        [JsonProperty("lorav:multiplier")]
        public float? Multiplier { get; set; } // multiplier to multiply the value with to get the correct unit of measure

        [JsonProperty("pollingTime")]
        public long PollingTime { get; set; }
    }
}
