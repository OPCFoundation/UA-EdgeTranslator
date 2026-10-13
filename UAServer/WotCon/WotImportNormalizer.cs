namespace Opc.Ua.Edge.Translator.WotCon
{
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Adapts Thing Descriptions written for the WoT Connectivity 1.02 asset
    /// management so the WoT Connectivity 1.1 registry can materialize them.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The registry classifies a document as a Thing Description only when its
    /// <c>@type</c> carries <c>uav:object</c>; a plain <c>Thing</c> is rejected with
    /// WOT3001. The term is added when missing.</item>
    /// <item><c>uav</c> must be bound to the WoT-Binding vocabulary for <c>uav:*</c>
    /// terms to resolve. Older files bind it to a companion-specification namespace
    /// while using the WoT-Binding mapping terms; the prefix is rebound so those
    /// authored mappings stay in force.</item>
    /// <item>Without a parent the materialized Object is not reachable by browsing.
    /// When the document states no <c>uav:browsePathAnchor</c> it is anchored at the
    /// Objects folder and linked into it with a <c>uav:componentOf</c> link.</item>
    /// <item>Properties without a WoT-Binding target mapping are mapped to the
    /// Variable materialized for them, so their forms are read and written.</item>
    /// </list>
    /// Documents that already state these terms are returned unchanged.
    /// </remarks>
    public static class WotImportNormalizer
    {
        public const string BindingNamespace = "http://opcfoundation.org/UA/WoT-Binding/";

        public const string ObjectsFolder = "nsu=http://opcfoundation.org/UA/;i=85";

        private const string TdContext = "https://www.w3.org/2022/wot/td/v1.1";

        /// <summary>
        /// Returns the namespace URIs named by the <c>uav:mapToType</c> terms of the
        /// document's properties (portable <c>nsu=</c> form only), in document order.
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<string> MappingTypeNamespaces(byte[] content)
        {
            System.Collections.Generic.List<string> result = new();
            JsonObject document;
            try
            {
                document = JsonNode.Parse(content)?.AsObject();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return result;
            }

            if (document?["properties"] is not JsonObject properties)
            {
                return result;
            }

            foreach ((string _, JsonNode node) in properties)
            {
                if (node is JsonObject property
                    && property["uav:mapToType"] is JsonValue value
                    && value.TryGetValue(out string type)
                    && type.StartsWith("nsu=", StringComparison.Ordinal))
                {
                    int end = type.IndexOf(';', StringComparison.Ordinal);
                    string uri = end > 4 ? type.Substring(4, end - 4) : null;
                    if (!string.IsNullOrEmpty(uri) && !result.Contains(uri))
                    {
                        result.Add(uri);
                    }
                }
            }

            return result;
        }

        public static byte[] Normalize(byte[] content)
        {
            ArgumentNullException.ThrowIfNull(content);

            JsonObject document;
            try
            {
                document = JsonNode.Parse(content)?.AsObject();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Not a JSON object; let the registry report it.
                return content;
            }

            if (document == null)
            {
                return content;
            }

            bool changed = BindUavPrefix(document);
            changed |= AddObjectType(document);
            changed |= AddObjectsFolderAnchor(document);
            changed |= QualifyRelativeTargetNodeIds(document);
            changed |= MapPropertiesToThemselves(document);

            return changed
                ? JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions { WriteIndented = true })
                : content;
        }

        private static bool BindUavPrefix(JsonObject document)
        {
            JsonArray context = document["@context"] switch
            {
                JsonArray array => array,
                JsonValue value => new JsonArray(value.DeepClone()),
                _ => new JsonArray(JsonValue.Create(TdContext))
            };

            JsonObject prefixes = context.OfType<JsonObject>().FirstOrDefault(o => o.ContainsKey("uav"))
                ?? context.OfType<JsonObject>().FirstOrDefault();

            string bound = prefixes?["uav"]?.GetValue<string>();
            if (string.Equals(bound, BindingNamespace, StringComparison.Ordinal))
            {
                return false;
            }

            if (prefixes == null)
            {
                prefixes = new JsonObject();
                context.Add(prefixes);
            }

            // Files written for the 1.02 asset management bind 'uav' to a companion
            // namespace, but the terms they use with it (uav:mapToNodeId, uav:mapToType,
            // uav:mapByFieldPath) are the WoT-Binding target-mapping terms. Rebinding the
            // prefix keeps every authored mapping in force.
            prefixes["uav"] = BindingNamespace;

            // The companion model must still be loaded: WoT Binding declares the model
            // namespaces a document depends on as 'ns1', 'ns2', ... context entries,
            // which is how the registry decides which NodeSets to fetch.
            if (!string.IsNullOrEmpty(bound)
                && !prefixes.Any(p => p.Key.StartsWith("ns", StringComparison.Ordinal) && string.Equals(p.Value?.GetValue<string>(), bound, StringComparison.Ordinal)))
            {
                int index = 1;
                while (prefixes.ContainsKey("ns" + index))
                {
                    index++;
                }

                prefixes["ns" + index] = bound;
            }

            document["@context"] = context;
            return true;
        }

        private static bool AddObjectType(JsonObject document)
        {
            JsonArray types = document["@type"] switch
            {
                JsonArray array => array,
                JsonValue value => new JsonArray(value.DeepClone()),
                _ => new JsonArray(JsonValue.Create("Thing"))
            };

            string[] tokens = types.Select(t => t?.GetValue<string>()).ToArray();
            if (tokens.Any(t => t is "uav:object" or "tm:ThingModel"))
            {
                return false;
            }

            types.Add("uav:object");
            document["@type"] = types;
            return true;
        }

        private static bool AddObjectsFolderAnchor(JsonObject document)
        {
            if (document.ContainsKey("uav:browsePathAnchor"))
            {
                return false;
            }

            document["uav:browsePathAnchor"] = ObjectsFolder;

            // The anchor scopes browse paths; the componentOf link is what places
            // the Object below the folder so clients can browse to it.
            JsonArray links = document["links"] as JsonArray ?? new JsonArray();
            bool linked = links.OfType<JsonObject>().Any(l =>
                string.Equals(l["rel"]?.GetValue<string>(), "uav:componentOf", StringComparison.Ordinal));
            if (!linked)
            {
                links.Add(new JsonObject { ["rel"] = "uav:componentOf", ["href"] = ObjectsFolder });
                document["links"] = links;
            }

            return true;
        }

        /// <summary>
        /// The 1.02 asset management resolves a namespace-less <c>uav:mapToNodeId</c>
        /// (e.g. <c>s=VoltageL-N</c>) against the asset's own namespace. The registry
        /// requires a portable NodeId, so such values are qualified with the Thing's
        /// namespace (its <c>id</c>), which is where the registry materializes it.
        /// Values that already carry <c>nsu=</c> or <c>ns=</c> are left untouched.
        /// </summary>
        private static bool QualifyRelativeTargetNodeIds(JsonObject document)
        {
            string thingId = document["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(thingId) || document["properties"] is not JsonObject properties)
            {
                return false;
            }

            bool changed = false;
            foreach ((string _, JsonNode node) in properties)
            {
                if (node is not JsonObject property || property["uav:mapToNodeId"] is not JsonValue value
                    || !value.TryGetValue(out string nodeId) || string.IsNullOrWhiteSpace(nodeId))
                {
                    continue;
                }

                string qualified = QualifyNodeId(nodeId, thingId);
                if (!string.Equals(qualified, nodeId, StringComparison.Ordinal))
                {
                    property["uav:mapToNodeId"] = qualified;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// Qualifies a relative NodeId (one without <c>nsu=</c> or <c>ns=</c>) with the
        /// Thing's namespace; an already qualified NodeId is returned unchanged.
        /// </summary>
        private static string QualifyNodeId(string nodeId, string thingId)
        {
            return nodeId.StartsWith("nsu=", StringComparison.Ordinal) || nodeId.StartsWith("ns=", StringComparison.Ordinal)
                ? nodeId
                : $"nsu={thingId};{nodeId}";
        }

        /// <summary>
        /// The registry only installs read/write handlers on Variables a property's
        /// <c>uav:mapToNodeId</c> (or <c>uav:mapToType</c>) targets; without one the
        /// Variable never receives a value and reads return BadWaitingForInitialData.
        /// Each property without a target mapping gets a stable <c>uav:id</c> in the
        /// Thing's namespace and a <c>uav:mapToNodeId</c> pointing at that same
        /// Variable, so its forms feed the Variable materialized for it.
        /// </summary>
        private static bool MapPropertiesToThemselves(JsonObject document)
        {
            string thingId = document["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(thingId) || document["properties"] is not JsonObject properties)
            {
                return false;
            }

            string thingName = document["name"]?.GetValue<string>() ?? document["title"]?.GetValue<string>() ?? thingId;
            bool changed = false;

            foreach ((string name, JsonNode node) in properties)
            {
                if (node is not JsonObject property
                    || property.ContainsKey("uav:mapToNodeId")
                    || property.ContainsKey("uav:mapToType"))
                {
                    continue;
                }

                string nodeId = property["uav:id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(nodeId))
                {
                    nodeId = $"nsu={thingId};s={thingName}.{name}";
                    property["uav:id"] = nodeId;
                }
                else
                {
                    // An authored relative id (such as "s=Speed") belongs to the Thing's
                    // namespace; unqualified it would resolve in namespace 0.
                    string qualified = QualifyNodeId(nodeId, thingId);
                    if (!string.Equals(qualified, nodeId, StringComparison.Ordinal))
                    {
                        nodeId = qualified;
                        property["uav:id"] = nodeId;
                    }
                }

                property["uav:mapToNodeId"] = nodeId;
                changed = true;
            }

            return changed;
        }
    }
}
