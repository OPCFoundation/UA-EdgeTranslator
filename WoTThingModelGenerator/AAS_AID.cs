
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class AAS_AID
    {
        [JsonProperty("idShort")]
        public string? IdShort { get; set; }

        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("kind")]
        public string? Kind { get; set; }

        [JsonProperty("semanticId")]
        public SemanticId? SemanticId { get; set; }

        [JsonProperty("description")]
        public List<Description>? Description { get; set; }

        [JsonProperty("submodelElements")]
        public List<SubmodelElement>? SubmodelElements { get; set; }

        [JsonProperty("modelType")]
        public string? ModelType { get; set; }
    }

    public class Description
    {
        [JsonProperty("language")]
        public string? Language { get; set; }

        [JsonProperty("text")]
        public string? Text { get; set; }
    }

    public class Key
    {
        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("value")]
        public string? Value { get; set; }
    }

    public class SemanticId
    {
        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("keys")]
        public List<Key>? Keys { get; set; }
    }

    public class SubmodelElement
    {
        [JsonProperty("idShort")]
        public string? IdShort { get; set; }

        [JsonProperty("semanticId")]
        public SemanticId? SemanticId { get; set; }

        [JsonProperty("supplementalSemanticIds")]
        public List<SupplementalSemanticId>? SupplementalSemanticIds { get; set; }

        [JsonProperty("value")]
        public List<AASValue>? Value { get; set; }

        [JsonProperty("modelType")]
        public string? ModelType { get; set; }
    }

    public class SupplementalSemanticId
    {
        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("keys")]
        public List<Key>? Keys { get; set; }
    }

    public class AASValue
    {
        [JsonProperty("idShort")]
        public string? IdShort { get; set; }

        [JsonProperty("valueType")]
        public string? ValueType { get; set; }

        [JsonProperty("value")]
        public object? Value { get; set; }

        [JsonProperty("modelType")]
        public string? ModelType { get; set; }

        [JsonProperty("semanticId")]
        public SemanticId? SemanticId { get; set; }

        [JsonProperty("supplementalSemanticIds")]
        public List<SupplementalSemanticId>? SupplementalSemanticIds { get; set; }
    }
}
