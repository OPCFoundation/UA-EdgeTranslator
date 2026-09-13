
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
        /// <c>lorav:derived</c> is a container for an expression language whose
        /// keys (<c>compute</c>, <c>polynomial</c>, <c>transform</c>,
        /// <c>guard</c>, <c>ref</c>) the specification names but does not define
        /// a schema or worked example for. Implementing it would mean inventing
        /// a syntax and then silently disagreeing with whatever the binding
        /// settles on, so it stays rejected until the specification pins it
        /// down.
        /// </para>
        /// <para>
        /// Rejecting is the conservative choice: silently ignoring the term
        /// would mis-decode a payload and report a plausible-looking wrong
        /// value.
        /// </para>
        /// </summary>
        public static IReadOnlyList<string> UnsupportedTerms { get; } =
        [
            "lorav:derived"
        ];

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
