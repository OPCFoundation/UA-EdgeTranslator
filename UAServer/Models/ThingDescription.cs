
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

#nullable enable

    public class ThingDescription
    {
        [JsonProperty("@context")]
        public object[]? Context { get; set; }

        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("securityDefinitions")]
        public SecurityDefinitions? SecurityDefinitions { get; set; }

        [JsonProperty("security")]
        public string[]? Security { get; set; }

        [JsonProperty("@type")]
        public string[]? Type { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("base")]
        public string? Base { get; set; }

        [JsonProperty("title")]
        public string? Title { get; set; }

        [JsonProperty("properties")]
        public Dictionary<string, Property>? Properties { get; set; }

        [JsonProperty("actions")]
        public Dictionary<string, TDAction>? Actions { get; set; }
    }

    public class Property
    {
        [JsonProperty("type")]
        public TypeEnum Type { get; set; }

        [JsonProperty("const")]
        public object? Const { get; set; }

        [JsonProperty("uav:mapToNodeId")]
        public string? OpcUaNodeId { get; set; }

        [JsonProperty("uav:mapToType")]
        public string? OpcUaType { get; set; }

        [JsonProperty("uav:mapByFieldPath")]
        public string? OpcUaFieldPath { get; set; }

        [JsonProperty("readOnly")]
        public bool ReadOnly { get; set; }

        [JsonProperty("observable")]
        public bool Observable { get; set; }

        [JsonProperty("forms")]
        public object[]? Forms { get; set; }
    }

    public class TDAction
    {
        [JsonProperty("input")]
        public TDArguments? Input { get; set; }

        [JsonProperty("output")]
        public TDArguments? Output { get; set; }

        [JsonProperty("forms")]
        public object[]? Forms { get; set; }
    }

    public class TDArguments
    {
        [JsonProperty("type")]
        public TypeEnum Type { get; set; }

        [JsonProperty("properties")]
        public Dictionary<string, Property>? Properties { get; set; }

        [JsonProperty("required")]
        public string[]? Required { get; set; }
    }

    public class GenericForm
    {
        [JsonProperty("href")]
        public string? Href { get; set; }

        [JsonProperty("op")]
        public Op[]? Op { get; set; }

        [JsonProperty("type")]
        public TypeString Type { get; set; }

        [JsonProperty("pollingTime")]
        public long PollingTime { get; set; }
    }

    public class SecurityDefinitions
    {
        [JsonProperty("nosec_sc")]
        public NosecSc? NosecSc { get; set; }
    }

    public class NosecSc
    {
        [JsonProperty("scheme")]
        public string? Scheme { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum Op
    {
        [EnumMember(Value = "observeproperty")]
        Observeproperty,

        [EnumMember(Value = "readproperty")]
        Readproperty,

        [EnumMember(Value = "writeproperty")]
        Writeproperty
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

        [EnumMember(Value = "xsd:boolean")]
        Boolean,

        [EnumMember(Value = "xsd:integer")]
        Integer,

        [EnumMember(Value = "xsd:string")]
        String,

        [EnumMember(Value = "xsd:short")]
        Short,

        [EnumMember(Value = "xsd:byte")]
        Byte,

        [EnumMember(Value = "xsd:timedCommand")]
        TimedCommand
    };
}
