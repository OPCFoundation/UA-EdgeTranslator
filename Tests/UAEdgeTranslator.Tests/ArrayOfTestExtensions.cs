namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.Collections.Generic;
    using Opc.Ua;

    /// <summary>
    /// Test helpers for SDK 2.0.0 <see cref="ArrayOf{T}"/> values.
    /// </summary>
    internal static class ArrayOfTestExtensions
    {
        /// <summary>
        /// Copies an <see cref="ArrayOf{T}"/> into a list so tests can use LINQ and
        /// <see cref="List{T}"/> members. <see cref="ArrayOf{T}"/> is not
        /// <see cref="IEnumerable{T}"/>, and a null instance enumerates as empty, so this
        /// also replaces the old <c>?? new XxxCollection()</c> null fallback.
        /// </summary>
        public static List<T> ToList<T>(this ArrayOf<T> source)
        {
            List<T> result = new(source.Count);
            foreach (T item in source)
            {
                result.Add(item);
            }

            return result;
        }

        /// <summary>
        /// Returns the plain CLR value held by a <see cref="DataValue"/> (e.g. <c>string[]</c>,
        /// <c>NodeId</c>, <c>bool</c>), as <c>DataValue.Value</c> did before SDK 2.0.0.
        /// Uses the production <see cref="VariantConversion"/> so tests assert against the
        /// same value contract the server gives drivers.
        /// </summary>
        public static object ClrValue(this DataValue value)
        {
            return VariantConversion.ToClrValue(value.WrappedValue);
        }

        /// <summary>
        /// Returns the plain CLR value held by a <see cref="Variant"/>, as
        /// <c>Variant.Value</c> did before SDK 2.0.0.
        /// </summary>
        public static object ClrValue(this Variant value)
        {
            return VariantConversion.ToClrValue(value);
        }

        /// <summary>
        /// Builds method-call input arguments from runtime-typed values. A null input
        /// yields an empty (not null) array, matching the previous
        /// <c>new VariantCollection()</c> so the request on the wire is unchanged.
        /// </summary>
        public static ArrayOf<Variant> ToVariantArray(object[] values)
        {
            if (values == null)
            {
                return new ArrayOf<Variant>(Array.Empty<Variant>());
            }

            Variant[] variants = new Variant[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null)
                {
                    // A null argument stays a null Variant, which is what the server's
                    // missing-argument guards (BadInvalidArgument) are written to reject.
                    variants[i] = default;
                    continue;
                }

                // Test inputs are runtime-typed; the (object, TypeInfo) constructor is the
                // only overload that accepts them (see NodeFactory.CreateVariable).
#pragma warning disable CS0618 // Type or member is obsolete
                variants[i] = new Variant(values[i], TypeInfo.Construct(values[i]));
#pragma warning restore CS0618
            }

            return new ArrayOf<Variant>(variants);
        }
    }

    /// <summary>
    /// Assertions for node identifiers, which are value types in SDK 2.0.0.
    /// </summary>
    internal static class NodeIdAssert
    {
        /// <summary>
        /// Replaces <c>Assert.Null</c> for methods that signal "not found / rejected" with
        /// <see cref="NodeId.Null"/> or <see cref="ExpandedNodeId.Null"/>. Both are structs
        /// now, so a boxed result is never <c>null</c> and <c>Assert.Null</c> would always
        /// fail. Any other type fails the assertion rather than passing by accident.
        /// </summary>
        public static void IsNullId(object value)
        {
            bool isNullId = value switch
            {
                NodeId nodeId => nodeId.IsNull,
                ExpandedNodeId expandedNodeId => expandedNodeId.IsNull,
                _ => false
            };

            Xunit.Assert.True(isNullId, $"Expected a null node id but got: {value ?? "<null reference>"} ({value?.GetType().Name ?? "no type"}).");
        }
    }
}
