namespace Opc.Ua.Edge.Translator.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;

    /// <summary>
    /// A single WoT property of a <see cref="WotMappingDocument"/> together with the OPC UA
    /// projection (uav:mapToNodeId, uav:mapToType, uav:mapByFieldPath) applied to it. Wraps
    /// the underlying JSON object so edits are written back on serialization.
    /// </summary>
    public sealed class WotMappingProperty
    {
        private readonly JsonObject _node;

        public WotMappingProperty(string name, JsonObject node)
        {
            Name = name;
            _node = node;

            // Default the target OPC UA NodeId to a string identifier derived from the
            // property name (e.g. "s=Voltage"); the user can edit it in the UI.
            if (string.IsNullOrEmpty(GetString("uav:mapToNodeId")))
            {
                _node["uav:mapToNodeId"] = DefaultNodeId(name);
            }
        }

        public string Name { get; }

        public string Type => GetString("type");

        public string Description => GetString("description");

        public string MapToNodeId => GetString("uav:mapToNodeId");

        public string MapToType => GetString("uav:mapToType");

        public string MapByFieldPath => GetString("uav:mapByFieldPath");

        /// <summary>True once an OPC UA type has been mapped to this property.</summary>
        public bool IsMapped => !string.IsNullOrEmpty(MapToType);

        public static string DefaultNodeId(string name) => "s=" + name;

        /// <summary>
        /// Sets the target OPC UA NodeId. An empty value resets it to the default derived
        /// from the property name.
        /// </summary>
        public void SetNodeId(string nodeId)
        {
            _node["uav:mapToNodeId"] = string.IsNullOrWhiteSpace(nodeId) ? DefaultNodeId(Name) : nodeId.Trim();
        }

        /// <summary>
        /// Maps the property onto an OPC UA type (an "nsu=...;i=..." expanded node id) and,
        /// for structures and ObjectTypes, onto one of its fields. The NodeId is left as is.
        /// </summary>
        public void ApplyMapping(string typeNodeId, string fieldPath)
        {
            _node["uav:mapToType"] = typeNodeId;

            if (!string.IsNullOrEmpty(fieldPath))
            {
                _node["uav:mapByFieldPath"] = fieldPath;
            }
            else
            {
                _node.Remove("uav:mapByFieldPath");
            }
        }

        public void ClearMapping()
        {
            _node.Remove("uav:mapToType");
            _node.Remove("uav:mapByFieldPath");

            // Keep a NodeId so the property remains addressable.
            _node["uav:mapToNodeId"] = DefaultNodeId(Name);
        }

        private string GetString(string key) => _node[key] is JsonValue value && value.TryGetValue(out string text) ? text : null;
    }

    /// <summary>
    /// Parses, edits and serializes a W3C WoT Thing Model or Thing Description, preserving
    /// all of its content while OPC UA projections are added to its properties. Ported from
    /// the UA-WoTMapper tool for the dashboard's Projections page.
    /// </summary>
    public sealed class WotMappingDocument
    {
        private const string ThingModelType = "tm:ThingModel";

        private static readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

        private static readonly Regex _placeholderRegex = new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);

        private readonly JsonObject _root;

        private WotMappingDocument(JsonObject root)
        {
            _root = root;
            Properties = LoadProperties();
        }

        public string Title => GetString("title");

        /// <summary>The Thing's name, which names its asset; falls back to the title like the registry import.</summary>
        public string Name => GetString("name") ?? Title;

        /// <summary>True when the document has an explicit "name", which the translator requires to name the asset.</summary>
        public bool HasName => !string.IsNullOrWhiteSpace(GetString("name"));

        public string Id => GetString("id");

        public IReadOnlyList<WotMappingProperty> Properties { get; }

        /// <summary>The file the document was loaded from, used as the default download name.</summary>
        public string FileName { get; set; }

        /// <summary>True for a Thing Model (its @type contains "tm:ThingModel") rather than a Thing Description.</summary>
        public bool IsThingModel => TypeContains(ThingModelType);

        public static WotMappingDocument Parse(string json)
        {
            if (JsonNode.Parse(json.Trim('\uFEFF')) is not JsonObject root)
            {
                throw new InvalidOperationException("The WoT document's root must be a JSON object.");
            }

            return new WotMappingDocument(root);
        }

        /// <summary>
        /// Declares the companion-model namespace of a mapped type in the JSON-LD @context, so
        /// both onboarding paths load its NodeSet: the first one is bound to the "uav" prefix
        /// (the asset management convention), further ones get their own "nsN" entry (the WoT
        /// Binding convention for model namespaces). A namespace already declared is left alone.
        /// </summary>
        public void DeclareNamespace(string namespaceUri)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return;
            }

            JsonArray context = _root["@context"] switch
            {
                JsonArray array => array,
                JsonValue single => new JsonArray(single.DeepClone()),
                _ => new JsonArray()
            };
            _root["@context"] = context;

            List<JsonObject> objects = context.OfType<JsonObject>().ToList();
            bool declared = objects.SelectMany(o => o).Any(p =>
                p.Value is JsonValue value && value.TryGetValue(out string uri) && string.Equals(uri, namespaceUri, StringComparison.Ordinal));
            if (declared)
            {
                return;
            }

            // Each in its own object: the asset management loads the first value of every
            // @context object as a companion NodeSet.
            if (!objects.Any(o => o.ContainsKey("uav")))
            {
                context.Add(new JsonObject { ["uav"] = namespaceUri });
                return;
            }

            int index = 1;
            while (objects.Any(o => o.ContainsKey("ns" + index)))
            {
                index++;
            }

            context.Add(new JsonObject { ["ns" + index] = namespaceUri });
        }

        /// <summary>The distinct Thing Model placeholders ("{{NAME}}") in first-seen order.</summary>
        public IReadOnlyList<string> FindPlaceholders()
        {
            List<string> seen = [];
            HashSet<string> set = new(StringComparer.Ordinal);
            foreach (Match match in _placeholderRegex.Matches(Serialize()))
            {
                string name = match.Groups[1].Value;
                if (set.Add(name))
                {
                    seen.Add(name);
                }
            }

            return seen;
        }

        /// <summary>
        /// Creates a Thing Description from this Thing Model: substitutes the placeholder
        /// values, removes the "tm:ThingModel" @type marker and, when given, sets the Thing's
        /// name (which the translator requires to name the asset).
        /// </summary>
        public string CreateThingDescriptionJson(IReadOnlyDictionary<string, string> placeholderValues, string name = null)
        {
            string json = _placeholderRegex.Replace(Serialize(), match =>
                placeholderValues != null && placeholderValues.TryGetValue(match.Groups[1].Value, out string value)
                    ? JsonEscape(value)
                    : match.Value);

            if (JsonNode.Parse(json) is not JsonObject root)
            {
                throw new InvalidOperationException("The Thing Description's root must be a JSON object.");
            }

            RemoveThingModelType(root);

            if (!string.IsNullOrWhiteSpace(name))
            {
                root["name"] = name.Trim();
            }

            return root.ToJsonString(_serializerOptions);
        }

        public string Serialize() => _root.ToJsonString(_serializerOptions);

        private List<WotMappingProperty> LoadProperties()
        {
            List<WotMappingProperty> list = [];
            if (_root["properties"] is JsonObject properties)
            {
                foreach (KeyValuePair<string, JsonNode> property in properties)
                {
                    if (property.Value is JsonObject propertyObject)
                    {
                        list.Add(new WotMappingProperty(property.Key, propertyObject));
                    }
                }
            }

            return list;
        }

        private string GetString(string key) => _root[key] is JsonValue value && value.TryGetValue(out string text) ? text : null;

        private bool TypeContains(string value)
        {
            return _root["@type"] switch
            {
                JsonArray array => array.Any(n => n is JsonValue v && v.TryGetValue(out string s) && string.Equals(s, value, StringComparison.Ordinal)),
                JsonValue single => single.TryGetValue(out string s) && string.Equals(s, value, StringComparison.Ordinal),
                _ => false
            };
        }

        private static void RemoveThingModelType(JsonObject root)
        {
            if (root["@type"] is JsonArray array)
            {
                for (int i = array.Count - 1; i >= 0; i--)
                {
                    if (array[i] is JsonValue v && v.TryGetValue(out string s) && string.Equals(s, ThingModelType, StringComparison.Ordinal))
                    {
                        array.RemoveAt(i);
                    }
                }

                if (array.Count == 0)
                {
                    root.Remove("@type");
                }
            }
            else if (root["@type"] is JsonValue single && single.TryGetValue(out string s) && string.Equals(s, ThingModelType, StringComparison.Ordinal))
            {
                root.Remove("@type");
            }
        }

        // Escapes the value for embedding inside an existing JSON string literal.
        private static string JsonEscape(string value)
        {
            string encoded = JsonSerializer.Serialize(value ?? string.Empty);
            return encoded.Length >= 2 ? encoded[1..^1] : encoded;
        }
    }
}
