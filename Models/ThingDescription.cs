
namespace Opc.Ua.Edge.Translator.Models
{
    using System;
    using System.Globalization;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    public partial class ThingDescription
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("$schema ")]
        public Uri Schema { get; set; }

        [JsonProperty("definitions")]
        public Definitions Definitions { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public ThingDescriptionProperties Properties { get; set; }

        [JsonProperty("required")]
        public string[] ThingDescriptionRequired { get; set; }

        [JsonProperty("additionalProperties")]
        public bool AdditionalProperties { get; set; }
    }

    public partial class Definitions
    {
        [JsonProperty("anyUri")]
        public AnyUri AnyUri { get; set; }

        [JsonProperty("description")]
        public Description Description { get; set; }

        [JsonProperty("descriptions")]
        public Descriptions Descriptions { get; set; }

        [JsonProperty("title")]
        public Description Title { get; set; }

        [JsonProperty("titles")]
        public Descriptions Titles { get; set; }

        [JsonProperty("security")]
        public Scopes Security { get; set; }

        [JsonProperty("scopes")]
        public Scopes Scopes { get; set; }

        [JsonProperty("subprotocol")]
        public Subprotocol Subprotocol { get; set; }

        [JsonProperty("thing-context-w3c-uri")]
        public Subprotocol ThingContextW3CUri { get; set; }

        [JsonProperty("thing-context")]
        public ThingContext ThingContext { get; set; }

        [JsonProperty("type_declaration")]
        public Scopes TypeDeclaration { get; set; }

        [JsonProperty("dataSchema")]
        public DataSchema DataSchema { get; set; }

        [JsonProperty("form_element_property")]
        public FormElement FormElementProperty { get; set; }

        [JsonProperty("form_element_action")]
        public FormElement FormElementAction { get; set; }

        [JsonProperty("form_element_event")]
        public FormElement FormElementEvent { get; set; }

        [JsonProperty("form_element_root")]
        public FormElement FormElementRoot { get; set; }

        [JsonProperty("property_element")]
        public PropertyElement PropertyElement { get; set; }

        [JsonProperty("action_element")]
        public ActionElement ActionElement { get; set; }

        [JsonProperty("event_element")]
        public EventElement EventElement { get; set; }

        [JsonProperty("link_element")]
        public LinkElement LinkElement { get; set; }

        [JsonProperty("securityScheme")]
        public SecurityScheme SecurityScheme { get; set; }
    }

    public partial class ActionElement
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public ActionElementProperties Properties { get; set; }

        [JsonProperty("required")]
        public string[] ActionElementRequired { get; set; }

        [JsonProperty("additionalProperties")]
        public bool AdditionalProperties { get; set; }
    }

    public partial class ActionElementProperties
    {
        [JsonProperty("@type")]
        public Context Type { get; set; }

        [JsonProperty("description")]
        public Context Description { get; set; }

        [JsonProperty("descriptions")]
        public Context Descriptions { get; set; }

        [JsonProperty("title")]
        public Context Title { get; set; }

        [JsonProperty("titles")]
        public Context Titles { get; set; }

        [JsonProperty("forms")]
        public Forms Forms { get; set; }

        [JsonProperty("uriVariables")]
        public Actions UriVariables { get; set; }

        [JsonProperty("input")]
        public Context Input { get; set; }

        [JsonProperty("output")]
        public Context Output { get; set; }

        [JsonProperty("safe")]
        public Description Safe { get; set; }

        [JsonProperty("idempotent")]
        public Description Idempotent { get; set; }
    }

    public partial class Context
    {
        [JsonProperty("$ref")]
        public string Ref { get; set; }
    }

    public partial class Forms
    {
        [JsonProperty("type")]
        public FormsType Type { get; set; }

        [JsonProperty("minItems")]
        public long MinItems { get; set; }

        [JsonProperty("items")]
        public Context Items { get; set; }
    }

    public partial class Description
    {
        [JsonProperty("type")]
        public DescriptionType Type { get; set; }
    }

    public partial class Actions
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("additionalProperties")]
        public Context AdditionalProperties { get; set; }
    }

    public partial class AnyUri
    {
        [JsonProperty("type")]
        public DescriptionType Type { get; set; }

        [JsonProperty("format")]
        public string Format { get; set; }
    }

    public partial class DataSchema
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public DataSchemaProperties Properties { get; set; }
    }

    public partial class DataSchemaProperties
    {
        [JsonProperty("@type")]
        public Context Type { get; set; }

        [JsonProperty("description")]
        public Context Description { get; set; }

        [JsonProperty("title")]
        public Context Title { get; set; }

        [JsonProperty("descriptions")]
        public Context Descriptions { get; set; }

        [JsonProperty("titles")]
        public Context Titles { get; set; }

        [JsonProperty("writeOnly")]
        public Description WriteOnly { get; set; }

        [JsonProperty("readOnly")]
        public Description ReadOnly { get; set; }

        [JsonProperty("oneOf")]
        public Links OneOf { get; set; }

        [JsonProperty("unit")]
        public Description Unit { get; set; }

        [JsonProperty("enum")]
        public Enum Enum { get; set; }

        [JsonProperty("format")]
        public Description Format { get; set; }

        [JsonProperty("const")]
        public Const Const { get; set; }

        [JsonProperty("type")]
        public Subprotocol PropertiesType { get; set; }

        [JsonProperty("items")]
        public Items Items { get; set; }

        [JsonProperty("maxItems")]
        public MaxItemsClass MaxItems { get; set; }

        [JsonProperty("minItems")]
        public MaxItemsClass MinItems { get; set; }

        [JsonProperty("minimum")]
        public Description Minimum { get; set; }

        [JsonProperty("maximum")]
        public Description Maximum { get; set; }

        [JsonProperty("properties")]
        public PropertiesProperties Properties { get; set; }

        [JsonProperty("required")]
        public RequiredElement PropertiesRequired { get; set; }

        [JsonProperty("forms", NullValueHandling = NullValueHandling.Ignore)]
        public Forms Forms { get; set; }

        [JsonProperty("uriVariables", NullValueHandling = NullValueHandling.Ignore)]
        public Actions UriVariables { get; set; }

        [JsonProperty("observable", NullValueHandling = NullValueHandling.Ignore)]
        public Description Observable { get; set; }
    }

    public partial class Const
    {
    }

    public partial class Enum
    {
        [JsonProperty("type")]
        public FormsType Type { get; set; }

        [JsonProperty("minItems")]
        public long MinItems { get; set; }

        [JsonProperty("uniqueItems")]
        public bool UniqueItems { get; set; }
    }

    public partial class Items
    {
        [JsonProperty("oneOf")]
        public ItemsOneOf[] OneOf { get; set; }
    }

    public partial class ItemsOneOf
    {
        [JsonProperty("$ref", NullValueHandling = NullValueHandling.Ignore)]
        public string Ref { get; set; }

        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public FormsType? Type { get; set; }

        [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
        public Context Items { get; set; }
    }

    public partial class MaxItemsClass
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("minimum")]
        public long Minimum { get; set; }
    }

    public partial class Links
    {
        [JsonProperty("type")]
        public FormsType Type { get; set; }

        [JsonProperty("items")]
        public Context Items { get; set; }
    }

    public partial class PropertiesProperties
    {
        [JsonProperty("additionalProperties")]
        public Context AdditionalProperties { get; set; }
    }

    public partial class RequiredElement
    {
        [JsonProperty("type")]
        public FormsType Type { get; set; }

        [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
        public Description Items { get; set; }
    }

    public partial class Subprotocol
    {
        [JsonProperty("type")]
        public DescriptionType Type { get; set; }

        [JsonProperty("enum")]
        public string[] Enum { get; set; }
    }

    public partial class Descriptions
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("additionalProperties")]
        public Description AdditionalProperties { get; set; }
    }

    public partial class EventElement
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public EventElementProperties Properties { get; set; }

        [JsonProperty("required")]
        public string[] EventElementRequired { get; set; }

        [JsonProperty("additionalProperties")]
        public bool AdditionalProperties { get; set; }
    }

    public partial class EventElementProperties
    {
        [JsonProperty("@type")]
        public Context Type { get; set; }

        [JsonProperty("description")]
        public Context Description { get; set; }

        [JsonProperty("descriptions")]
        public Context Descriptions { get; set; }

        [JsonProperty("title")]
        public Context Title { get; set; }

        [JsonProperty("titles")]
        public Context Titles { get; set; }

        [JsonProperty("forms")]
        public Forms Forms { get; set; }

        [JsonProperty("uriVariables")]
        public Actions UriVariables { get; set; }

        [JsonProperty("subscription")]
        public Context Subscription { get; set; }

        [JsonProperty("data")]
        public Context Data { get; set; }

        [JsonProperty("cancellation")]
        public Context Cancellation { get; set; }
    }

    public partial class FormElement
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public FormElementActionProperties Properties { get; set; }

        [JsonProperty("required")]
        public string[] FormElementRequired { get; set; }

        [JsonProperty("additionalProperties")]
        public bool AdditionalProperties { get; set; }
    }

    public partial class FormElementActionProperties
    {
        [JsonProperty("op")]
        public Op Op { get; set; }

        [JsonProperty("href")]
        public Context Href { get; set; }

        [JsonProperty("contentType")]
        public Description ContentType { get; set; }

        [JsonProperty("contentCoding")]
        public Description ContentCoding { get; set; }

        [JsonProperty("subprotocol")]
        public Context Subprotocol { get; set; }

        [JsonProperty("security")]
        public Context Security { get; set; }

        [JsonProperty("scopes")]
        public Context Scopes { get; set; }

        [JsonProperty("response")]
        public Response Response { get; set; }
    }

    public partial class Op
    {
        [JsonProperty("oneOf")]
        public OpOneOf[] OneOf { get; set; }
    }

    public partial class OpOneOf
    {
        [JsonProperty("type")]
        public FormsType Type { get; set; }

        [JsonProperty("enum", NullValueHandling = NullValueHandling.Ignore)]
        public string[] Enum { get; set; }

        [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
        public Subprotocol Items { get; set; }
    }

    public partial class Response
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public ResponseProperties Properties { get; set; }
    }

    public partial class ResponseProperties
    {
        [JsonProperty("contentType")]
        public Description ContentType { get; set; }
    }

    public partial class LinkElement
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public LinkElementProperties Properties { get; set; }

        [JsonProperty("required")]
        public string[] LinkElementRequired { get; set; }

        [JsonProperty("additionalProperties")]
        public bool AdditionalProperties { get; set; }
    }

    public partial class LinkElementProperties
    {
        [JsonProperty("href")]
        public Context Href { get; set; }

        [JsonProperty("type")]
        public Description Type { get; set; }

        [JsonProperty("rel")]
        public Description Rel { get; set; }

        [JsonProperty("anchor")]
        public Context Anchor { get; set; }
    }

    public partial class PropertyElement
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public DataSchemaProperties Properties { get; set; }

        [JsonProperty("required")]
        public string[] PropertyElementRequired { get; set; }

        [JsonProperty("additionalProperties")]
        public bool AdditionalProperties { get; set; }
    }

    public partial class Scopes
    {
        [JsonProperty("oneOf")]
        public RequiredElement[] OneOf { get; set; }
    }

    public partial class SecurityScheme
    {
        [JsonProperty("oneOf")]
        public SecuritySchemeOneOf[] OneOf { get; set; }
    }

    public partial class SecuritySchemeOneOf
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public OneOfProperties Properties { get; set; }

        [JsonProperty("required")]
        public string[] OneOfRequired { get; set; }
    }

    public partial class OneOfProperties
    {
        [JsonProperty("@type")]
        public Context Type { get; set; }

        [JsonProperty("description")]
        public Context Description { get; set; }

        [JsonProperty("descriptions")]
        public Context Descriptions { get; set; }

        [JsonProperty("proxy")]
        public Context Proxy { get; set; }

        [JsonProperty("scheme")]
        public Subprotocol Scheme { get; set; }

        [JsonProperty("in", NullValueHandling = NullValueHandling.Ignore)]
        public Subprotocol In { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public Description Name { get; set; }

        [JsonProperty("qop", NullValueHandling = NullValueHandling.Ignore)]
        public Subprotocol Qop { get; set; }

        [JsonProperty("authorization", NullValueHandling = NullValueHandling.Ignore)]
        public Context Authorization { get; set; }

        [JsonProperty("alg", NullValueHandling = NullValueHandling.Ignore)]
        public Description Alg { get; set; }

        [JsonProperty("format", NullValueHandling = NullValueHandling.Ignore)]
        public Description Format { get; set; }

        [JsonProperty("identity", NullValueHandling = NullValueHandling.Ignore)]
        public Description Identity { get; set; }

        [JsonProperty("token", NullValueHandling = NullValueHandling.Ignore)]
        public Context Token { get; set; }

        [JsonProperty("refresh", NullValueHandling = NullValueHandling.Ignore)]
        public Context Refresh { get; set; }

        [JsonProperty("scopes", NullValueHandling = NullValueHandling.Ignore)]
        public Scopes Scopes { get; set; }

        [JsonProperty("flow", NullValueHandling = NullValueHandling.Ignore)]
        public Subprotocol Flow { get; set; }
    }

    public partial class ThingContext
    {
        [JsonProperty("oneOf")]
        public ThingContextOneOf[] OneOf { get; set; }
    }

    public partial class ThingContextOneOf
    {
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public FormsType? Type { get; set; }

        [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
        public Context[] Items { get; set; }

        [JsonProperty("additionalItems", NullValueHandling = NullValueHandling.Ignore)]
        public AdditionalItems AdditionalItems { get; set; }

        [JsonProperty("$ref", NullValueHandling = NullValueHandling.Ignore)]
        public string Ref { get; set; }
    }

    public partial class AdditionalItems
    {
        [JsonProperty("anyOf")]
        public AnyOf[] AnyOf { get; set; }
    }

    public partial class AnyOf
    {
        [JsonProperty("$ref", NullValueHandling = NullValueHandling.Ignore)]
        public string Ref { get; set; }

        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string Type { get; set; }
    }

    public partial class ThingDescriptionProperties
    {
        [JsonProperty("id")]
        public AnyUri Id { get; set; }

        [JsonProperty("title")]
        public Context Title { get; set; }

        [JsonProperty("titles")]
        public Context Titles { get; set; }

        [JsonProperty("properties")]
        public Actions Properties { get; set; }

        [JsonProperty("actions")]
        public Actions Actions { get; set; }

        [JsonProperty("events")]
        public Actions Events { get; set; }

        [JsonProperty("description")]
        public Context Description { get; set; }

        [JsonProperty("descriptions")]
        public Context Descriptions { get; set; }

        [JsonProperty("version")]
        public Version Version { get; set; }

        [JsonProperty("links")]
        public Links Links { get; set; }

        [JsonProperty("forms")]
        public Forms Forms { get; set; }

        [JsonProperty("base")]
        public Context Base { get; set; }

        [JsonProperty("securityDefinitions")]
        public SecurityDefinitions SecurityDefinitions { get; set; }

        [JsonProperty("support")]
        public Context Support { get; set; }

        [JsonProperty("created")]
        public AnyUri Created { get; set; }

        [JsonProperty("modified")]
        public AnyUri Modified { get; set; }

        [JsonProperty("security")]
        public Security Security { get; set; }

        [JsonProperty("@type")]
        public Context Type { get; set; }

        [JsonProperty("@context")]
        public Context Context { get; set; }
    }

    public partial class Security
    {
        [JsonProperty("oneOf")]
        public SecurityOneOf[] OneOf { get; set; }
    }

    public partial class SecurityOneOf
    {
        [JsonProperty("type")]
        public FormsType Type { get; set; }

        [JsonProperty("minItems", NullValueHandling = NullValueHandling.Ignore)]
        public long? MinItems { get; set; }

        [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
        public Description Items { get; set; }
    }

    public partial class SecurityDefinitions
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("minProperties")]
        public long MinProperties { get; set; }

        [JsonProperty("additionalProperties")]
        public Context AdditionalProperties { get; set; }
    }

    public partial class Version
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public VersionProperties Properties { get; set; }

        [JsonProperty("required")]
        public string[] VersionRequired { get; set; }
    }

    public partial class VersionProperties
    {
        [JsonProperty("instance")]
        public Description Instance { get; set; }
    }

    public enum FormsType { Array, String };

    public enum DescriptionType { Boolean, Number, String };

    public partial class ThingDescription
    {
        public static ThingDescription FromJson(string json) => JsonConvert.DeserializeObject<ThingDescription>(json, Converter.Settings);
    }

    public static class Serialize
    {
        public static string ToJson(this ThingDescription self) => JsonConvert.SerializeObject(self, Converter.Settings);
    }

    internal static class Converter
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            Converters =
        {
            FormsTypeConverter.Singleton,
            DescriptionTypeConverter.Singleton,
            new IsoDateTimeConverter { DateTimeStyles = DateTimeStyles.AssumeUniversal }
        },
        };
    }

    internal class FormsTypeConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(FormsType) || t == typeof(FormsType?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            switch (value)
            {
                case "array":
                    return FormsType.Array;
                case "string":
                    return FormsType.String;
            }
            throw new Exception("Cannot unmarshal type FormsType");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (FormsType)untypedValue;
            switch (value)
            {
                case FormsType.Array:
                    serializer.Serialize(writer, "array");
                    return;
                case FormsType.String:
                    serializer.Serialize(writer, "string");
                    return;
            }
            throw new Exception("Cannot marshal type FormsType");
        }

        public static readonly FormsTypeConverter Singleton = new FormsTypeConverter();
    }

    internal class DescriptionTypeConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(DescriptionType) || t == typeof(DescriptionType?);

        public override object ReadJson(JsonReader reader, Type t, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var value = serializer.Deserialize<string>(reader);
            switch (value)
            {
                case "boolean":
                    return DescriptionType.Boolean;
                case "number":
                    return DescriptionType.Number;
                case "string":
                    return DescriptionType.String;
            }
            throw new Exception("Cannot unmarshal type DescriptionType");
        }

        public override void WriteJson(JsonWriter writer, object untypedValue, JsonSerializer serializer)
        {
            if (untypedValue == null)
            {
                serializer.Serialize(writer, null);
                return;
            }
            var value = (DescriptionType)untypedValue;
            switch (value)
            {
                case DescriptionType.Boolean:
                    serializer.Serialize(writer, "boolean");
                    return;
                case DescriptionType.Number:
                    serializer.Serialize(writer, "number");
                    return;
                case DescriptionType.String:
                    serializer.Serialize(writer, "string");
                    return;
            }
            throw new Exception("Cannot marshal type DescriptionType");
        }

        public static readonly DescriptionTypeConverter Singleton = new DescriptionTypeConverter();
    }
}
