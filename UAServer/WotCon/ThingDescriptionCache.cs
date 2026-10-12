namespace Opc.Ua.Edge.Translator.WotCon
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Concurrent;

    /// <summary>
    /// Thing Descriptions known to the registry, keyed by their <c>base</c> URI, in
    /// the Edge Translator model the protocol drivers consume.
    /// </summary>
    public sealed class ThingDescriptionCache
    {
        private readonly ConcurrentDictionary<string, ThingDescription> _byBase =
            new(StringComparer.OrdinalIgnoreCase);

        public void Set(string json)
        {
            ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(json.Trim('\uFEFF'));
            if (!string.IsNullOrEmpty(td?.Base))
            {
                _byBase[td.Base] = td;
            }
        }

        public ThingDescription GetOrCreate(string baseUri)
        {
            if (_byBase.TryGetValue(baseUri, out ThingDescription td))
            {
                return td;
            }

            // Unknown document: give drivers the minimum they need to connect.
            string name = Uri.TryCreate(baseUri, UriKind.Absolute, out Uri uri) ? uri.Host : baseUri;
            return new ThingDescription { Base = baseUri, Name = name, Title = name };
        }
    }
}
