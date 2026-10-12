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
    /// terms to resolve. Older files bind it to a companion-specification namespace;
    /// that binding is kept under a new prefix and <c>uav</c> is rebound.</item>
    /// <item>Without a parent the materialized Object is not reachable by browsing.
    /// When the document states no <c>uav:browsePathAnchor</c> it is anchored at the
    /// Objects folder and linked into it with a <c>uav:componentOf</c> link.</item>
    /// </list>
    /// Documents that already state these terms are returned unchanged.
    /// </remarks>
    public static class WotImportNormalizer
    {
        public const string BindingNamespace = "http://opcfoundation.org/UA/WoT-Binding/";

        public const string ObjectsFolder = "nsu=http://opcfoundation.org/UA/;i=85";

        private const string TdContext = "https://www.w3.org/2022/wot/td/v1.1";

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

            if (bound != null)
            {
                // Keep the companion namespace reachable under its own prefix, and move
                // every term written against it there; otherwise the registry would read
                // them as WoT-Binding terms (e.g. uav:mapToNodeId pointing at nodes of a
                // companion model that is not loaded).
                string alias = "uav0";
                for (int i = 1; prefixes.ContainsKey(alias); i++)
                {
                    alias = "uav" + i;
                }

                prefixes[alias] = bound;
                RenamePrefix(document, "uav:", alias + ":");
            }

            prefixes["uav"] = BindingNamespace;
            document["@context"] = context;
            return true;
        }

        private static void RenamePrefix(JsonNode node, string from, string to)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (string key in obj.Select(p => p.Key).ToList())
                    {
                        JsonNode child = obj[key];
                        if (key.StartsWith(from, StringComparison.Ordinal))
                        {
                            obj.Remove(key);
                            obj[to + key.Substring(from.Length)] = child;
                        }

                        if (child != null && key != "@context")
                        {
                            RenamePrefix(child, from, to);
                        }
                    }

                    break;

                case JsonArray array:
                    foreach (JsonNode item in array)
                    {
                        if (item != null)
                        {
                            RenamePrefix(item, from, to);
                        }
                    }

                    break;
            }
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
    }
}
