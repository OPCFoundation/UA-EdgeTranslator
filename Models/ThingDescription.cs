
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

    public class OpcUaNamespaces
    {
        [JsonProperty("opcua")]
        public Uri[]? Namespaces { get; set; }
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
        public ModbusType ModbusType { get; set; }

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
    }

    public class OPCUAForm
    {
        [JsonProperty("href")]
        public string? Href { get; set; }

        [JsonProperty("op")]
        public Op[]? Op { get; set; }

        [JsonProperty("opcua:type")]
        public OPCUAType OPCUAType { get; set; }

        [JsonProperty("opcua:pollingTime")]
        public long OPCUAPollingTime { get; set; }
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

        [JsonProperty("s7:type")]
        public S7Type S7Type { get; set; }

        [JsonProperty("s7:target")]
        public S7Target S7Target { get; set; }

        [JsonProperty("s7:address")]
        public string? S7Address { get; set; }

        [JsonProperty("s7:pollingTime")]
        public long S7PollingTime { get; set; }
    }

    public class EIPForm
    {
        [JsonProperty("href")]
        public string? Href { get; set; }

        [JsonProperty("op")]
        public Op[]? Op { get; set; }

        [JsonProperty("eip:type")]
        public EIPType EIPType { get; set; }

        [JsonProperty("eip:pollingTime")]
        public long EIPPollingTime { get; set; }
    }

    public class ADSForm
    {
        [JsonProperty("href")]
        public string? Href { get; set; }

        [JsonProperty("op")]
        public Op[]? Op { get; set; }

        [JsonProperty("ads:type")]
        public ADSType ADSType { get; set; }

        [JsonProperty("ads:pollingTime")]
        public long ADSPollingTime { get; set; }
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
    public enum ModbusType
    {
        [EnumMember(Value = "xsd:float")]
        Float
    };

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum OPCUAType
    {
        [EnumMember(Value = "xsd:float")]
        Float
    };

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum S7Type
    {
        [EnumMember(Value = "xsd:float")]
        Float
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
    public enum EIPType
    {
        [EnumMember(Value = "xsd:float")]
        Float
    };

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum ADSType
    {
        [EnumMember(Value = "xsd:float")]
        Float
    };

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum Op
    {
        [EnumMember(Value = "observeproperty")]
        Observeproperty,

        [EnumMember(Value = "readproperty")]
        Readproperty
    };

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum TypeEnum
    {
        [EnumMember(Value = "number")]
        Number
    };
}
