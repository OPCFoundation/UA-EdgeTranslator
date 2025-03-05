
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System;
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
    }

    public class Property
    {
        [JsonProperty("type")]
        public TypeEnum Type { get; set; }

        [JsonProperty("opcua:nodeId")]
        public string? OpcUaNodeId { get; set; }

        [JsonProperty("opcua:type")]
        public string? OpcUaType { get; set; }

        [JsonProperty("opcua:fieldPath")]
        public string? OpcUaFieldPath { get; set; }

        [JsonProperty("readOnly")]
        public bool ReadOnly { get; set; }

        [JsonProperty("observable")]
        public bool Observable { get; set; }

        [JsonProperty("forms")]
        public object[]? Forms { get; set; }
    }

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

        [JsonProperty("modv:pollingTime")]
        public long ModbusPollingTime { get; set; }
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
    public enum ModbusEntity
    {
        [EnumMember(Value = "HoldingRegister")]
        HoldingRegister
    };

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
        String
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
        String
    };

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
