namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;

    /// <summary>
    /// Form vocabulary of the OPC UA protocol binding (conventional prefix <c>uav</c>).
    /// An action form names the method to call in <c>href</c> and the object it is
    /// called on in <c>uav:objectId</c>, both as expanded node ids.
    /// </summary>
    public class UAForm
    {
        [JsonProperty("href")]
        public string Href { get; set; }

        [JsonProperty("op", NullValueHandling = NullValueHandling.Ignore)]
        public Op[] Op { get; set; }

        [JsonProperty("type")]
        public TypeString Type { get; set; }

        [JsonProperty("pollingTime")]
        public long PollingTime { get; set; }

        /// <summary>
        /// For actions, the expanded node id of the object the method named by
        /// <see cref="Href"/> is called on.
        /// </summary>
        [JsonProperty("uav:objectId", NullValueHandling = NullValueHandling.Ignore)]
        public string ObjectId { get; set; }
    }
}
