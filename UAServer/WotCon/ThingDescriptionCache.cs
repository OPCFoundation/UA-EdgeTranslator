namespace Opc.Ua.Edge.Translator.WotCon
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Concurrent;

    /// <summary>
    /// Thing Descriptions known to the registry, in the Edge Translator model the
    /// protocol drivers consume.
    /// </summary>
    /// <remarks>
    /// A TD's <c>base</c> is not a unique identity: several registry resources may
    /// describe different Things on the same endpoint. TDs are therefore keyed by the
    /// registry resource they came from. Only when no resource is known does the
    /// lookup fall back to the base.
    /// </remarks>
    public sealed class ThingDescriptionCache
    {
        private readonly ConcurrentDictionary<string, ThingDescription> _byResource =
            new(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, ThingDescription> _byBase =
            new(StringComparer.OrdinalIgnoreCase);

        // The registry resource each compiled form belongs to. Forms are immutable and
        // the runtime opens channels with the same instances it was given, so they are
        // keyed by reference. Weak keys let retired plans be collected.
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<global::Opc.Ua.WotCon.Bindings.WotCompiledForm, string> _resourceOfForm = new();

        /// <summary>Records that <paramref name="form"/> was planned for <paramref name="resourceXid"/>.</summary>
        public void SetResource(global::Opc.Ua.WotCon.Bindings.WotCompiledForm form, string resourceXid)
        {
            if (form != null && !string.IsNullOrEmpty(resourceXid))
            {
                _resourceOfForm.AddOrUpdate(form, resourceXid);
            }
        }

        /// <summary>Returns the registry resource <paramref name="form"/> was planned for, if known.</summary>
        public string ResourceOf(global::Opc.Ua.WotCon.Bindings.WotCompiledForm form)
        {
            return form != null && _resourceOfForm.TryGetValue(form, out string resourceXid) ? resourceXid : null;
        }

        /// <summary>Caches the TD in <paramref name="json"/> for <paramref name="resourceXid"/>.</summary>
        public void Set(string resourceXid, string json)
        {
            ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(json.Trim('\uFEFF'));
            if (td == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(resourceXid))
            {
                _byResource[resourceXid] = td;
            }

            if (!string.IsNullOrEmpty(td.Base))
            {
                _byBase[td.Base] = td;
            }
        }

        /// <summary>Caches a TD that has no registry resource; it is found by its base.</summary>
        public void Set(string json) => Set(null, json);

        /// <summary>
        /// Returns the TD of <paramref name="resourceXid"/>. Without a known resource it
        /// falls back to the TD with base <paramref name="baseUri"/>, and finally to a
        /// minimal TD that is just enough for a driver to connect.
        /// </summary>
        public ThingDescription GetOrCreate(string resourceXid, string baseUri)
        {
            if (!string.IsNullOrEmpty(resourceXid) && _byResource.TryGetValue(resourceXid, out ThingDescription byResource))
            {
                return byResource;
            }

            return GetOrCreate(baseUri);
        }

        /// <summary>Returns the TD with base <paramref name="baseUri"/>, or a minimal one.</summary>
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
