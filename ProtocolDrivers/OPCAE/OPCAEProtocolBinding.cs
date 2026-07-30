namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;

    // OPC A&E-specific WoT event form binding kept in the driver module by design.
    public class OpcAeEventForm
    {
        [JsonProperty("href")]
        public string Href { get; set; }

        [JsonProperty("categories")]
        public int[] Categories { get; set; }

        [JsonProperty("areas")]
        public string[] Areas { get; set; }

        [JsonProperty("sources")]
        public string[] Sources { get; set; }

        [JsonProperty("eventTypes")]
        public int EventTypes { get; set; } = 7;

        [JsonProperty("lowSeverity")]
        public int LowSeverity { get; set; } = 1;

        [JsonProperty("highSeverity")]
        public int HighSeverity { get; set; } = 1000;

        [JsonProperty("bufferTime")]
        public int BufferTime { get; set; } = 1000;

        [JsonProperty("maxEvents")]
        public int MaxEvents { get; set; } = 1000;

        [JsonProperty("refreshOnConnect")]
        public bool RefreshOnConnect { get; set; } = true;
    }
}
