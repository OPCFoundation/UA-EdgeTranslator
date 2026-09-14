
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Form-level vocabulary of the official W3C WoT LoRaWAN binding
    /// (<see cref="BindingNamespace"/>, conventional prefix <c>lorav</c>).
    /// <para>
    /// The binding is currently uplink-only: a LoRaWAN device transmits on its
    /// own schedule, so values are described as WoT events carrying
    /// <c>subscribeevent</c> forms rather than as readable properties. The form
    /// terms below say how a single decoded value is located inside the
    /// application payload bytes.
    /// </para>
    /// </summary>
    public class LoRaWANForm
    {
        /// <summary>
        /// The official binding namespace. Thing Descriptions bind this to the
        /// <c>lorav</c> prefix in their <c>@context</c>.
        /// </summary>
        public const string BindingNamespace = "https://www.w3.org/2026/wot/lorawan#";

        [JsonProperty("href")]
        public string Href { get; set; }

        [JsonProperty("op")]
        public Op[] Op { get; set; }

        [JsonProperty("type")]
        public TypeString Type { get; set; }

        // ----- Official field location terms --------------------------------

        /// <summary>Zero-based offset of the field within the decrypted payload.</summary>
        [JsonProperty("lorav:byteOffset")]
        public int? ByteOffset { get; set; }

        /// <summary>Length of the field in bytes. Defaults from the wire type when omitted.</summary>
        [JsonProperty("lorav:byteLength")]
        public int? ByteLength { get; set; }

        /// <summary>
        /// The on-the-wire type of the raw field, e.g. <c>xsd:short</c>,
        /// <c>xsd:unsignedShort</c>, <c>xsd:int</c>, <c>xsd:byte</c>.
        /// </summary>
        [JsonProperty("lorav:wireType")]
        public string WireType { get; set; }

        /// <summary>Byte order of the raw field: <c>"big"</c> or <c>"little"</c>.</summary>
        [JsonProperty("lorav:endian")]
        public string Endian { get; set; }

        // ----- Official scaling terms ---------------------------------------

        /// <summary>Bitmask applied to the raw value before scaling.</summary>
        [JsonProperty("lorav:bitmask")]
        public string BitMask { get; set; }

        /// <summary>Factor the raw value is multiplied by.</summary>
        [JsonProperty("lorav:multiplier")]
        public float? Multiplier { get; set; }

        /// <summary>Factor the raw value is divided by (e.g. 100 for centi-units).</summary>
        [JsonProperty("lorav:divisor")]
        public float? Divisor { get; set; }

        /// <summary>Constant added after multiplication/division.</summary>
        [JsonProperty("lorav:addend")]
        public float? Addend { get; set; }

        // ----- Conditional presence and grouping ----------------------------

        /// <summary>
        /// Name under which other conditional blocks reference this value.
        /// A <see cref="PresentWhen"/> gate names its discriminator by alias.
        /// </summary>
        [JsonProperty("lorav:alias")]
        public string Alias { get; set; }

        /// <summary>
        /// Condition gating this value by flag bit or discriminator value:
        /// <c>{"field": "flags", "bit": n}</c> or
        /// <c>{"field": "kind", "value": n}</c>.
        /// </summary>
        [JsonProperty("lorav:presentWhen")]
        public PresentWhenCondition PresentWhen { get; set; }

        /// <summary>
        /// Mapping from wire integers to decoded values, as
        /// <c>[{"wireValue": n, "value": ...}]</c>.
        /// </summary>
        [JsonProperty("lorav:valueMap")]
        public ValueMapEntry[] ValueMap { get; set; }

        /// <summary>The LoRaWAN application port this form decodes.</summary>
        [JsonProperty("lorav:fPort")]
        public int? FPort { get; set; }

        /// <summary>
        /// Tag values that select this field in a <c>tlv</c> or <c>ctv</c>
        /// layout, e.g. <c>[1, 117]</c> for channel 1, type 117.
        /// <para>
        /// A tagged field is found by scanning the payload for the tag bytes
        /// rather than by a fixed <c>lorav:byteOffset</c>, because a device may
        /// omit a measurement or reorder them between uplinks.
        /// </para>
        /// </summary>
        [JsonProperty("lorav:tag")]
        public int[] Tag { get; set; }

        /// <summary>
        /// Order of this value within its group, when several values share one
        /// locator (the same <c>lorav:tag</c> or <c>lorav:byteOffset</c>).
        /// </summary>
        [JsonProperty("lorav:slot")]
        public int? Slot { get; set; }

        /// <summary>
        /// Reserved bytes consumed before this value within its group.
        /// </summary>
        [JsonProperty("lorav:padBefore")]
        public int? PadBefore { get; set; }

        /// <summary>
        /// Descriptor for a value computed from already-decoded values rather
        /// than read from the wire.
        /// </summary>
        [JsonProperty("lorav:derived")]
        public DerivedDescriptor Derived { get; set; }

        [JsonProperty("pollingTime")]
        public long PollingTime { get; set; }

        /// <summary>
        /// True when the raw field is most-significant-byte first, per
        /// <c>lorav:endian</c>. The binding's default is big endian, which
        /// matches LoRaWAN payload conventions.
        /// </summary>
        public bool IsBigEndian()
        {
            // Absent the term, big endian is the sane default: it is what the
            // specification's own examples use and what device datasheets
            // overwhelmingly specify for multi-byte fields.
            if (string.IsNullOrEmpty(Endian))
            {
                return true;
            }

            return Endian.Equals("big", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Combined scaling factor: <c>multiplier / divisor</c>. The binding
        /// allows either or both; a divisor of 100 and a multiplier of 0.01
        /// describe the same conversion.
        /// </summary>
        public float? EffectiveMultiplier()
        {
            if (Multiplier is null && Divisor is null)
            {
                return null;
            }

            float scale = Multiplier ?? 1.0f;

            // A zero divisor is meaningless and would produce infinity; treat it
            // as "not specified" rather than corrupting every decoded value.
            if (Divisor is > 0 or < 0)
            {
                scale /= Divisor.Value;
            }

            return scale;
        }

        /// <summary>
        /// Terms defined by the binding that this driver does not yet decode.
        /// <para>
        /// Empty: every term the binding defines is now decoded. Withdrawn
        /// terms are handled separately by <see cref="WithdrawnTerms"/>, which
        /// rejects them naming their replacement.
        /// </para>
        /// </summary>
        public static IReadOnlyList<string> UnsupportedTerms { get; } = [];

        /// <summary>
        /// Terms withdrawn by the binding before 0.3, mapped to the term that
        /// replaces them.
        /// <para>
        /// These are rejected rather than silently accepted: the replacements
        /// are not merely renames, so decoding an old term as though it were
        /// its successor would change the meaning of a payload.
        /// </para>
        /// </summary>
        public static IReadOnlyDictionary<string, string> WithdrawnTerms { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lorav:type"] = "lorav:wireType",
                ["lorav:offset"] = "lorav:addend",
                ["lorav:length"] = "lorav:byteLength",
                ["lorav:enum"] = "lorav:valueMap",
                ["lorav:presenceField"] = "lorav:presentWhen with field/bit",
                ["lorav:presenceBit"] = "lorav:presentWhen with field/bit",
                ["lorav:switchField"] = "lorav:presentWhen with field/value",
                ["lorav:switchValue"] = "lorav:presentWhen with field/value",
                ["lorav:var"] = "lorav:alias",
                ["lorav:ref"] = "keys inside lorav:derived",
                ["lorav:polynomial"] = "keys inside lorav:derived",
                ["lorav:compute"] = "keys inside lorav:derived",
                ["lorav:guard"] = "keys inside lorav:derived",
                ["lorav:transform"] = "keys inside lorav:derived",
                ["lorav:validRange"] = "the TD core terms minimum / maximum",
                ["lorav:unece"] = "the TD core term unit",
                ["lorav:brand"] = "schema:manufacturer",
                ["lorav:model"] = "schema:mpn",
                ["lorav:hardwareVersion"] = "schema:version",
                ["lorav:softwareVersion"] = "schema:softwareVersion"
            };
    }

    /// <summary>
    /// A <c>lorav:presentWhen</c> condition. The referenced field is named by
    /// its <c>lorav:alias</c>; exactly one of <see cref="Bit"/> or
    /// <see cref="Value"/> selects the test.
    /// </summary>
    public class PresentWhenCondition
    {
        [JsonProperty("field")]
        public string Field { get; set; }

        /// <summary>Bit index that must be set in the referenced field.</summary>
        [JsonProperty("bit")]
        public int? Bit { get; set; }

        /// <summary>Discriminator value the referenced field must equal.</summary>
        [JsonProperty("value")]
        public long? Value { get; set; }
    }

    /// <summary>A single <c>lorav:valueMap</c> entry.</summary>
    public class ValueMapEntry
    {
        [JsonProperty("wireValue")]
        public long WireValue { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }
    }

    /// <summary>
    /// One position in a <c>tlv</c>/<c>ctv</c> tag, from
    /// <c>lorav:tagFields</c>.
    /// </summary>
    public class TagFieldDefinition
    {
        /// <summary>Name of this tag position, e.g. <c>channel_id</c>.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Wire type of this tag position, e.g. <c>u8</c>.</summary>
        [JsonProperty("type")]
        public string Type { get; set; }
    }

    /// <summary>
    /// A <c>lorav:derived</c> descriptor: where a computed value comes from.
    /// <para>
    /// The keys apply in the order they are declared here. <see cref="Ref"/>,
    /// <see cref="Polynomial"/>, <see cref="Compute"/> and <see cref="Guard"/>
    /// describe a value that is never transmitted, which is why such a form
    /// declares <c>lorav:wireType: "number"</c> and occupies no payload bytes.
    /// <see cref="Transform"/> is the exception: it post-processes a value that
    /// <em>was</em> read from the wire, so it sits alongside a real wire type
    /// and byte offset rather than replacing them.
    /// </para>
    /// </summary>
    public class DerivedDescriptor
    {
        /// <summary>The input: <c>$name</c> of the value this is computed from.</summary>
        [JsonProperty("ref")]
        public string Ref { get; set; }

        /// <summary>
        /// Coefficients <c>[c0, c1, c2, …]</c> applied to the input as
        /// <c>c0 + c1·x + c2·x² + …</c>.
        /// </summary>
        [JsonProperty("polynomial")]
        public double[] Polynomial { get; set; }

        /// <summary>A binary operation over two values or constants.</summary>
        [JsonProperty("compute")]
        public ComputeOperation Compute { get; set; }

        /// <summary>A precondition, with a fallback when it is not met.</summary>
        [JsonProperty("guard")]
        public GuardCondition Guard { get; set; }

        /// <summary>Ordered post-processing steps.</summary>
        [JsonProperty("transform")]
        public TransformStep[] Transform { get; set; }

        /// <summary>
        /// True when this descriptor replaces the wire value entirely, rather
        /// than post-processing one. Such a value reads no payload bytes.
        /// </summary>
        public bool ReplacesWireValue =>
            (Ref != null) || (Polynomial != null) || (Compute != null) || (Guard != null);
    }

    /// <summary>A <c>compute</c> operation, e.g. <c>{"op":"div","a":…,"b":…}</c>.</summary>
    public class ComputeOperation
    {
        [JsonProperty("op")]
        public string Op { get; set; }

        [JsonProperty("a")]
        public object A { get; set; }

        [JsonProperty("b")]
        public object B { get; set; }
    }

    /// <summary>
    /// A <c>guard</c>: the value falls back to <see cref="Else"/> when any
    /// <see cref="When"/> clause fails.
    /// </summary>
    public class GuardCondition
    {
        [JsonProperty("when")]
        public GuardClause[] When { get; set; }

        [JsonProperty("else")]
        public object Else { get; set; }
    }

    /// <summary>A single comparison inside a <c>guard</c>'s <c>when</c> list.</summary>
    public class GuardClause
    {
        [JsonProperty("field")]
        public string Field { get; set; }

        [JsonProperty("gt")]
        public double? GreaterThan { get; set; }

        [JsonProperty("gte")]
        public double? GreaterThanOrEqual { get; set; }

        [JsonProperty("lt")]
        public double? LessThan { get; set; }

        [JsonProperty("lte")]
        public double? LessThanOrEqual { get; set; }

        [JsonProperty("eq")]
        public double? EqualTo { get; set; }
    }

    /// <summary>One step of a <c>transform</c> pipeline.</summary>
    public class TransformStep
    {
        [JsonProperty("add")]
        public double? Add { get; set; }

        [JsonProperty("div")]
        public double? Div { get; set; }

        [JsonProperty("mult")]
        public double? Mult { get; set; }

        [JsonProperty("round")]
        public int? Round { get; set; }
    }

    /// <summary>
    /// Thing-level vocabulary of the W3C WoT LoRaWAN binding. These terms sit
    /// on the Thing itself and identify the device and its network parameters.
    /// </summary>
    public class LoRaWANThingDescription
    {
        /// <summary>Payload codec family, e.g. <c>"fixed"</c> or <c>"ctv"</c>.</summary>
        [JsonProperty("lorav:payloadLayout")]
        public string PayloadLayout { get; set; }

        /// <summary>
        /// Definitions of the leading fields that make up a tag in a
        /// <c>tlv</c> or <c>ctv</c> layout, e.g.
        /// <c>[{"name":"channel_id","type":"u8"},{"name":"channel_type","type":"u8"}]</c>.
        /// <para>
        /// This names what each position in a form's <c>lorav:tag</c> means, so
        /// it also fixes how many elements that array must have.
        /// </para>
        /// </summary>
        [JsonProperty("lorav:tagFields")]
        public TagFieldDefinition[] TagFields { get; set; }

        [JsonProperty("lorav:devEUI")]
        public string DevEUI { get; set; }

        [JsonProperty("lorav:joinEUI")]
        public string JoinEUI { get; set; }

        [JsonProperty("lorav:macVersion")]
        public string MacVersion { get; set; }

        [JsonProperty("lorav:region")]
        public string Region { get; set; }

        [JsonProperty("lorav:frequencyPlan")]
        public string FrequencyPlan { get; set; }

        /// <summary>
        /// Expected minutes between unprompted uplinks. A LoRaWAN device
        /// transmits on its own schedule, so this is how often a value can
        /// actually change - polling faster only re-reads the cached payload.
        /// </summary>
        [JsonProperty("lorav:defaultEventingFrequencyMinutes")]
        public double? DefaultEventingFrequencyMinutes { get; set; }

        // ----- Device description -------------------------------------------
        //
        // The binding withdrew lorav:brand, lorav:model, lorav:hardwareVersion
        // and lorav:softwareVersion in favour of schema.org terms, so the
        // device description now reuses the vocabulary the wider WoT ecosystem
        // already understands rather than a LoRaWAN-specific duplicate.

        [JsonProperty("schema:manufacturer")]
        public string Manufacturer { get; set; }

        /// <summary>Manufacturer part number (schema.org <c>mpn</c>).</summary>
        [JsonProperty("schema:mpn")]
        public string Mpn { get; set; }

        [JsonProperty("schema:version")]
        public string Version { get; set; }

        [JsonProperty("schema:softwareVersion")]
        public string SoftwareVersion { get; set; }
    }
}
