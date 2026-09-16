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

        /// <summary>
        /// Bytes to skip after a tag before this value starts, because earlier
        /// <c>lorav:slot</c> values in the same tag group occupy them.
        /// <para>
        /// This cannot live in the address: the asset dispatches on the number
        /// of parts in the address string, so an extra segment would stop the
        /// tag being matched at all.
        /// </para>
        /// </summary>
        public int TagGroupOffset { get; init; }

        /// <summary>
        /// Descriptor for a computed value, from <c>lorav:derived</c>.
        /// </summary>
        public DerivedDescriptor Derived { get; init; }

        /// <summary>True when this field carries no conditional or mapping rules.</summary>
        public bool IsEmpty =>
            (PresentWhen is null)
            && ((ValueMap is null) || (ValueMap.Count == 0))
            && (TagGroupOffset == 0)
            && (Derived is null);
    }

    /// <summary>
    /// Per-asset registry of <see cref="LoRaWANFieldRule"/>, keyed by tag name.
    /// </summary>
    public sealed class LoRaWANFieldRules
    {
        private readonly ConcurrentDictionary<string, LoRaWANFieldRule> _rules = new(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, AssetTag> _tags = new(StringComparer.Ordinal);

        /// <summary>
        /// Every tag created for this asset, so a <c>$name</c> reference in a
        /// derived value can be resolved to the field it names.
        /// </summary>
        public IReadOnlyDictionary<string, AssetTag> Tags => _tags;

        public void Register(string tagName, LoRaWANFieldRule rule)
        {
            if (string.IsNullOrEmpty(tagName) || (rule is null) || rule.IsEmpty)
            {
                return;
            }

            _rules[tagName] = rule;
        }

        /// <summary>
        /// Records a tag so derived values can reference it by name. Every tag
        /// is recorded, not only those carrying rules, because any field may be
        /// the input to a computation.
        /// </summary>
        public void RegisterTag(AssetTag tag)
        {
            if (!string.IsNullOrEmpty(tag?.Name))
            {
                _tags[tag.Name] = tag;
            }
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

        /// <summary>
        /// Evaluates a <c>lorav:derived</c> descriptor.
        /// <para>
        /// The keys apply in their documented order: <c>ref</c> supplies the
        /// input, <c>polynomial</c> or <c>compute</c> produces a value from it,
        /// <c>guard</c> can veto the result, and <c>transform</c> post-processes
        /// whatever remains.
        /// </para>
        /// </summary>
        /// <param name="derived">The descriptor to evaluate.</param>
        /// <param name="wireValue">
        /// The value read from the wire, for a <c>transform</c>-only descriptor.
        /// Ignored when the descriptor replaces the wire value.
        /// </param>
        /// <param name="resolve">
        /// Resolves a <c>$name</c> reference to another value's current reading,
        /// returning <c>null</c> when it is unavailable.
        /// </param>
        public static object Evaluate(
            DerivedDescriptor derived,
            object wireValue,
            Func<string, object> resolve)
        {
            ArgumentNullException.ThrowIfNull(derived);

            double? value = null;

            if (derived.Ref != null)
            {
                value = ToNumber(resolve?.Invoke(derived.Ref));

                // A reference that cannot be resolved yields no value rather
                // than a zero, which would look like a real reading.
                if (value is null)
                {
                    return null;
                }
            }
            else if (!derived.ReplacesWireValue)
            {
                // transform-only: post-process the value read from the wire.
                value = ToNumber(wireValue);
            }

            if (derived.Polynomial is { Length: > 0 })
            {
                if (value is null)
                {
                    return null;
                }

                double x = value.Value;
                double result = 0;
                double power = 1;

                foreach (double coefficient in derived.Polynomial)
                {
                    result += coefficient * power;
                    power *= x;
                }

                value = result;
            }

            // The guard is a PRECONDITION, so it is checked before the value is
            // computed rather than after. The specification's own albedo example
            // guards against dividing by a night-time zero: evaluating the
            // division first would fail on its own terms and never reach the
            // fallback the guard exists to supply.
            if ((derived.Guard != null) && !IsGuardSatisfied(derived.Guard, resolve))
            {
                return derived.Guard.Else;
            }

            if (derived.Compute != null)
            {
                value = EvaluateCompute(derived.Compute, resolve);

                if (value is null)
                {
                    return null;
                }
            }

            if (derived.Transform is { Length: > 0 })
            {
                if (value is null)
                {
                    return null;
                }

                foreach (TransformStep step in derived.Transform)
                {
                    value = ApplyTransform(step, value.Value);
                }
            }

            return value;
        }

        private static double? EvaluateCompute(ComputeOperation compute, Func<string, object> resolve)
        {
            double? a = ResolveOperand(compute.A, resolve);
            double? b = ResolveOperand(compute.B, resolve);

            if ((a is null) || (b is null))
            {
                return null;
            }

            switch (compute.Op?.ToLowerInvariant())
            {
                case "add":
                    return a.Value + b.Value;

                case "sub":
                    return a.Value - b.Value;

                case "mult":
                case "mul":
                    return a.Value * b.Value;

                case "div":
                    // An unguarded division by zero would yield infinity and
                    // propagate through the rest of the pipeline.
                    return b.Value == 0 ? null : a.Value / b.Value;

                default:
                    throw new NotSupportedException(
                        $"The LoRaWAN 'compute' operation '{compute.Op}' is not one of add, sub, mult or div.");
            }
        }

        private static bool IsGuardSatisfied(GuardCondition guard, Func<string, object> resolve)
        {
            if (guard.When is null)
            {
                return true;
            }

            foreach (GuardClause clause in guard.When)
            {
                double? actual = ToNumber(resolve?.Invoke(clause.Field));

                if (actual is null)
                {
                    return false;
                }

                if ((clause.GreaterThan.HasValue && !(actual.Value > clause.GreaterThan.Value))
                    || (clause.GreaterThanOrEqual.HasValue && !(actual.Value >= clause.GreaterThanOrEqual.Value))
                    || (clause.LessThan.HasValue && !(actual.Value < clause.LessThan.Value))
                    || (clause.LessThanOrEqual.HasValue && !(actual.Value <= clause.LessThanOrEqual.Value))
                    || (clause.EqualTo.HasValue && (actual.Value != clause.EqualTo.Value)))
                {
                    return false;
                }
            }

            return true;
        }

        private static double ApplyTransform(TransformStep step, double value)
        {
            if (step.Mult.HasValue)
            {
                value *= step.Mult.Value;
            }

            if (step.Div.HasValue && (step.Div.Value != 0))
            {
                value /= step.Div.Value;
            }

            if (step.Add.HasValue)
            {
                value += step.Add.Value;
            }

            if (step.Round.HasValue)
            {
                value = Math.Round(value, step.Round.Value, MidpointRounding.AwayFromZero);
            }

            return value;
        }

        /// <summary>
        /// Resolves a compute operand, which is either a <c>$name</c> reference
        /// or a literal number.
        /// </summary>
        private static double? ResolveOperand(object operand, Func<string, object> resolve)
        {
            if (operand is null)
            {
                return null;
            }

            string text = operand.ToString();

            if (text.StartsWith('$'))
            {
                return ToNumber(resolve?.Invoke(text));
            }

            return ToNumber(operand);
        }

        private static double? ToNumber(object value)
        {
            if (value is null)
            {
                return null;
            }

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                return null;
            }
        }
    }
}
