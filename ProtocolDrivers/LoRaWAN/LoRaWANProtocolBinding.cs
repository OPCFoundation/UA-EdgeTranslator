
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

        /// <summary>UN/CEFACT unit code for the decoded value.</summary>
        [JsonProperty("lorav:unece")]
        public string Unece { get; set; }

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
        /// These describe conditional presence, branching layouts and a derived
        /// value expression language. Silently ignoring them would mis-decode a
        /// payload rather than fail, so callers surface them instead.
        /// </para>
        /// </summary>
        public static IReadOnlyList<string> UnsupportedTerms { get; } =
        [
            "lorav:compute",
            "lorav:derived",
            "lorav:transform",
            "lorav:polynomial",
            "lorav:switchField",
            "lorav:switchValue",
            "lorav:presentWhen",
            "lorav:presenceBit",
            "lorav:presenceField",
            "lorav:guard",
            "lorav:tagFields",
            "lorav:ref",
            "lorav:var"
        ];
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

        [JsonProperty("lorav:brand")]
        public string Brand { get; set; }

        [JsonProperty("lorav:model")]
        public string Model { get; set; }

        [JsonProperty("lorav:hardwareVersion")]
        public string HardwareVersion { get; set; }

        [JsonProperty("lorav:softwareVersion")]
        public string SoftwareVersion { get; set; }
    }
}
