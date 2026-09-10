
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    public class ThingDescription
    {
        [JsonProperty("@context")]
        public object[] Context { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("securityDefinitions")]
        public SecurityDefinitions SecurityDefinitions { get; set; }

        [JsonProperty("security")]
        public string[] Security { get; set; }

        [JsonProperty("@type")]
        public string[] Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("base")]
        public string Base { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("properties")]
        public Dictionary<string, Property> Properties { get; set; }

        [JsonProperty("actions")]
        public Dictionary<string, TDAction> Actions { get; set; }

        [JsonProperty("events")]
        public Dictionary<string, TDEvent> Events { get; set; }

        /// <summary>
        /// Thing-level terms contributed by protocol bindings, e.g. the W3C WoT
        /// LoRaWAN binding's <c>lorav:devEUI</c> and <c>lorav:joinEUI</c>.
        /// <para>
        /// Bindings define their own thing-level vocabulary, so these cannot be
        /// enumerated here without the model needing an edit for every binding.
        /// Capturing them keeps round-tripping lossless and lets a driver read
        /// the terms it understands.
        /// </para>
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, object> AdditionalData { get; set; }
    }

    public class Property
    {
        [JsonProperty("type")]
        public TypeEnum Type { get; set; }

        [JsonProperty("const")]
        public object Const { get; set; }

        [JsonProperty("uav:mapToNodeId")]
        public string OpcUaNodeId { get; set; }

        [JsonProperty("uav:mapToType")]
        public string OpcUaType { get; set; }

        [JsonProperty("uav:mapByFieldPath")]
        public string OpcUaFieldPath { get; set; }

        [JsonProperty("readOnly")]
        public bool ReadOnly { get; set; }

        [JsonProperty("observable")]
        public bool Observable { get; set; }

        [JsonProperty("forms")]
        public object[] Forms { get; set; }
    }

    public class TDAction
    {
        [JsonProperty("input")]
        public TDArguments Input { get; set; }

        [JsonProperty("output")]
        public TDArguments Output { get; set; }

        [JsonProperty("forms")]
        public object[] Forms { get; set; }
    }

    public class TDArguments
    {
        [JsonProperty("type")]
        public TypeEnum Type { get; set; }

        [JsonProperty("properties")]
        public Dictionary<string, Property> Properties { get; set; }

        [JsonProperty("required")]
        public string[] Required { get; set; }
    }

    public class TDEvent
    {
        [JsonProperty("@type", NullValueHandling = NullValueHandling.Ignore)]
        public string[] Type { get; set; }

        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("titles", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> Titles { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("descriptions", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> Descriptions { get; set; }

        [JsonProperty("uriVariables", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> UriVariables { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }

        [JsonProperty("subscription", NullValueHandling = NullValueHandling.Ignore)]
        public object Subscription { get; set; }

        [JsonProperty("cancellation", NullValueHandling = NullValueHandling.Ignore)]
        public object Cancellation { get; set; }

        [JsonProperty("dataResponse", NullValueHandling = NullValueHandling.Ignore)]
        public object DataResponse { get; set; }

        [JsonProperty("forms")]
        public object[] Forms { get; set; }
    }

    public class GenericForm
    {
        [JsonProperty("href")]
        public string Href { get; set; }

        [JsonProperty("op")]
        public Op[] Op { get; set; }

        [JsonProperty("type")]
        public TypeString Type { get; set; }

        [JsonProperty("pollingTime")]
        public long PollingTime { get; set; }
    }

    public class SecurityDefinitions
    {
        [JsonProperty("nosec_sc", NullValueHandling = NullValueHandling.Ignore)]
        public NosecSc NosecSc { get; set; }

        /// <summary>
        /// OTAA security scheme used by the W3C WoT LoRaWAN binding, which
        /// requires root keys to be declared as <c>apikey</c> schemes (named
        /// <c>appKey</c>, and <c>nwkKey</c> for LoRaWAN 1.1.x) with their values
        /// injected at runtime rather than written into the Thing Description.
        /// </summary>
        [JsonProperty("otaa_sc", NullValueHandling = NullValueHandling.Ignore)]
        public ApiKeySc OtaaSc { get; set; }
    }

    public class NosecSc
    {
        [JsonProperty("scheme")]
        public string Scheme { get; set; }
    }

    /// <summary>
    /// A WoT <c>apikey</c> security scheme. It names the credential but never
    /// carries its value.
    /// </summary>
    public class ApiKeySc
    {
        [JsonProperty("scheme")]
        public string Scheme { get; set; }

        [JsonProperty("in", NullValueHandling = NullValueHandling.Ignore)]
        public string In { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum Op
    {
        [EnumMember(Value = "observeproperty")]
        Observeproperty,

        [EnumMember(Value = "readproperty")]
        Readproperty,

        [EnumMember(Value = "writeproperty")]
        Writeproperty,

        [EnumMember(Value = "subscribeevent")]
        Subscribeevent,

        [EnumMember(Value = "unsubscribeevent")]
        Unsubscribeevent
    };

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum TypeEnum
    {
        [EnumMember(Value = "number")]
        Number,

        [EnumMember(Value = "boolean")]
        Boolean,

        [EnumMember(Value = "integer")]
        Integer,

        [EnumMember(Value = "string")]
        String,

        [EnumMember(Value = "object")]
        Object
    };

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum TypeString
    {
        [EnumMember(Value = "xsd:float")]
        Float,

        [EnumMember(Value = "xsd:double")]
        Double,

        [EnumMember(Value = "xsd:boolean")]
        Boolean,

        [EnumMember(Value = "xsd:short")]
        Short,

        [EnumMember(Value = "xsd:integer")]
        Integer,

        [EnumMember(Value = "xsd:string")]
        String,

        [EnumMember(Value = "xsd:byte")]
        Byte,

        [EnumMember(Value = "xsd:timedCommand")]
        TimedCommand,

        [EnumMember(Value = "xsd:long")]
        Long,

        [EnumMember(Value = "xsd:unsignedLong")]
        UnsignedLong,

        [EnumMember(Value = "xsd:dateTime")]
        DateTime,

        [EnumMember(Value = "xsd:duration")]
        Duration
    };
}
