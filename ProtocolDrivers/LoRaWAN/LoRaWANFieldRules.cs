namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    /// <summary>
    /// Decoding rules that apply to a single LoRaWAN field but do not fit the
    /// asset's <c>&lt;devEUI&gt;[/&lt;channel&gt;]/&lt;offset&gt;?quantity=&lt;len&gt;</c>
    /// address scheme.
    /// <para>
    /// <see cref="AssetTag"/> is shared by every protocol driver, and the asset
    /// dispatches on the number of parts in the address string, so neither is a
    /// safe place to carry binding-specific state. These rules are therefore
    /// registered against the tag name when the tag is created and applied after
    /// the raw bytes have been read.
    /// </para>
    /// </summary>
    public sealed class LoRaWANFieldRule
    {
        /// <summary>
        /// Name this field is known by in a <c>lorav:presentWhen</c> condition,
        /// from <c>lorav:alias</c>.
        /// </summary>
        public string Alias { get; init; }

        /// <summary>The gate that decides whether this field is present.</summary>
        public PresentWhenCondition PresentWhen { get; init; }

        /// <summary>Wire-value to decoded-value mapping, from <c>lorav:valueMap</c>.</summary>
        public IReadOnlyList<ValueMapEntry> ValueMap { get; init; }

        /// <summary>
        /// Byte offset of the field a <see cref="PresentWhen"/> gate refers to,
        /// resolved from its alias when the tag was created.
        /// </summary>
        public int? DiscriminatorOffset { get; init; }

        /// <summary>Byte length of the discriminator field.</summary>
        public int DiscriminatorLength { get; init; } = 1;

        /// <summary>True when this field carries no conditional or mapping rules.</summary>
        public bool IsEmpty => (PresentWhen is null) && ((ValueMap is null) || (ValueMap.Count == 0));
    }

    /// <summary>
    /// Per-asset registry of <see cref="LoRaWANFieldRule"/>, keyed by tag name.
    /// </summary>
    public sealed class LoRaWANFieldRules
    {
        private readonly ConcurrentDictionary<string, LoRaWANFieldRule> _rules = new(StringComparer.Ordinal);

        public void Register(string tagName, LoRaWANFieldRule rule)
        {
            if (string.IsNullOrEmpty(tagName) || (rule is null) || rule.IsEmpty)
            {
                return;
            }

            _rules[tagName] = rule;
        }

        public LoRaWANFieldRule Get(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
            {
                return null;
            }

            return _rules.TryGetValue(tagName, out LoRaWANFieldRule rule) ? rule : null;
        }

        /// <summary>
        /// Resolves the byte offset of the field published under
        /// <paramref name="alias"/>, so a gate can be evaluated without a
        /// second pass over the Thing Description at read time.
        /// </summary>
        public (int? Offset, int Length) ResolveAlias(string alias)
        {
            if (string.IsNullOrEmpty(alias))
            {
                return (null, 1);
            }

            foreach (LoRaWANFieldRule rule in _rules.Values)
            {
                if (string.Equals(rule.Alias, alias, StringComparison.Ordinal)
                    && rule.DiscriminatorOffset.HasValue)
                {
                    return (rule.DiscriminatorOffset, rule.DiscriminatorLength);
                }
            }

            return (null, 1);
        }

        /// <summary>
        /// Applies a <c>lorav:valueMap</c>, returning the mapped value when the
        /// raw value matches an entry.
        /// </summary>
        public static object ApplyValueMap(IReadOnlyList<ValueMapEntry> valueMap, object rawValue)
        {
            if ((valueMap is null) || (valueMap.Count == 0) || (rawValue is null))
            {
                return rawValue;
            }

            long raw;

            try
            {
                raw = Convert.ToInt64(rawValue, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                // A non-integral value cannot match a wire value; leave it alone
                // rather than failing the whole read.
                return rawValue;
            }

            ValueMapEntry match = valueMap.FirstOrDefault(entry => entry.WireValue == raw);

            return match is null ? rawValue : match.Value;
        }
    }
}
