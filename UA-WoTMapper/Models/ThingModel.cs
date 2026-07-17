using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WotOpcUaMapper.Models
{
    /// <summary>
    /// A single WoT property together with any OPC UA mapping applied to it.
    /// Wraps the underlying JSON object so edits are written back on serialization.
    /// </summary>
    public class WotProperty
    {
        private readonly JsonObject _node;

        public WotProperty(string name, JsonObject node)
        {
            Name = name;
            _node = node;

            // Default the target OPC UA NodeId to a string identifier derived from the WoT
            // property name (e.g. "s=Voltage"). The user can edit this in the UI.
            if (string.IsNullOrEmpty(GetString("uav:mapToNodeId")))
            {
                _node["uav:mapToNodeId"] = DefaultNodeId(name);
            }
        }

        public string Name { get; }

        public string? Type => _node["type"]?.GetValue<string>();

        public string? Description => _node["description"]?.GetValue<string>();

        public string? MapToNodeId => GetString("uav:mapToNodeId");
        public string? MapToType => GetString("uav:mapToType");
        public string? MapByFieldPath => GetString("uav:mapByFieldPath");

        /// <summary>True once an OPC UA type has been mapped to this property (via drag &amp; drop).</summary>
        public bool IsMapped => !string.IsNullOrEmpty(MapToType);

        public static string DefaultNodeId(string name) => "s=" + name;

        private string? GetString(string key) => _node[key]?.GetValue<string>();

        /// <summary>
        /// Sets the target OPC UA NodeId for this property. An empty value resets it to the
        /// default derived from the property name.
        /// </summary>
        public void SetNodeId(string? nodeId)
        {
            _node["uav:mapToNodeId"] = string.IsNullOrWhiteSpace(nodeId)
                ? DefaultNodeId(Name)
                : nodeId.Trim();
        }

        /// <summary>
        /// Applies an OPC UA type mapping to this property (mirrors the pac4200.tm.jsonld sample).
        /// The NodeId is left untouched so any user-provided value is preserved.
        /// </summary>
        public void ApplyMapping(string typeNodeId, string? fieldPath)
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
            // keep a NodeId so the property remains addressable
            _node["uav:mapToNodeId"] = DefaultNodeId(Name);
        }
    }

    /// <summary>
    /// Parses, edits and serializes a W3C Web of Things Thing Model (.tm.jsonld) file,
    /// preserving all original content while allowing OPC UA property mappings to be added.
    /// </summary>
    public class ThingModel
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        private static readonly Regex PlaceholderRegex =
            new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);

        private JsonObject _root;

        private ThingModel(JsonObject root)
        {
            _root = root;
            Properties = LoadProperties();
        }

        public string? Title => _root["title"]?.GetValue<string>();

        public string? Id => _root["id"]?.GetValue<string>();

        public List<WotProperty> Properties { get; private set; }

        public string? FileName { get; set; }

        /// <summary>
        /// True when this document is a WoT Thing Model (as opposed to a Thing Description),
        /// i.e. its @type contains "tm:ThingModel".
        /// </summary>
        public bool IsThingModel => TypeContains("tm:ThingModel");

        public static ThingModel Parse(string json)
        {
            var node = JsonNode.Parse(json) as JsonObject
                       ?? throw new InvalidOperationException("Thing Model root must be a JSON object.");
            return new ThingModel(node);
        }

        private List<WotProperty> LoadProperties()
        {
            var list = new List<WotProperty>();
            if (_root["properties"] is JsonObject props)
            {
                foreach (var kvp in props)
                {
                    if (kvp.Value is JsonObject propObj)
                    {
                        list.Add(new WotProperty(kvp.Key, propObj));
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Ensures the given OPC UA namespace is declared under the "uav" prefix in the
        /// JSON-LD @context so that mapped nodeset type references resolve.
        /// </summary>
        public void EnsureContextPrefix(string prefix, string namespaceUri)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return;
            }

            if (_root["@context"] is JsonArray contextArray)
            {
                foreach (var item in contextArray)
                {
                    if (item is JsonObject obj)
                    {
                        obj[prefix] = namespaceUri;
                        return;
                    }
                }
                contextArray.Add(new JsonObject { [prefix] = namespaceUri });
            }
        }

        /// <summary>
        /// Returns the distinct set of WoT placeholder tokens ("{{NAME}}") found anywhere in
        /// the document, in first-seen order.
        /// </summary>
        public IReadOnlyList<string> FindPlaceholders()
        {
            var json = Serialize();
            var seen = new List<string>();
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in PlaceholderRegex.Matches(json))
            {
                var name = match.Groups[1].Value;
                if (set.Add(name))
                {
                    seen.Add(name);
                }
            }
            return seen;
        }

        /// <summary>
        /// Produces a Thing Description from this Thing Model by substituting the provided
        /// placeholder values and removing the "tm:ThingModel" @type marker. Returns the
        /// serialized Thing Description JSON.
        /// </summary>
        public string CreateThingDescriptionJson(IReadOnlyDictionary<string, string> placeholderValues)
        {
            var json = Serialize();

            json = PlaceholderRegex.Replace(json, match =>
            {
                var name = match.Groups[1].Value;
                if (placeholderValues.TryGetValue(name, out var value))
                {
                    return JsonEscape(value);
                }
                return match.Value;
            });

            var root = JsonNode.Parse(json) as JsonObject
                       ?? throw new InvalidOperationException("Thing Description root must be a JSON object.");

            RemoveThingModelType(root);

            return root.ToJsonString(SerializerOptions);
        }

        private bool TypeContains(string value)
        {
            var typeNode = _root["@type"];
            if (typeNode is JsonArray array)
            {
                return array.Any(n => n is JsonValue && string.Equals(n!.GetValue<string>(), value, StringComparison.Ordinal));
            }
            if (typeNode is JsonValue single)
            {
                return string.Equals(single.GetValue<string>(), value, StringComparison.Ordinal);
            }
            return false;
        }

        private static void RemoveThingModelType(JsonObject root)
        {
            var typeNode = root["@type"];
            if (typeNode is JsonArray array)
            {
                for (int i = array.Count - 1; i >= 0; i--)
                {
                    if (array[i] is JsonValue v && string.Equals(v.GetValue<string>(), "tm:ThingModel", StringComparison.Ordinal))
                    {
                        array.RemoveAt(i);
                    }
                }
                if (array.Count == 0)
                {
                    root.Remove("@type");
                }
            }
            else if (typeNode is JsonValue single &&
                     string.Equals(single.GetValue<string>(), "tm:ThingModel", StringComparison.Ordinal))
            {
                root.Remove("@type");
            }
        }

        private static string JsonEscape(string value)
        {
            // Escapes the value for embedding inside an existing JSON string literal.
            var encoded = JsonSerializer.Serialize(value);
            // strip the surrounding quotes added by the serializer
            return encoded.Length >= 2 ? encoded[1..^1] : encoded;
        }

        public string Serialize()
        {
            return _root.ToJsonString(SerializerOptions);
        }
    }
}
