using System.Text.Json;
using System.Text.Json.Nodes;

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
        }

        public string Name { get; }

        public string? Type => _node["type"]?.GetValue<string>();

        public string? Description => _node["description"]?.GetValue<string>();

        public string? MapToNodeId => GetString("uav:mapToNodeId");
        public string? MapToType => GetString("uav:mapToType");
        public string? MapByFieldPath => GetString("uav:mapByFieldPath");

        public bool IsMapped => !string.IsNullOrEmpty(MapToNodeId);

        private string? GetString(string key) => _node[key]?.GetValue<string>();

        /// <summary>
        /// Applies an OPC UA mapping to this property (mirrors the pac4200.tm.jsonld sample).
        /// </summary>
        public void ApplyMapping(string nodeId, string typeNodeId, string? fieldPath)
        {
            _node["uav:mapToNodeId"] = nodeId;
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
            _node.Remove("uav:mapToNodeId");
            _node.Remove("uav:mapToType");
            _node.Remove("uav:mapByFieldPath");
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

        public string Serialize()
        {
            return _root.ToJsonString(SerializerOptions);
        }
    }
}
