
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System;
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

        [JsonProperty("properties")]
        public Dictionary<string, Property> Properties { get; set; }

        [JsonProperty("opcua:type")]
        public string OpcUaType { get; set; }
    }

    public class OpcUaNamespaces
    {
        [JsonProperty("opcua")]
        public Uri[] Namespaces { get; set; }
    }

    public class Property
    {
        [JsonProperty("type")]
        public TypeEnum Type { get; set; }

        [JsonProperty("opcua:nodeId")]
        public string OpcUaNodeId { get; set; }

        [JsonProperty("opcua:type")]
        public string OpcUaType { get; set; }

        [JsonProperty("opcua:fieldPath")]
        public string OpcUaFieldPath { get; set; }

        [JsonProperty("readOnly")]
        public bool ReadOnly { get; set; }

        [JsonProperty("observable")]
        public bool Observable { get; set; }

        [JsonProperty("forms")]
        public object[] Forms { get; set; }
    }

    public class ModbusForm
    {
        [JsonProperty("href")]
        public string Href { get; set; }

        [JsonProperty("op")]
        public Op[] Op { get; set; }

        [JsonProperty("modv:type")]
        public ModbusType ModbusType { get; set; }

        [JsonProperty("modv:entity")]
        public ModbusEntity ModbusEntity { get; set; }

        [JsonProperty("modv:pollingTime")]
        public long ModbusPollingTime { get; set; }
    }

    public class SecurityDefinitions
    {
        [JsonProperty("nosec_sc")]
        public NosecSc NosecSc { get; set; }
    }

    public class NosecSc
    {
        [JsonProperty("scheme")]
        public string Scheme { get; set; }
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
