
namespace Opc.Ua.Edge.Translator.Models
{
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.Serialization;
    using System.Text;

    /// <summary>
    /// Form vocabulary of the W3C WoT S7Comm protocol binding
    /// (https://w3c.github.io/wot-binding-templates/bindings/protocols/s7comm/),
    /// prefix <c>s7v</c>, namespace <see cref="BindingNamespace"/>.
    /// <para>
    /// A form addresses one variable either with a TIA Portal-style address in its
    /// <c>href</c> (<c>./%25DB1.DBW20:INT</c>) or with the explicit address terms
    /// <c>s7v:area</c>, <c>s7v:dbNumber</c>, <c>s7v:byteOffset</c>, <c>s7v:bitOffset</c>
    /// and <c>s7v:length</c>. Thing Descriptions written before the binding
    /// (<c>s7:target</c>, <c>s7:dbnumber</c>, <c>s7:start</c>, <c>s7:pos</c>,
    /// <c>s7:maxlen</c>, <c>s7:s7type</c>, <c>type</c>, <c>pollingTime</c>) are still
    /// read: the legacy terms are mapped onto the binding terms when the form is
    /// deserialized.
    /// </para>
    /// </summary>
    public class S7Form
    {
        /// <summary>The provisional namespace of the binding vocabulary.</summary>
        public const string BindingNamespace = "https://www.w3.org/2026/wot/s7comm";

        /// <summary>The conventional prefix of the binding vocabulary.</summary>
        public const string Prefix = "s7v";

        /// <summary>The maximum length of a string when <c>s7v:length</c> is absent.</summary>
        public const int DefaultStringLength = 254;

        [JsonProperty("href")]
        public string Href { get; set; }

        [JsonProperty("op")]
        public Op[] Op { get; set; }

        /// <summary>S7 data type used to encode and decode the value.</summary>
        [JsonProperty("s7v:type", NullValueHandling = NullValueHandling.Ignore)]
        public S7DataType? Type { get; set; }

        /// <summary>S7Comm function; the default functions apply when absent.</summary>
        [JsonProperty("s7v:service", NullValueHandling = NullValueHandling.Ignore)]
        public string Service { get; set; }

        /// <summary>Requested polling interval for observeproperty, in milliseconds.</summary>
        [JsonProperty("s7v:pollingTime", NullValueHandling = NullValueHandling.Ignore)]
        public long? PollingTime { get; set; }

        /// <summary>Maximum time to wait for the controller response, in milliseconds.</summary>
        [JsonProperty("s7v:timeout", NullValueHandling = NullValueHandling.Ignore)]
        public long? Timeout { get; set; }

        /// <summary>Memory area of the explicit address.</summary>
        [JsonProperty("s7v:area", NullValueHandling = NullValueHandling.Ignore)]
        public S7MemoryArea? Area { get; set; }

        /// <summary>Data block number; required for the DB and DBI areas.</summary>
        [JsonProperty("s7v:dbNumber", NullValueHandling = NullValueHandling.Ignore)]
        public int? DbNumber { get; set; }

        /// <summary>Byte offset of the value inside the memory area.</summary>
        [JsonProperty("s7v:byteOffset", NullValueHandling = NullValueHandling.Ignore)]
        public int? ByteOffset { get; set; }

        /// <summary>Bit position inside the byte at <see cref="ByteOffset"/>; required for BOOL.</summary>
        [JsonProperty("s7v:bitOffset", NullValueHandling = NullValueHandling.Ignore)]
        public int? BitOffset { get; set; }

        /// <summary>Maximum characters of a STRING/WSTRING, or bytes of a value of variable size.</summary>
        [JsonProperty("s7v:length", NullValueHandling = NullValueHandling.Ignore)]
        public int? Length { get; set; }

        /// <summary>Factor applied to the controller value: value = raw * scale + offset.</summary>
        [JsonProperty("s7v:scale", NullValueHandling = NullValueHandling.Ignore)]
        public double? Scale { get; set; }

        /// <summary>Summand applied after <see cref="Scale"/>.</summary>
        [JsonProperty("s7v:offset", NullValueHandling = NullValueHandling.Ignore)]
        public double? Offset { get; set; }

        // Legacy terms, read but never written.

        private string _legacyType;

        [JsonProperty("type")]
        private string LegacyType { set => _legacyType = value; }

        [JsonProperty("s7:s7type")]
        private string LegacyS7Type
        {
            set
            {
                if (Type == null && S7DataTypes.TryParse(value, out S7DataType type))
                {
                    Type = type;
                }
            }
        }

        [JsonProperty("s7:target")]
        private string LegacyTarget
        {
            set
            {
                if (Area == null && S7MemoryAreas.TryParseLegacyTarget(value, out S7MemoryArea area))
                {
                    Area = area;
                }
            }
        }

        [JsonProperty("s7:dbnumber")]
        private int? LegacyDbNumber { set => DbNumber = DbNumber ?? value; }

        [JsonProperty("s7:start")]
        private int? LegacyStart { set => ByteOffset = ByteOffset ?? value; }

        [JsonProperty("s7:pos")]
        private int? LegacyPos { set => BitOffset = BitOffset ?? value; }

        [JsonProperty("s7:maxlen")]
        private int? LegacyMaxLen
        {
            set
            {
                if (Length == null && value.HasValue && value.Value > 0)
                {
                    Length = value;
                }
            }
        }

        [JsonProperty("pollingTime")]
        private long? LegacyPollingTime { set => PollingTime = PollingTime ?? value; }

        /// <summary>
        /// A legacy form named only the coarse XSD <c>type</c>; once the form is read the
        /// matching S7 type is used, unless a binding or Siemens type was given.
        /// </summary>
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Type == null && S7DataTypes.TryParseLegacyXsdType(_legacyType, out S7DataType type))
            {
                Type = type;
            }

            // Legacy forms carried a DB number of 0 and a bit position of 0 for every area
            // and type; they mean "none" outside data blocks and BOOL values.
            if (Area != null && Area != S7MemoryArea.DB && Area != S7MemoryArea.DBI && DbNumber == 0)
            {
                DbNumber = null;
            }

            if (Type != null && Type != S7DataType.BOOL && BitOffset == 0)
            {
                BitOffset = null;
            }
        }

        /// <summary>The polling interval, with the driver default when none is given.</summary>
        [JsonIgnore]
        public long EffectivePollingTime => PollingTime.HasValue && PollingTime.Value > 0 ? PollingTime.Value : 1000;

        /// <summary>True when <c>s7v:scale</c> or <c>s7v:offset</c> changes the value.</summary>
        [JsonIgnore]
        public bool HasValueConversion => (Scale.HasValue && Scale.Value != 1.0) || (Offset.HasValue && Offset.Value != 0.0);

        /// <summary>
        /// Resolves the address of the form: the explicit terms, which take precedence,
        /// and the address in <c>href</c>. Returns null on success, otherwise the reason
        /// the form must be rejected.
        /// </summary>
        public string TryResolveAddress(out S7Address address)
        {
            address = null;

            S7Address fromHref = null;
            string hrefAddress = S7Address.FromHref(Href);
            if (hrefAddress != null)
            {
                string problem = S7Address.TryParse(hrefAddress, out fromHref);
                if (problem != null)
                {
                    return problem;
                }
            }

            S7Address explicitAddress = null;
            if (Area != null)
            {
                if (!ByteOffset.HasValue)
                {
                    return "s7v:byteOffset is required when s7v:area is present.";
                }

                explicitAddress = new S7Address
                {
                    Area = Area.Value,
                    DbNumber = DbNumber,
                    ByteOffset = ByteOffset.Value,
                    BitOffset = BitOffset,
                    Type = Type,
                    Length = Length
                };
            }
            else if (DbNumber.HasValue || ByteOffset.HasValue || BitOffset.HasValue)
            {
                return "s7v:dbNumber, s7v:byteOffset and s7v:bitOffset require s7v:area.";
            }

            if (explicitAddress == null && fromHref == null)
            {
                return "A form needs an address in href or the explicit address terms.";
            }

            if (Type != null && fromHref?.Type != null && Type != fromHref.Type)
            {
                return $"s7v:type {S7DataTypes.ToTerm(Type.Value)} differs from the address type {fromHref.Type}.";
            }

            if (explicitAddress != null && fromHref != null && !explicitAddress.SameLocation(fromHref))
            {
                return $"The explicit address terms and the href address {fromHref} describe different locations.";
            }

            address = explicitAddress ?? fromHref;
            address.Type = Type ?? fromHref?.Type;
            address.Length = Length ?? fromHref?.Length;
            if (address.BitOffset == null && fromHref?.BitOffset != null)
            {
                address.BitOffset = fromHref.BitOffset;
            }

            return null;
        }

        /// <summary>
        /// Checks the form against every normative rule of the binding, for the declared
        /// controller family (null when the Thing Description does not declare one).
        /// Returns null when the form is valid, otherwise the reason.
        /// </summary>
        public string Validate(S7Controller? controller = null)
        {
            if (string.IsNullOrWhiteSpace(Href))
            {
                return "href is required.";
            }

            if (Op == null || Op.Length == 0)
            {
                return "op is required.";
            }

            foreach (Op op in Op)
            {
                if (op != Models.Op.Readproperty && op != Models.Op.Writeproperty && op != Models.Op.Observeproperty)
                {
                    return $"The operation {op} has no S7Comm mapping.";
                }
            }

            if (Scale.HasValue && Scale.Value == 0.0)
            {
                return "s7v:scale must not be zero.";
            }

            if ((PollingTime.HasValue && PollingTime.Value < 0) || (Timeout.HasValue && Timeout.Value < 0))
            {
                return "s7v:pollingTime and s7v:timeout must not be negative.";
            }

            if (Length.HasValue && Length.Value < 1)
            {
                return "s7v:length must be positive.";
            }

            string serviceProblem = ValidateService();
            if (serviceProblem != null)
            {
                return serviceProblem;
            }

            string addressProblem = TryResolveAddress(out S7Address address);
            if (addressProblem != null)
            {
                return addressProblem;
            }

            string problem = address.Validate();
            if (problem != null)
            {
                return problem;
            }

            if (HasValueConversion && !S7DataTypes.IsNumeric(address.Type.Value))
            {
                return $"s7v:scale and s7v:offset do not apply to {address.Type}.";
            }

            return controller.HasValue ? S7Controllers.Supports(controller.Value, address) : null;
        }

        private string ValidateService()
        {
            if (Service == null)
            {
                return null;
            }

            if (Service != S7Services.ReadVariable && Service != S7Services.WriteVariable)
            {
                return $"Unknown s7v:service {Service}.";
            }

            foreach (Op op in Op)
            {
                bool isWrite = op == Models.Op.Writeproperty;
                if ((Service == S7Services.ReadVariable && isWrite) || (Service == S7Services.WriteVariable && !isWrite))
                {
                    return $"s7v:service {Service} does not perform the operation {op}.";
                }
            }

            return null;
        }

        /// <summary>Applies <c>s7v:scale</c> and <c>s7v:offset</c> to a value read from the controller.</summary>
        public object ToApplicationValue(object raw)
        {
            if (raw == null || !HasValueConversion)
            {
                return raw;
            }

            return Convert.ToDouble(raw, CultureInfo.InvariantCulture) * (Scale ?? 1.0) + (Offset ?? 0.0);
        }

        /// <summary>
        /// Applies the inverse conversion to a value written by the application,
        /// rounding to the nearest representable value of the declared type.
        /// </summary>
        public object ToControllerValue(object value, S7DataType type)
        {
            if (value == null || !HasValueConversion)
            {
                return value;
            }

            double raw = (Convert.ToDouble(value, CultureInfo.InvariantCulture) - (Offset ?? 0.0)) / (Scale ?? 1.0);
            if (type == S7DataType.REAL || type == S7DataType.LREAL)
            {
                return raw;
            }

            raw = Math.Round(raw, MidpointRounding.AwayFromZero);
            S7DataTypes.Bounds(type, out long? minimum, out long? maximum);
            double min = minimum.HasValue ? minimum.Value : (type == S7DataType.LINT ? long.MinValue : 0.0);
            double max = maximum.HasValue ? maximum.Value : (type == S7DataType.LINT ? long.MaxValue : (type == S7DataType.DWORD || type == S7DataType.UDINT ? (double)uint.MaxValue : ulong.MaxValue));

            // long/ulong bounds round up as doubles, so stay just inside them.
            if (raw >= max)
            {
                return max >= long.MaxValue ? BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(max) - 1) : max;
            }

            return raw <= min ? min : raw;
        }
    }

    /// <summary>
    /// A variable address of the binding:
    /// <c>%I0.3:BOOL</c>, <c>%M100:DINT</c>, <c>%DB1.DBX0.3:BOOL</c>, <c>%DB5:10:INT</c> or
    /// <c>%DB1.DBB0:STRING(80)</c>.
    /// </summary>
    public sealed class S7Address
    {
        public S7MemoryArea Area { get; set; }

        public int? DbNumber { get; set; }

        public int ByteOffset { get; set; }

        public int? BitOffset { get; set; }

        public S7DataType? Type { get; set; }

        public int? Length { get; set; }

        /// <summary>True for the data block areas, which carry a DB number.</summary>
        public bool IsDataBlock => Area == S7MemoryArea.DB || Area == S7MemoryArea.DBI;

        /// <summary>The bit offset of a BOOL value; 0 otherwise.</summary>
        public int EffectiveBitOffset => BitOffset ?? 0;

        /// <summary>The declared maximum length of a string.</summary>
        public int EffectiveLength => Length.HasValue && Length.Value > 0 ? Length.Value : S7Form.DefaultStringLength;

        /// <summary>True when both addresses name the same memory location.</summary>
        public bool SameLocation(S7Address other)
        {
            return other != null
                && Area == other.Area
                && (!IsDataBlock || DbNumber == other.DbNumber)
                && ByteOffset == other.ByteOffset
                && (Type != S7DataType.BOOL && other.Type != S7DataType.BOOL || EffectiveBitOffset == other.EffectiveBitOffset);
        }

        /// <summary>Checks the address; returns null when valid, otherwise the reason.</summary>
        public string Validate()
        {
            if (Type == null)
            {
                return "s7v:type is required.";
            }

            if (IsDataBlock && !(DbNumber.HasValue && DbNumber.Value >= 1))
            {
                return "s7v:dbNumber must be a positive integer for the DB and DBI areas.";
            }

            if (ByteOffset < 0)
            {
                return "s7v:byteOffset must not be negative.";
            }

            if (Type == S7DataType.BOOL && !BitOffset.HasValue)
            {
                return "A BOOL value needs a bit offset.";
            }

            if (BitOffset.HasValue && (BitOffset.Value < 0 || BitOffset.Value > 7))
            {
                return "s7v:bitOffset must be between 0 and 7.";
            }

            if (Length.HasValue && Length.Value < 1)
            {
                return "s7v:length must be positive.";
            }

            if (Type == S7DataType.STRING && EffectiveLength > 254)
            {
                return "A STRING holds at most 254 characters.";
            }

            if ((Area == S7MemoryArea.C || Area == S7MemoryArea.T) && Type != S7DataType.WORD && Type != S7DataType.INT && Type != S7DataType.S5TIME)
            {
                return $"Counter and timer values are 16-bit; {Type} cannot be read from the {Area} area.";
            }

            return null;
        }

        /// <summary>
        /// The percent-decoded address carried by a form href, relative
        /// (<c>./%25DB1.DBW20:INT</c>) or absolute (<c>s7://host/0,1/%25DB1.DBW20:INT</c>);
        /// null when the href names no address.
        /// </summary>
        public static string FromHref(string href)
        {
            string text = href?.Trim() ?? string.Empty;
            int query = text.IndexOfAny(new[] { '?', '#' });
            if (query >= 0)
            {
                text = text.Substring(0, query);
            }

            if (text.StartsWith(S7Endpoint.Scheme + "://", StringComparison.OrdinalIgnoreCase))
            {
                int slash = text.LastIndexOf('/');
                text = slash > S7Endpoint.Scheme.Length + 2 ? text.Substring(slash + 1) : string.Empty;
            }
            else if (text.StartsWith("./", StringComparison.Ordinal))
            {
                text = text.Substring(2);
            }

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(text);
            }
            catch (UriFormatException)
            {
                return null;
            }

            return decoded.StartsWith("%", StringComparison.Ordinal) ? decoded : null;
        }

        /// <summary>
        /// The href the binding requires for an address: reserved characters
        /// percent-encoded and a leading <c>./</c>, since every address contains a colon.
        /// </summary>
        public static string ToHref(string address)
        {
            var builder = new StringBuilder("./");
            foreach (char c in address ?? string.Empty)
            {
                switch (c)
                {
                    case '%': builder.Append("%25"); break;
                    case '[': builder.Append("%5B"); break;
                    case ']': builder.Append("%5D"); break;
                    case '?': builder.Append("%3F"); break;
                    case '#': builder.Append("%23"); break;
                    case '/': builder.Append("%2F"); break;
                    case ' ': builder.Append("%20"); break;
                    default: builder.Append(c); break;
                }
            }

            return builder.ToString();
        }

        /// <summary>Parses an address; returns null on success, otherwise the reason.</summary>
        public static string TryParse(string text, out S7Address address)
        {
            address = null;
            string value = text?.Trim() ?? string.Empty;
            int typeSeparator = value.LastIndexOf(':');
            if (!value.StartsWith("%", StringComparison.Ordinal) || typeSeparator < 0)
            {
                return $"'{text}' is not an S7 address (%<area><offset>:<type>).";
            }

            string typeText = value.Substring(typeSeparator + 1).Trim();
            int? length = null;
            int paren = typeText.IndexOf('(');
            if (paren >= 0)
            {
                if (!typeText.EndsWith(")", StringComparison.Ordinal)
                    || !int.TryParse(typeText.Substring(paren + 1, typeText.Length - paren - 2), NumberStyles.None, CultureInfo.InvariantCulture, out int declared))
                {
                    return $"'{text}' has an unreadable length.";
                }

                length = declared;
                typeText = typeText.Substring(0, paren);
            }

            if (!S7DataTypes.TryParse(typeText, out S7DataType type))
            {
                return $"'{text}' names the unsupported type {typeText}.";
            }

            string location = value.Substring(1, typeSeparator - 1).ToUpperInvariant();
            var result = new S7Address { Type = type, Length = length };
            int position = 0;

            if (location.StartsWith("DB", StringComparison.Ordinal) && location.Length > 2
                && (char.IsDigit(location[2]) || (location[2] == 'I' && location.Length > 3 && char.IsDigit(location[3]))))
            {
                result.Area = location[2] == 'I' ? S7MemoryArea.DBI : S7MemoryArea.DB;
                position = result.Area == S7MemoryArea.DBI ? 3 : 2;
                result.DbNumber = ReadNumber(location, ref position);

                if (position < location.Length && location[position] == ':')
                {
                    // Short notation: %DB5:10:INT
                    position++;
                }
                else if (position < location.Length && location[position] == '.'
                    && (string.CompareOrdinal(location, position + 1, "DB", 0, 2) == 0 || string.CompareOrdinal(location, position + 1, "DI", 0, 2) == 0))
                {
                    // Long notation: %DB1.DBW20:INT
                    position += 3;
                    if (position < location.Length && IsShortCode(location[position]))
                    {
                        position++;
                    }
                }
                else
                {
                    return $"'{text}' is not a data block address (%DB<n>.DB<code><offset> or %DB<n>:<offset>).";
                }
            }
            else
            {
                if (location.StartsWith("LD", StringComparison.Ordinal))
                {
                    result.Area = S7MemoryArea.LD;
                    position = 2;
                }
                else if (location.Length > 0 && S7MemoryAreas.TryParseLetter(location[0], out S7MemoryArea area))
                {
                    result.Area = area;
                    position = 1;
                }
                else
                {
                    return $"'{text}' names an unsupported memory area.";
                }

                if (position < location.Length && IsShortCode(location[position])
                    && position + 1 < location.Length && char.IsDigit(location[position + 1]))
                {
                    position++;
                }
            }

            int? byteOffset = ReadNumber(location, ref position);
            if (!byteOffset.HasValue)
            {
                return $"'{text}' has no byte offset.";
            }

            result.ByteOffset = byteOffset.Value;
            if (position < location.Length && location[position] == '.')
            {
                position++;
                result.BitOffset = ReadNumber(location, ref position);
                if (!result.BitOffset.HasValue)
                {
                    return $"'{text}' has an unreadable bit offset.";
                }
            }

            if (position != location.Length)
            {
                return $"'{text}' is not an S7 address.";
            }

            if (result.BitOffset.HasValue && type != S7DataType.BOOL)
            {
                return $"'{text}' has a bit offset, which only a BOOL value takes.";
            }

            address = result;
            return null;
        }

        private static bool IsShortCode(char c) => c == 'X' || c == 'B' || c == 'W' || c == 'D';

        private static int? ReadNumber(string text, ref int position)
        {
            int start = position;
            while (position < text.Length && char.IsDigit(text[position]))
            {
                position++;
            }

            if (position == start || !int.TryParse(text.Substring(start, position - start), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                return null;
            }

            return number;
        }

        /// <summary>
        /// The address in the notation of the Siemens engineering tools:
        /// <c>%DB1.DBW20:INT</c> for data blocks, <c>%I0.3:BOOL</c> or <c>%M100:DINT</c> otherwise.
        /// </summary>
        public override string ToString()
        {
            string type = Type.HasValue ? Type.Value.ToString() : string.Empty;
            if ((Type == S7DataType.STRING || Type == S7DataType.WSTRING) && Length.HasValue)
            {
                type += "(" + Length.Value.ToString(CultureInfo.InvariantCulture) + ")";
            }

            string bit = Type == S7DataType.BOOL ? "." + EffectiveBitOffset.ToString(CultureInfo.InvariantCulture) : string.Empty;
            string offset = ByteOffset.ToString(CultureInfo.InvariantCulture) + bit;

            if (IsDataBlock)
            {
                string code = Type.HasValue ? S7DataTypes.ShortCode(Type.Value).ToString() : "B";
                return "%" + Area + (DbNumber ?? 0).ToString(CultureInfo.InvariantCulture) + ".DB" + code + offset + ":" + type;
            }

            return "%" + Area + offset + ":" + type;
        }
    }

    /// <summary>S7 data types of the binding (<c>s7v:BOOL</c> ... <c>s7v:S5TIME</c>).</summary>
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum S7DataType
    {
        [EnumMember(Value = "s7v:BOOL")] BOOL,
        [EnumMember(Value = "s7v:BYTE")] BYTE,
        [EnumMember(Value = "s7v:WORD")] WORD,
        [EnumMember(Value = "s7v:DWORD")] DWORD,
        [EnumMember(Value = "s7v:LWORD")] LWORD,
        [EnumMember(Value = "s7v:SINT")] SINT,
        [EnumMember(Value = "s7v:USINT")] USINT,
        [EnumMember(Value = "s7v:INT")] INT,
        [EnumMember(Value = "s7v:UINT")] UINT,
        [EnumMember(Value = "s7v:DINT")] DINT,
        [EnumMember(Value = "s7v:UDINT")] UDINT,
        [EnumMember(Value = "s7v:LINT")] LINT,
        [EnumMember(Value = "s7v:ULINT")] ULINT,
        [EnumMember(Value = "s7v:REAL")] REAL,
        [EnumMember(Value = "s7v:LREAL")] LREAL,
        [EnumMember(Value = "s7v:CHAR")] CHAR,
        [EnumMember(Value = "s7v:WCHAR")] WCHAR,
        [EnumMember(Value = "s7v:STRING")] STRING,
        [EnumMember(Value = "s7v:WSTRING")] WSTRING,
        [EnumMember(Value = "s7v:DATE")] DATE,
        [EnumMember(Value = "s7v:TIME")] TIME,
        [EnumMember(Value = "s7v:LTIME")] LTIME,
        [EnumMember(Value = "s7v:TOD")] TOD,
        [EnumMember(Value = "s7v:LTOD")] LTOD,
        [EnumMember(Value = "s7v:DT")] DT,
        [EnumMember(Value = "s7v:LDT")] LDT,
        [EnumMember(Value = "s7v:DTL")] DTL,
        [EnumMember(Value = "s7v:S5TIME")] S5TIME
    }

    /// <summary>Parses, sizes and classifies <see cref="S7DataType"/> values.</summary>
    public static class S7DataTypes
    {
        /// <summary>The binding term for <paramref name="type"/>, such as <c>s7v:INT</c>.</summary>
        public static string ToTerm(S7DataType type) => S7Form.Prefix + ":" + type;

        /// <summary>
        /// Parses a type written as <c>s7v:INT</c> or bare, including the long Siemens
        /// names <c>TIME_OF_DAY</c>, <c>LTIME_OF_DAY</c> and <c>DATE_AND_TIME</c>.
        /// </summary>
        public static bool TryParse(string text, out S7DataType type)
        {
            type = default(S7DataType);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int colon = text.IndexOf(':');
            string name = (colon >= 0 ? text.Substring(colon + 1) : text).Trim().ToUpperInvariant();
            switch (name)
            {
                case "TIME_OF_DAY": name = "TOD"; break;
                case "LTIME_OF_DAY": name = "LTOD"; break;
                case "DATE_AND_TIME": name = "DT"; break;
            }

            foreach (S7DataType value in Enum.GetValues(typeof(S7DataType)))
            {
                if (value.ToString() == name)
                {
                    type = value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Maps the coarse XSD <c>type</c> of legacy forms onto an S7 type.</summary>
        public static bool TryParseLegacyXsdType(string text, out S7DataType type)
        {
            type = default(S7DataType);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int colon = text.IndexOf(':');
            switch ((colon >= 0 ? text.Substring(colon + 1) : text).Trim().ToLowerInvariant())
            {
                case "boolean": type = S7DataType.BOOL; return true;
                case "byte": type = S7DataType.BYTE; return true;
                case "short": type = S7DataType.INT; return true;
                case "unsignedshort": type = S7DataType.UINT; return true;
                case "integer": case "int": type = S7DataType.DINT; return true;
                case "long": type = S7DataType.LINT; return true;
                case "unsignedlong": type = S7DataType.ULINT; return true;
                case "float": type = S7DataType.REAL; return true;
                case "double": type = S7DataType.LREAL; return true;
                case "datetime": type = S7DataType.DTL; return true;
                case "duration": type = S7DataType.TIME; return true;
                case "string": type = S7DataType.STRING; return true;
                default: return false;
            }
        }

        /// <summary>The short code of the type inside a data block address (X, B, W or D).</summary>
        public static char ShortCode(S7DataType type)
        {
            switch (type)
            {
                case S7DataType.BYTE: case S7DataType.SINT: case S7DataType.USINT: case S7DataType.CHAR:
                case S7DataType.STRING: case S7DataType.DT: case S7DataType.DTL:
                    return 'B';
                case S7DataType.WORD: case S7DataType.INT: case S7DataType.UINT: case S7DataType.WCHAR:
                case S7DataType.WSTRING: case S7DataType.DATE: case S7DataType.S5TIME:
                    return 'W';
                case S7DataType.DWORD: case S7DataType.DINT: case S7DataType.UDINT: case S7DataType.REAL:
                case S7DataType.TIME: case S7DataType.TOD:
                    return 'D';
                default:
                    return 'X';
            }
        }

        /// <summary>The size in bytes of a value; strings use the declared maximum length.</summary>
        public static int ByteSize(S7DataType type, int length = S7Form.DefaultStringLength)
        {
            switch (type)
            {
                case S7DataType.BOOL: case S7DataType.BYTE: case S7DataType.SINT: case S7DataType.USINT: case S7DataType.CHAR:
                    return 1;
                case S7DataType.WORD: case S7DataType.INT: case S7DataType.UINT: case S7DataType.WCHAR:
                case S7DataType.DATE: case S7DataType.S5TIME:
                    return 2;
                case S7DataType.DWORD: case S7DataType.DINT: case S7DataType.UDINT: case S7DataType.REAL:
                case S7DataType.TIME: case S7DataType.TOD:
                    return 4;
                case S7DataType.DTL:
                    return 12;
                case S7DataType.STRING:
                    return 2 + length;
                case S7DataType.WSTRING:
                    return 4 + (2 * length);
                default:
                    return 8;
            }
        }

        /// <summary>True for the types whose values are numbers, to which value conversion applies.</summary>
        public static bool IsNumeric(S7DataType type)
        {
            switch (type)
            {
                case S7DataType.BYTE: case S7DataType.WORD: case S7DataType.DWORD: case S7DataType.LWORD:
                case S7DataType.SINT: case S7DataType.USINT: case S7DataType.INT: case S7DataType.UINT:
                case S7DataType.DINT: case S7DataType.UDINT: case S7DataType.LINT: case S7DataType.ULINT:
                case S7DataType.REAL: case S7DataType.LREAL:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The WoT data schema type the binding recommends for an S7 type.</summary>
        public static TypeEnum WotTypeOf(S7DataType type)
        {
            if (type == S7DataType.BOOL)
            {
                return TypeEnum.Boolean;
            }

            if (type == S7DataType.REAL || type == S7DataType.LREAL)
            {
                return TypeEnum.Number;
            }

            return IsNumeric(type) ? TypeEnum.Integer : TypeEnum.String;
        }

        /// <summary>
        /// The minimum and maximum of the WoT data schema the binding recommends for an
        /// integer type; null where the binding gives none.
        /// </summary>
        public static void Bounds(S7DataType type, out long? minimum, out long? maximum)
        {
            minimum = null;
            maximum = null;
            switch (type)
            {
                case S7DataType.BYTE: case S7DataType.USINT: minimum = 0; maximum = byte.MaxValue; break;
                case S7DataType.WORD: case S7DataType.UINT: minimum = 0; maximum = ushort.MaxValue; break;
                case S7DataType.SINT: minimum = sbyte.MinValue; maximum = sbyte.MaxValue; break;
                case S7DataType.INT: minimum = short.MinValue; maximum = short.MaxValue; break;
                case S7DataType.DINT: minimum = int.MinValue; maximum = int.MaxValue; break;
                case S7DataType.DWORD: case S7DataType.UDINT: case S7DataType.LWORD: case S7DataType.ULINT: minimum = 0; break;
            }
        }
    }

    /// <summary>The memory areas of the binding (<c>s7v:area</c>).</summary>
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum S7MemoryArea
    {
        /// <summary>Process image of the inputs (%I, %E).</summary>
        [EnumMember(Value = "I")] I,

        /// <summary>Process image of the outputs (%Q, %A).</summary>
        [EnumMember(Value = "Q")] Q,

        /// <summary>Flags or markers (%M).</summary>
        [EnumMember(Value = "M")] M,

        /// <summary>Data blocks (%DBn).</summary>
        [EnumMember(Value = "DB")] DB,

        /// <summary>Instance data blocks (%DBIn).</summary>
        [EnumMember(Value = "DBI")] DBI,

        /// <summary>Counters (%C, %Z).</summary>
        [EnumMember(Value = "C")] C,

        /// <summary>Timers (%T).</summary>
        [EnumMember(Value = "T")] T,

        /// <summary>Direct peripheral access (%D).</summary>
        [EnumMember(Value = "D")] D,

        /// <summary>Local data (%LD).</summary>
        [EnumMember(Value = "LD")] LD
    }

    /// <summary>Parses the address letters of the memory areas.</summary>
    public static class S7MemoryAreas
    {
        /// <summary>Parses an area letter, including the German I/O, output and counter letters.</summary>
        public static bool TryParseLetter(char letter, out S7MemoryArea area)
        {
            switch (char.ToUpperInvariant(letter))
            {
                case 'I': case 'E': area = S7MemoryArea.I; return true;
                case 'Q': case 'A': area = S7MemoryArea.Q; return true;
                case 'M': area = S7MemoryArea.M; return true;
                case 'C': case 'Z': area = S7MemoryArea.C; return true;
                case 'T': area = S7MemoryArea.T; return true;
                case 'D': area = S7MemoryArea.D; return true;
                default: area = default(S7MemoryArea); return false;
            }
        }

        /// <summary>Maps the legacy <c>s7:target</c> values (DB, MB, EB, AB, TM, CT).</summary>
        public static bool TryParseLegacyTarget(string target, out S7MemoryArea area)
        {
            switch ((target ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "DB": area = S7MemoryArea.DB; return true;
                case "MB": area = S7MemoryArea.M; return true;
                case "EB": area = S7MemoryArea.I; return true;
                case "AB": area = S7MemoryArea.Q; return true;
                case "TM": area = S7MemoryArea.T; return true;
                case "CT": area = S7MemoryArea.C; return true;
                default: area = default(S7MemoryArea); return false;
            }
        }
    }

    /// <summary>The S7Comm function names of <c>s7v:service</c>.</summary>
    public static class S7Services
    {
        public const string ReadVariable = "ReadVariable";

        public const string WriteVariable = "WriteVariable";
    }

    /// <summary>The controller families of the binding (<c>s7v:controller</c>).</summary>
    public enum S7Controller
    {
        ANY,
        S7_200,
        S7_200_SMART,
        S7_300,
        S7_400,
        S7_1200,
        S7_1500,
        LOGO
    }

    /// <summary>Reads the Thing-level terms and checks what a controller family supports.</summary>
    public static class S7Controllers
    {
        public const string ControllerTerm = S7Form.Prefix + ":controller";

        public const string LocalTsapTerm = S7Form.Prefix + ":localTsap";

        public const string RemoteTsapTerm = S7Form.Prefix + ":remoteTsap";

        public const string PduSizeTerm = S7Form.Prefix + ":pduSize";

        /// <summary>True for the families that are addressed with TSAPs instead of rack and slot.</summary>
        public static bool NeedsTsaps(S7Controller controller) =>
            controller == S7Controller.S7_200 || controller == S7Controller.S7_200_SMART || controller == S7Controller.LOGO;

        /// <summary>Parses a family name exactly as the binding writes it.</summary>
        public static bool TryParse(string text, out S7Controller controller)
        {
            controller = S7Controller.ANY;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (S7Controller value in Enum.GetValues(typeof(S7Controller)))
            {
                if (string.Equals(value.ToString(), text.Trim(), StringComparison.Ordinal))
                {
                    controller = value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The family of a CPU from its Siemens order number: 6ES7 5.. is an S7-1500,
        /// 6ES7 21. an S7-1200, 6ES7 2.. an S7-200 SMART, 6ES7 3.. an S7-300 and
        /// 6ES7 4.. an S7-400. Null when the order number names none of them.
        /// </summary>
        public static S7Controller? FromOrderNumber(string orderNumber)
        {
            string code = (orderNumber ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
            if (!code.StartsWith("6ES7", StringComparison.Ordinal) || code.Length < 6)
            {
                return null;
            }

            switch (code[4])
            {
                case '5': return S7Controller.S7_1500;
                case '2': return code[5] == '1' ? S7Controller.S7_1200 : S7Controller.S7_200_SMART;
                case '3': return S7Controller.S7_300;
                case '4': return S7Controller.S7_400;
                default: return null;
            }
        }

        /// <summary>
        /// Checks that the controller family supports the area and type of an address;
        /// returns null when it does, otherwise the reason.
        /// </summary>
        public static string Supports(S7Controller controller, S7Address address)
        {
            if (controller == S7Controller.ANY || address?.Type == null)
            {
                return null;
            }

            bool modern = controller == S7Controller.S7_1200 || controller == S7Controller.S7_1500;
            if (modern && (address.Area == S7MemoryArea.C || address.Area == S7MemoryArea.T))
            {
                return $"The {controller} family has no {address.Area} area.";
            }

            switch (address.Type.Value)
            {
                case S7DataType.LWORD: case S7DataType.LINT: case S7DataType.ULINT:
                case S7DataType.LTIME: case S7DataType.LTOD: case S7DataType.LDT:
                    return controller == S7Controller.S7_1500 ? null : $"{address.Type} is supported on S7_1500 only.";
                case S7DataType.SINT: case S7DataType.USINT: case S7DataType.UINT: case S7DataType.UDINT:
                case S7DataType.LREAL: case S7DataType.WCHAR: case S7DataType.WSTRING: case S7DataType.DTL:
                    return modern ? null : $"{address.Type} is supported on S7_1200 and S7_1500 only.";
                case S7DataType.S5TIME:
                    // The binding lists S5TIME for the S7-300 and S7-400 families; the S7-1500
                    // keeps it for compatibility with migrated programs, so it is accepted there too.
                    return controller == S7Controller.S7_300 || controller == S7Controller.S7_400 || controller == S7Controller.S7_1500 ? null : "S5TIME is supported on S7_300, S7_400 and S7_1500 only.";
                default:
                    return null;
            }
        }
    }

    /// <summary>The connection terms of a Thing Description (<c>s7v:controller</c>, TSAPs, PDU size).</summary>
    public sealed class S7ConnectionTerms
    {
        /// <summary>The declared family; null when the Thing Description declares none.</summary>
        public S7Controller? Controller { get; set; }

        public ushort? LocalTsap { get; set; }

        public ushort? RemoteTsap { get; set; }

        public int? PduSize { get; set; }

        /// <summary>True when a non-zero TSAP replaces the rack and slot of the endpoint.</summary>
        public bool UsesTsaps => (LocalTsap ?? 0) != 0 || (RemoteTsap ?? 0) != 0;

        /// <summary>
        /// Reads the Thing-level terms and checks them: a known family, TSAPs where the
        /// family needs them (never defaulted) and a PDU size of at least 128 bytes.
        /// </summary>
        public static S7ConnectionTerms FromThingDescription(ThingDescription td)
        {
            var terms = new S7ConnectionTerms();
            IDictionary<string, object> data = td?.AdditionalData;
            if (data == null)
            {
                return terms;
            }

            if (data.TryGetValue(S7Controllers.ControllerTerm, out object controller) && controller != null)
            {
                if (!S7Controllers.TryParse(controller.ToString(), out S7Controller family))
                {
                    throw new ArgumentException($"Unknown {S7Controllers.ControllerTerm} '{controller}'.");
                }

                terms.Controller = family;
            }

            terms.LocalTsap = ReadUShort(data, S7Controllers.LocalTsapTerm);
            terms.RemoteTsap = ReadUShort(data, S7Controllers.RemoteTsapTerm);

            ushort? pdu = ReadUShort(data, S7Controllers.PduSizeTerm);
            if (pdu.HasValue && pdu.Value < 128)
            {
                throw new ArgumentException($"{S7Controllers.PduSizeTerm} must be at least 128.");
            }

            terms.PduSize = pdu;

            if (terms.Controller.HasValue && S7Controllers.NeedsTsaps(terms.Controller.Value)
                && (!terms.LocalTsap.HasValue || !terms.RemoteTsap.HasValue))
            {
                throw new ArgumentException(
                    $"The {terms.Controller} family needs {S7Controllers.LocalTsapTerm} and {S7Controllers.RemoteTsapTerm} from the device configuration.");
            }

            return terms;
        }

        private static ushort? ReadUShort(IDictionary<string, object> data, string term)
        {
            if (!data.TryGetValue(term, out object value) || value == null)
            {
                return null;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
            bool parsed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ushort.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort number)
                : ushort.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);

            if (!parsed)
            {
                throw new ArgumentException($"{term} must be an unsigned 16-bit number, not '{text}'.");
            }

            return number;
        }
    }

    /// <summary>
    /// An S7 endpoint as the binding writes it: <c>s7://host[:port][/rack,slot][/]</c>,
    /// with port 102 and rack 0, slot 1 when omitted.
    /// </summary>
    public sealed class S7Endpoint
    {
        public const string Scheme = "s7";

        public const int DefaultPort = 102;

        public const int DefaultRack = 0;

        public const int DefaultSlot = 1;

        public S7Endpoint(string host, int port = DefaultPort, int rack = DefaultRack, int slot = DefaultSlot)
        {
            Host = host;
            Port = port;
            Rack = rack;
            Slot = slot;
        }

        public string Host { get; }

        public int Port { get; }

        public int Rack { get; }

        public int Slot { get; }

        /// <summary>The base URI of the binding, ending in <c>/</c> so relative addresses resolve below it.</summary>
        public string ToBaseUri()
        {
            string host = Host.IndexOf(':') >= 0 && !Host.StartsWith("[", StringComparison.Ordinal) ? "[" + Host + "]" : Host;
            string port = Port == DefaultPort ? string.Empty : ":" + Port.ToString(CultureInfo.InvariantCulture);
            return $"{Scheme}://{host}{port}/{Rack.ToString(CultureInfo.InvariantCulture)},{Slot.ToString(CultureInfo.InvariantCulture)}/";
        }

        /// <summary>Parses an <c>s7</c> URI, such as a Thing Description <c>base</c>.</summary>
        public static bool TryParse(string uri, out S7Endpoint endpoint)
        {
            endpoint = null;
            if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri.Trim(), UriKind.Absolute, out Uri parsed)
                || !string.Equals(parsed.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(parsed.Host))
            {
                return false;
            }

            int port = parsed.Port > 0 ? parsed.Port : DefaultPort;
            int rack = DefaultRack;
            int slot = DefaultSlot;

            // The first path segment is the rack and slot; anything after it is an address.
            string path = parsed.AbsolutePath.Trim('/');
            int slash = path.IndexOf('/');
            string first = Uri.UnescapeDataString(slash >= 0 ? path.Substring(0, slash) : path);
            if (first.Length > 0 && !first.StartsWith("%", StringComparison.Ordinal))
            {
                string[] parts = first.Split(',');
                if (parts.Length != 2
                    || !int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out rack)
                    || !int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out slot))
                {
                    return false;
                }
            }

            endpoint = new S7Endpoint(parsed.Host.Trim('[', ']'), port, rack, slot);
            return true;
        }
    }
}
