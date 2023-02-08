
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;

    public partial class ThingDescription
    {
        [JsonProperty("@context")]
        public Uri[] Context { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("gms:hasValueType")]
        public string[] GmsHasValueType { get; set; }

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

        [JsonProperty("mlfb")]
        public string Mlfb { get; set; }

        [JsonProperty("properties")]
        public Dictionary<string, Property> Properties { get; set; }
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

        [JsonProperty("opcua:type")]
        public string OpcUaType { get; set; }

        [JsonProperty("modbus:entity")]
        public ModbusEntity ModbusEntity { get; set; }

        [JsonProperty("modbus:pollingTime")]
        public long ModbusPollingTime { get; set; }
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

    public enum ModbusEntity { Holdingregister };

    public enum ModbusType { Float };

    public enum Op { Observeproperty, Readproperty };

    public enum TypeEnum { Number };
}
