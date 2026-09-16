namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using Xunit;

    /// <summary>
    /// Tests for <c>lorav:derived</c>, using the worked examples from the
    /// Eclipse Thingweb LoRaWAN binding documentation.
    /// <para>
    /// A derived value is never transmitted: it is computed from values other
    /// fields already decoded. The distinction that matters throughout is
    /// between a descriptor that <em>replaces</em> the wire value (and so reads
    /// no bytes) and a <c>transform</c>-only one that post-processes a value
    /// that was read.
    /// </para>
    /// </summary>
    public class LoRaWANDerivedValueTests
    {
        private static DerivedDescriptor Parse(string json) =>
            JsonConvert.DeserializeObject<DerivedDescriptor>(json);

        private static Func<string, object> Resolver(params (string Name, object Value)[] values)
        {
            Dictionary<string, object> map = new(StringComparer.Ordinal);

            foreach ((string name, object value) in values)
            {
                map[name] = value;
            }

            return reference =>
            {
                string key = reference.StartsWith('$') ? reference[1..] : reference;

                return map.TryGetValue(key, out object value) ? value : null;
            };
        }

        [Fact]
        public void The_specs_volumetric_water_content_example_evaluates()
        {
            // Verbatim from the W3C binding, which absorbed the lorav:derived
            // definition in a later revision. Its examples use camelCase
            // references, so this also pins down that the $name convention is
            // not sensitive to the naming style an author happens to use.
            DerivedDescriptor derived = Parse("""
            {
              "ref": "$dielectricPermittivity",
              "polynomial": [ 4.3e-06, -0.00055, 0.0292, -0.053 ]
            }
            """);

            object result = LoRaWANFieldRules.Evaluate(
                derived, null, Resolver(("dielectricPermittivity", 20.0)));

            double expected = 4.3e-06 + (-0.00055 * 20) + (0.0292 * 400) + (-0.053 * 8000);

            Assert.Equal(expected, Assert.IsType<double>(result), 6);
        }

        [Fact]
        public void The_specs_albedo_example_guards_its_division()
        {
            // Also verbatim from the W3C binding: reflected over incoming
            // radiation, guarded so a night-time reading cannot divide by zero.
            DerivedDescriptor derived = Parse("""
            {
              "compute": { "op": "div", "a": "$reflectedRadiation", "b": "$incomingRadiation" },
              "guard": {
                "when": [
                  { "field": "$incomingRadiation", "gt": 0 },
                  { "field": "$reflectedRadiation", "gte": 0 }
                ],
                "else": 0
              }
            }
            """);

            object daylight = LoRaWANFieldRules.Evaluate(
                derived, null, Resolver(("reflectedRadiation", 200.0), ("incomingRadiation", 800.0)));

            Assert.Equal(0.25, Assert.IsType<double>(daylight), 6);

            object night = LoRaWANFieldRules.Evaluate(
                derived, null, Resolver(("reflectedRadiation", 0.0), ("incomingRadiation", 0.0)));

            // Assert on the value itself, not a conversion: Convert.ToInt64(null)
            // is also 0, so a null would pass a converted comparison and hide a
            // guard that never ran.
            Assert.NotNull(night);
            Assert.Equal(0L, Convert.ToInt64(night));
        }

        [Fact]
        public void A_guard_is_checked_before_the_value_is_computed()
        {
            // The guard is a precondition. If the computation ran first, a
            // division by zero would fail on its own terms and return nothing,
            // never reaching the fallback the guard exists to provide.
            DerivedDescriptor derived = Parse("""
            {
              "compute": { "op": "div", "a": "$a", "b": "$b" },
              "guard": { "when": [ { "field": "$b", "gt": 0 } ], "else": -1 }
            }
            """);

            object result = LoRaWANFieldRules.Evaluate(
                derived, null, Resolver(("a", 5.0), ("b", 0.0)));

            Assert.NotNull(result);
            Assert.Equal(-1L, Convert.ToInt64(result));
        }

        [Fact]
        public void A_derived_descriptor_carrying_only_transform_keeps_its_wire_type()
        {
            // The spec states this as the distinguishing rule: "If lorav:derived
            // only carries transform, keep the wire type of the source value; if
            // it carries ref, polynomial, compute, or guard, use number."
            Assert.False(Parse("""{ "transform": [ { "add": 1 } ] }""").ReplacesWireValue);

            Assert.True(Parse("""{ "ref": "$x" }""").ReplacesWireValue);
            Assert.True(Parse("""{ "polynomial": [ 1 ] }""").ReplacesWireValue);
            Assert.True(Parse("""{ "compute": { "op": "div", "a": 1, "b": 2 } }""").ReplacesWireValue);
            Assert.True(Parse("""{ "guard": { "when": [], "else": 0 } }""").ReplacesWireValue);
        }

        [Fact]
        public void A_polynomial_applies_its_coefficients_in_ascending_power()
        {
            // Decentlab 5TM: volumetric water content from raw permittivity.
            DerivedDescriptor derived = Parse("""
            {
              "ref": "$dielectric_permittivity",
              "polynomial": [ 4.3e-06, -0.00055, 0.0292, -0.053 ]
            }
            """);

            object result = LoRaWANFieldRules.Evaluate(
                derived, null, Resolver(("dielectric_permittivity", 20.0)));

            // 4.3e-06 + (-0.00055)(20) + 0.0292(400) + (-0.053)(8000)
            double expected = 4.3e-06 + (-0.00055 * 20) + (0.0292 * 400) + (-0.053 * 8000);

            Assert.Equal(expected, Assert.IsType<double>(result), 6);
        }

        [Fact]
        public void A_compute_divides_two_referenced_values()
        {
            // Decentlab albedo: reflected / incoming radiation.
            DerivedDescriptor derived = Parse("""
            {
              "compute": { "op": "div", "a": "$reflected_radiation", "b": "$incoming_radiation" }
            }
            """);

            object result = LoRaWANFieldRules.Evaluate(
                derived, null, Resolver(("reflected_radiation", 200.0), ("incoming_radiation", 800.0)));

            Assert.Equal(0.25, Assert.IsType<double>(result), 6);
        }

        [Fact]
        public void A_guard_falls_back_when_a_clause_fails()
        {
            // The albedo example guards against a night-time divide by zero.
            DerivedDescriptor derived = Parse("""
            {
              "compute": { "op": "div", "a": "$reflected_radiation", "b": "$incoming_radiation" },
              "guard": {
                "when": [
                  { "field": "$incoming_radiation", "gt": 0 },
                  { "field": "$reflected_radiation", "gte": 0 }
                ],
                "else": 0
              }
            }
            """);

            object night = LoRaWANFieldRules.Evaluate(
                derived, null, Resolver(("reflected_radiation", 0.0), ("incoming_radiation", 0.0)));

            // Convert.ToInt64(null) is also 0, so assert the value is present
            // before comparing it.
            Assert.NotNull(night);
            Assert.Equal(0L, Convert.ToInt64(night));
        }

        [Fact]
        public void A_guard_permits_the_computed_value_when_every_clause_holds()
        {
            DerivedDescriptor derived = Parse("""
            {
              "compute": { "op": "div", "a": "$reflected", "b": "$incoming" },
              "guard": { "when": [ { "field": "$incoming", "gt": 0 } ], "else": 0 }
            }
            """);

            object result = LoRaWANFieldRules.Evaluate(
                derived, null, Resolver(("reflected", 300.0), ("incoming", 1200.0)));

            Assert.Equal(0.25, Assert.IsType<double>(result), 6);
        }

        [Fact]
        public void A_transform_post_processes_the_value_read_from_the_wire()
        {
            // transform is the exception: it keeps a real wireType because the
            // value it adjusts was actually transmitted.
            DerivedDescriptor derived = Parse("""
            { "transform": [ { "mult": 2 }, { "add": 5 }, { "round": 1 } ] }
            """);

            Assert.False(derived.ReplacesWireValue);

            object result = LoRaWANFieldRules.Evaluate(derived, 10.0, Resolver());

            Assert.Equal(25.0, Assert.IsType<double>(result), 6);
        }

        [Fact]
        public void Transform_steps_apply_in_order()
        {
            DerivedDescriptor derived = Parse("""
            { "transform": [ { "add": 10 }, { "div": 2 } ] }
            """);

            // (4 + 10) / 2, not 4 + (10 / 2).
            object result = LoRaWANFieldRules.Evaluate(derived, 4.0, Resolver());

            Assert.Equal(7.0, Assert.IsType<double>(result), 6);
        }

        [Fact]
        public void A_descriptor_without_transform_replaces_the_wire_value()
        {
            // This is what makes such a form declare wireType "number" and read
            // no payload bytes.
            Assert.True(Parse("""{ "ref": "$x" }""").ReplacesWireValue);
            Assert.True(Parse("""{ "polynomial": [ 1, 2 ] }""").ReplacesWireValue);
            Assert.True(Parse("""{ "compute": { "op": "add", "a": 1, "b": 2 } }""").ReplacesWireValue);
            Assert.True(Parse("""{ "guard": { "when": [], "else": 0 } }""").ReplacesWireValue);
        }

        [Fact]
        public void An_unresolvable_reference_yields_no_value()
        {
            // Substituting zero would look like a real reading.
            DerivedDescriptor derived = Parse("""{ "ref": "$missing", "polynomial": [ 0, 1 ] }""");

            Assert.Null(LoRaWANFieldRules.Evaluate(derived, null, Resolver()));
        }

        [Fact]
        public void A_division_by_zero_yields_no_value_rather_than_infinity()
        {
            // Infinity would propagate silently through any later transform.
            DerivedDescriptor derived = Parse("""
            { "compute": { "op": "div", "a": "$a", "b": "$b" } }
            """);

            Assert.Null(LoRaWANFieldRules.Evaluate(derived, null, Resolver(("a", 1.0), ("b", 0.0))));
        }

        [Fact]
        public void An_unknown_compute_operation_is_rejected()
        {
            // Guessing at an operator would produce a confidently wrong number.
            DerivedDescriptor derived = Parse("""
            { "compute": { "op": "modulo", "a": 7, "b": 3 } }
            """);

            NotSupportedException ex = Assert.Throws<NotSupportedException>(
                () => LoRaWANFieldRules.Evaluate(derived, null, Resolver()));

            Assert.Contains("modulo", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_compute_accepts_literal_operands()
        {
            DerivedDescriptor derived = Parse("""
            { "compute": { "op": "mult", "a": "$x", "b": 3 } }
            """);

            object result = LoRaWANFieldRules.Evaluate(derived, null, Resolver(("x", 5.0)));

            Assert.Equal(15.0, Assert.IsType<double>(result), 6);
        }

        [Theory]
        [InlineData("add", 7.0)]
        [InlineData("sub", 1.0)]
        [InlineData("mult", 12.0)]
        [InlineData("div", 1.3333333)]
        public void Each_compute_operation_is_supported(string op, double expected)
        {
            DerivedDescriptor derived = Parse($$"""
            { "compute": { "op": "{{op}}", "a": 4, "b": 3 } }
            """);

            object result = LoRaWANFieldRules.Evaluate(derived, null, Resolver());

            Assert.Equal(expected, Assert.IsType<double>(result), 5);
        }
    }
}
