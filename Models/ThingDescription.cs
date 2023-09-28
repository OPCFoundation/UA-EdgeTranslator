
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    public partial class ThingDescription
    {
        [JsonProperty("@context")]
        public Uri[] Context { get; set; }

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


        [JsonProperty("opcua:nodeId")]
        public string OpcUaObjectNode { get; set; }

        [JsonProperty("opcua:type")]
        public string OpcUaObjectType { get; set; }

        [JsonProperty("opcua:parent")]
        public string OpcUaParentNode { get; set; }
    }

    public partial class Property
    {
        [JsonProperty("type")]
        public TypeEnum Type { get; set; }

        [JsonProperty("readOnly")]
        public bool ReadOnly { get; set; }

        [JsonProperty("observable")]
        public bool Observable { get; set; }

        [JsonProperty("forms")]
        public object[] Forms { get; set; }
    }

    public partial class ModbusForm
    {
        [JsonProperty("href")]
        public string Href { get; set; }

        [JsonProperty("op")]
        public Op[] Op { get; set; }

        [JsonProperty("modbus:type")]
        public ModbusType ModbusType { get; set; }

        [JsonProperty("modbus:entity")]
        public ModbusEntity ModbusEntity { get; set; }

        [JsonProperty("modbus:pollingTime")]
        public long ModbusPollingTime { get; set; }


        [JsonProperty("opcua:node")]
        public string OpcUaVariableNode { get; set; }

        [JsonProperty("opcua:type")]
        public string OpcUaType { get; set; }
    }

    public partial class SecurityDefinitions
    {
        [JsonProperty("nosec_sc")]
        public NosecSc NosecSc { get; set; }
    }

    public partial class NosecSc
    {
        [JsonProperty("scheme")]
        public string Scheme { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum ModbusEntity
    {
        [EnumMember(Value = "holdingregister")]
        Holdingregister
    };

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum ModbusType
    {
        [EnumMember(Value = "float")]
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
