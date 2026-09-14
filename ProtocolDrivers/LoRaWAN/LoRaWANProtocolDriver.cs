namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;

    public class LoRaWANProtocolDriver: IProtocolDriver
    {
        public string Scheme => "lorawan";

        /// <summary>
        /// The official W3C WoT LoRaWAN binding namespace, as published in
        /// w3c/wot-binding-templates. Thing Descriptions declare this in their
        /// @context under the conventional "lorav" prefix.
        /// </summary>
        public string WoTBindingUri => LoRaWANForm.BindingNamespace;

        private readonly LoRaWANNetworkServerAsset _lorawanNetworkServer = new();

        /// <summary>
        /// The network server asset this driver registers field rules against.
        /// </summary>
        public LoRaWANNetworkServerAsset NetworkServer => _lorawanNetworkServer;

        public IEnumerable<string> Discover()
        {
            // LoRaWAN does not support discovery
            return new List<string>();
        }

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            return new ThingDescription()
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1"],
                Id = "urn:" + assetName,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Base = assetEndpoint,
                Title = assetName,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };
        }

        public async Task<AssetConnection> CreateAndConnectAssetAsync(ThingDescription td, CancellationToken cancellationToken = default)
        {
            const byte unitId = 1;

            // The binding forbids OTAA root keys anywhere in the URI, so reject
            // a Thing Description that carries one rather than quietly accepting
            // a leaked secret.
            RejectEmbeddedSecrets(td);

            LoRaWANUri uri = LoRaWANUri.Parse(td.Base);

            // The device identity is a thing-level term; the URI restates it.
            // Prefer the term and fall back to the URI so either is sufficient.
            string devEui = ReadThingLevelDevEui(td) ?? uri.DevEUI;

            await _lorawanNetworkServer.ConnectAsync(
                uri.IsRouterConfig ? td.Base : BuildRegistrationAddress(uri, devEui),
                0,
                cancellationToken).ConfigureAwait(false);

            return new AssetConnection(_lorawanNetworkServer, unitId);
        }

        /// <summary>
        /// Builds the address the network server asset registers the device
        /// with. The OTAA key is deliberately absent: it is injected at runtime
        /// from the environment, never from the Thing Description.
        /// </summary>
        private static string BuildRegistrationAddress(LoRaWANUri uri, string devEui)
        {
            return string.Create(CultureInfo.InvariantCulture, $"lorawan://{uri.Authority}/{devEui}/{uri.Target}");
        }

        /// <summary>
        /// Reads <c>lorav:devEUI</c> from the Thing Description root.
        /// </summary>
        private static string ReadThingLevelDevEui(ThingDescription td)
        {
            try
            {
                // The thing-level terms are not part of the strongly typed
                // ThingDescription, so read them from the raw document.
                if (td.AdditionalData is not null
                    && td.AdditionalData.TryGetValue("lorav:devEUI", out object value))
                {
                    string devEui = value?.ToString();

                    return string.IsNullOrWhiteSpace(devEui) ? null : devEui;
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to read lorav:devEUI from the Thing Description.");
            }

            return null;
        }

        /// <summary>
        /// Enforces the binding's rule that "OTAA root keys are secrets and MUST
        /// NOT be embedded in base, href, or URI query strings".
        /// </summary>
        private static void RejectEmbeddedSecrets(ThingDescription td)
        {
            foreach (string segment in (td.Base ?? string.Empty).Split(['/', ':', '?', '&', '=']))
            {
                if (LoRaWANUri.LooksLikeSecret(segment))
                {
                    throw new NotSupportedException(
                        "The Thing Description embeds what looks like a LoRaWAN OTAA key in its base URI. The W3C WoT LoRaWAN binding forbids this: declare the key as an apikey security scheme and inject its value at runtime (see the LORAWAN_APPKEY_<devEUI> environment variable).");
                }
            }
        }

        public AssetTag CreateTag(
            ThingDescription td,
            object form,
            string assetId,
            byte unitId,
            string variableId,
            string mappedUAExpandedNodeId,
            string mappedUAFieldPath)
        {
            AssetTag tag;
            if (td.Base.ToLower().EndsWith("routerconfig"))
            {
                tag = new()
                {
                    Name = variableId,
                    Address = td.Base.ToLower(),
                    UnitID = unitId,
                    Type = TypeString.String.ToString(),
                    MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                    MappedUAFieldPath = mappedUAFieldPath
                };
            }
            else
            {
                LoRaWANForm lorawanForm = JsonConvert.DeserializeObject<LoRaWANForm>(form.ToString());

                // Fail loudly on binding terms this driver cannot honour.
                // Silently ignoring, say, lorav:presentWhen would decode a field
                // that is not actually present in the payload and report a
                // plausible-looking wrong value.
                RejectUnsupportedTerms(form, variableId);

                // The official binding locates a field with lorav:byteOffset /
                // lorav:byteLength. The asset's address scheme already encodes
                // exactly that as "<devEUI>[/<channel>]/<offset>?quantity=<len>",
                // so the terms are folded into the href rather than appended as
                // extra query parameters: LoRaWANNetworkServerAsset splits the
                // address on [?&=/] and dispatches on the resulting part COUNT,
                // so any extra parameter would stop the tag being read at all.
                string address = BuildAddress(lorawanForm, td);

                tag = new()
                {
                    Name = variableId,
                    Address = address,
                    UnitID = unitId,
                    Type = lorawanForm.Type.ToString(),

                    // Byte order comes from lorav:endian. The binding has no
                    // per-word swap concept, so SwapPerWord stays at its
                    // default: a LoRaWAN payload is a flat byte layout, not a
                    // register file like Modbus.
                    IsBigEndian = lorawanForm.IsBigEndian(),

                    // multiplier and divisor are combined: the binding allows a
                    // divisor of 100 or a multiplier of 0.01 to mean the same.
                    Multiplier = lorawanForm.EffectiveMultiplier() ?? 1.0f,
                    BitMask = lorawanForm.BitMask,
                    MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                    MappedUAFieldPath = mappedUAFieldPath
                };

                // lorav:alias, lorav:presentWhen and lorav:valueMap do not fit
                // the asset's address scheme, so they travel alongside the tag
                // instead of inside its address string.
                RegisterFieldRule(tag, lorawanForm, td);
            }

            return tag;
        }

        /// <summary>
        /// Reads <c>lorav:defaultEventingFrequencyMinutes</c>, the expected
        /// interval between a device's unprompted uplinks.
        /// <para>
        /// This is deliberately <em>not</em> used as the tag polling interval.
        /// Uplink arrival and poll boundary are unsynchronised, so polling at
        /// the same period means a value that lands just after a poll waits
        /// almost a full period to be published - worst-case staleness
        /// approaching twice the uplink interval. Reading is also cheap: the
        /// asset serves the last decoded payload from memory rather than going
        /// to the device, so there is nothing to save by polling slowly.
        /// </para>
        /// <para>
        /// It is exposed for diagnostics and for callers that want to reason
        /// about how stale a reading may legitimately be.
        /// </para>
        /// </summary>
        public static double? ReadEventingFrequencyMinutes(ThingDescription td)
        {
            if ((td?.AdditionalData is null)
                || !td.AdditionalData.TryGetValue("lorav:defaultEventingFrequencyMinutes", out object value)
                || (value is null))
            {
                return null;
            }

            if (!double.TryParse(
                    value.ToString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double minutes)
                || (minutes <= 0))
            {
                return null;
            }

            return minutes;
        }

        /// <summary>
        /// Records a field's conditional-presence, value-mapping, grouping and
        /// derived-value rules, and resolves any <c>lorav:presentWhen</c>
        /// discriminator to a byte offset.
        /// <para>
        /// A gate names its discriminator by event name or <c>lorav:alias</c>,
        /// which only the Thing Description can resolve. Doing it here means the
        /// read path never has to search the Thing Description again.
        /// </para>
        /// </summary>
        private void RegisterFieldRule(AssetTag tag, LoRaWANForm form, ThingDescription td)
        {
            // Every tag is recorded, whether or not it carries rules: any field
            // may be the input to another field's derived value.
            _lorawanNetworkServer.FieldRules.RegisterTag(tag);

            bool hasGrouping = (form.Slot != null) || (form.PadBefore != null);

            if ((form.Alias is null)
                && (form.PresentWhen is null)
                && (form.ValueMap is null)
                && (form.Derived is null)
                && !hasGrouping)
            {
                return;
            }

            int? discriminatorOffset = null;
            int discriminatorLength = 1;

            if (form.PresentWhen?.Field != null)
            {
                (discriminatorOffset, discriminatorLength) = FindAliasedField(td, form.PresentWhen.Field);
            }

            // Only a tagged field needs its group offset carried separately; a
            // byteOffset group already had it folded into the address.
            int tagGroupOffset = form.Tag is { Length: > 0 }
                ? GroupOffset(form, td, IsSameTagGroup)
                : 0;

            _lorawanNetworkServer.FieldRules.Register(
                tag.Name,
                new LoRaWANFieldRule
                {
                    Alias = form.Alias,
                    PresentWhen = form.PresentWhen,
                    ValueMap = form.ValueMap,
                    DiscriminatorOffset = discriminatorOffset,
                    DiscriminatorLength = discriminatorLength,
                    TagGroupOffset = tagGroupOffset,
                    Derived = form.Derived
                });
        }

        /// <summary>
        /// Finds the byte offset and length of the field a condition's
        /// <c>field</c> names.
        /// <para>
        /// <c>lorav:alias</c> is only required "when a condition's
        /// <c>field</c> differs from the event name", so a gate normally names
        /// the event directly. An explicit alias wins over an event name,
        /// because that is the reason an author would declare one.
        /// </para>
        /// </summary>
        private static (int? Offset, int Length) FindAliasedField(ThingDescription td, string field)
        {
            if ((td?.Events is null) || string.IsNullOrEmpty(field))
            {
                return (null, 1);
            }

            // References may be written as "$name"; the sigil is not part of
            // the name being referenced.
            string wanted = field.StartsWith('$') ? field[1..] : field;

            (int? Offset, int Length)? byEventName = null;

            foreach (KeyValuePair<string, TDEvent> tdEvent in td.Events)
            {
                foreach (object formObject in tdEvent.Value?.Forms ?? [])
                {
                    LoRaWANForm candidate;

                    try
                    {
                        candidate = JsonConvert.DeserializeObject<LoRaWANForm>(formObject.ToString());
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if ((candidate is null) || !candidate.ByteOffset.HasValue)
                    {
                        continue;
                    }

                    int length = candidate.ByteLength ?? WireTypeLength(candidate.WireType);

                    if (string.Equals(candidate.Alias, wanted, StringComparison.Ordinal))
                    {
                        return (candidate.ByteOffset, length);
                    }

                    // Remember the event-name match but keep looking: an
                    // explicit alias elsewhere is the more deliberate answer.
                    if ((byEventName is null)
                        && string.Equals(tdEvent.Key, wanted, StringComparison.Ordinal))
                    {
                        byEventName = (candidate.ByteOffset, length);
                    }
                }
            }

            return byEventName ?? (null, 1);
        }

        /// <summary>
        /// Builds the tag address the LoRaWAN asset understands.
        /// <para>
        /// The asset addresses a field as
        /// <c>&lt;devEUI&gt;[/&lt;channel&gt;]/&lt;byteOffset&gt;?quantity=&lt;byteLength&gt;</c>,
        /// which is the same information the binding expresses with
        /// <c>lorav:byteOffset</c> and <c>lorav:byteLength</c>. When a form
        /// supplies those terms they are folded into the href so a
        /// specification-conformant Thing Description works without the author
        /// having to hand-encode the offset into the href.
        /// </para>
        /// </summary>
        private static string BuildAddress(LoRaWANForm form, ThingDescription td)
        {
            string href = ResolveHref(form.Href, td);

            // A tlv/ctv field is found by scanning for its tag bytes, not at a
            // fixed offset, so it takes the asset's tag-matching address form.
            if (form.Tag is { Length: > 0 })
            {
                return BuildTagAddress(form, td, href);
            }

            if (form.ByteOffset is null)
            {
                // The href already carries the offset in its own scheme.
                return href;
            }

            // Keep only the device/channel prefix; drop any offset and query the
            // author may already have written, since the binding terms win.
            string prefix = href.Split('?')[0].TrimEnd('/');

            // A trailing numeric segment is an offset the author baked into the
            // href. Without removing it the binding's offset would be appended
            // to the old one ("/0/7") and the asset would read the wrong field.
            int lastSeparator = prefix.LastIndexOf('/');
            if ((lastSeparator > 0) && int.TryParse(prefix.AsSpan(lastSeparator + 1), out _))
            {
                prefix = prefix.Substring(0, lastSeparator);
            }

            int quantity = form.ByteLength ?? WireTypeLength(form.WireType);

            // Values sharing one byteOffset are positioned within that group by
            // lorav:slot and lorav:padBefore.
            int offset = form.ByteOffset.Value + GroupOffset(form, td, IsSameOffsetGroup);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{prefix}/{offset}?quantity={quantity}");
        }

        /// <summary>
        /// Bytes to skip before this value because of its position in a group.
        /// <para>
        /// Several values may share one locator - the same <c>lorav:tag</c> or
        /// <c>lorav:byteOffset</c> - and are then laid out consecutively in
        /// <c>lorav:slot</c> order. A value therefore starts after every
        /// earlier slot's bytes, plus any <c>lorav:padBefore</c> reserved
        /// immediately before it.
        /// </para>
        /// </summary>
        private static int GroupOffset(
            LoRaWANForm form,
            ThingDescription td,
            Func<LoRaWANForm, LoRaWANForm, bool> sharesGroupWith)
        {
            int padding = form.PadBefore ?? 0;

            // Without a slot this value is not part of an ordered group, so only
            // its own padding applies.
            if ((form.Slot is null) || (td?.Events is null))
            {
                return padding;
            }

            int precedingBytes = 0;

            foreach (KeyValuePair<string, TDEvent> tdEvent in td.Events)
            {
                foreach (object formObject in tdEvent.Value?.Forms ?? [])
                {
                    LoRaWANForm sibling;

                    try
                    {
                        sibling = JsonConvert.DeserializeObject<LoRaWANForm>(formObject.ToString());
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if ((sibling?.Slot is null)
                        || (sibling.Slot >= form.Slot)
                        || !sharesGroupWith(form, sibling))
                    {
                        continue;
                    }

                    // A derived sibling occupies no payload bytes, so it does
                    // not displace the values after it.
                    if (sibling.Derived?.ReplacesWireValue == true)
                    {
                        continue;
                    }

                    precedingBytes += (sibling.PadBefore ?? 0)
                        + (sibling.ByteLength ?? WireTypeLength(sibling.WireType));
                }
            }

            return precedingBytes + padding;
        }

        /// <summary>Two forms share a group when they name the same byte offset.</summary>
        private static bool IsSameOffsetGroup(LoRaWANForm form, LoRaWANForm sibling)
        {
            return sibling.ByteOffset == form.ByteOffset;
        }

        /// <summary>Two forms share a group when they carry the same tag.</summary>
        private static bool IsSameTagGroup(LoRaWANForm form, LoRaWANForm sibling)
        {
            if ((sibling.Tag is null) || (form.Tag is null) || (sibling.Tag.Length != form.Tag.Length))
            {
                return false;
            }

            for (int i = 0; i < form.Tag.Length; i++)
            {
                if (sibling.Tag[i] != form.Tag[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds the tag-matching address for a <c>tlv</c>/<c>ctv</c> field.
        /// <para>
        /// The asset locates a tagged field by scanning the payload for the tag
        /// bytes and reading the value that follows, which is the
        /// <c>&lt;devEUI&gt;/&lt;tag0&gt;/&lt;tag1&gt;?quantity=&lt;len&gt;</c>
        /// form of its address scheme. Translating <c>lorav:tag</c> here means a
        /// conformant Thing Description works without the author hand-encoding
        /// the tag into the href.
        /// </para>
        /// </summary>
        private static string BuildTagAddress(LoRaWANForm form, ThingDescription td, string href)
        {
            // The asset matches exactly two tag bytes (payload[i], payload[i+1]).
            // A longer or shorter tag cannot be honoured, and quietly reading the
            // first two elements would select the wrong field, so it is refused.
            if (form.Tag.Length != 2)
            {
                throw new NotSupportedException(
                    $"A LoRaWAN 'lorav:tag' must have exactly 2 elements (channel and type); this form declares {form.Tag.Length}.");
            }

            ValidateTagArity(td, form.Tag.Length);

            foreach (int element in form.Tag)
            {
                if (element is < 0 or > 255)
                {
                    throw new NotSupportedException(
                        $"The LoRaWAN tag value {element} does not fit in a byte; 'lorav:tagFields' declares byte-sized tag positions.");
                }
            }

            // Keep only the device prefix: a tagged field has no offset, so any
            // trailing path the author wrote is not part of its address.
            string prefix = href.Split('?')[0].TrimEnd('/').Split('/')[0];

            int quantity = form.ByteLength ?? WireTypeLength(form.WireType);

            // NOTE: a tag group's slot offset cannot be expressed here. The
            // asset dispatches on the number of parts in the address, so an
            // extra segment would stop the tag being read at all; it travels
            // through the field-rule registry instead.
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{prefix}/{form.Tag[0]}/{form.Tag[1]}?quantity={quantity}");
        }

        /// <summary>
        /// Checks a form's tag against the Thing's <c>lorav:tagFields</c>.
        /// <para>
        /// <c>lorav:tagFields</c> declares what each tag position means, so a
        /// form whose tag has a different number of elements is describing a
        /// different layout than the Thing claims - an authoring error that
        /// would otherwise surface as a field that silently never matches.
        /// </para>
        /// </summary>
        private static void ValidateTagArity(ThingDescription td, int tagLength)
        {
            TagFieldDefinition[] tagFields = ReadTagFields(td);

            if ((tagFields is null) || (tagFields.Length == 0))
            {
                // tagFields is optional; without it there is nothing to check.
                return;
            }

            if (tagFields.Length != tagLength)
            {
                throw new NotSupportedException(
                    $"The Thing declares {tagFields.Length} 'lorav:tagFields' but a form's 'lorav:tag' has {tagLength} elements.");
            }
        }

        /// <summary>
        /// Reads the thing-level <c>lorav:tagFields</c> from a Thing
        /// Description's additional data.
        /// </summary>
        private static TagFieldDefinition[] ReadTagFields(ThingDescription td)
        {
            if ((td?.AdditionalData is null)
                || !td.AdditionalData.TryGetValue("lorav:tagFields", out object value)
                || (value is null))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<TagFieldDefinition[]>(value.ToString());
            }
            catch (JsonException ex)
            {
                Log.Logger.Debug(ex, "Failed to read lorav:tagFields from the Thing Description.");
                return null;
            }
        }

        /// <summary>
        /// Resolves a form's href against the Thing's base URI.
        /// <para>
        /// The binding expects a Thing to "set base once and use the relative
        /// targets uplink and downlink in each form". The device identity lives
        /// in the base, so a relative href has to be resolved against it before
        /// the asset can tell which device a tag belongs to.
        /// </para>
        /// </summary>
        private static string ResolveHref(string href, ThingDescription td)
        {
            href ??= string.Empty;

            // Already absolute, or no base to resolve against.
            if (href.Contains("://", StringComparison.Ordinal) || string.IsNullOrEmpty(td?.Base))
            {
                return StripScheme(href);
            }

            bool isOperationTarget =
                href.Equals(LoRaWANUri.UplinkTarget, StringComparison.OrdinalIgnoreCase)
                || href.Equals(LoRaWANUri.DownlinkTarget, StringComparison.OrdinalIgnoreCase);

            try
            {
                string devEui = LoRaWANUri.Parse(td.Base).DevEUI;

                // A bare operation target addresses the device's payload as a
                // whole; the field is then located by lorav:byteOffset.
                if (isOperationTarget)
                {
                    return devEui;
                }

                // Anything else is a relative path into the payload (a channel
                // and type for a tagged layout). The device identity lives in
                // the base, so prepend it unless the author repeated it.
                if (href.StartsWith(devEui, StringComparison.OrdinalIgnoreCase))
                {
                    return href;
                }

                return devEui + "/" + href.TrimStart('/');
            }
            catch (ArgumentException)
            {
                return href;
            }
        }

        private static string StripScheme(string href)
        {
            int index = href.IndexOf("://", StringComparison.Ordinal);

            return index < 0 ? href : href[(index + 3)..];
        }

        /// <summary>
        /// Byte width implied by a <c>lorav:wireType</c> when
        /// <c>lorav:byteLength</c> is not given explicitly.
        /// <para>
        /// The binding allows "an <c>xsd:</c> alias or a native type such as
        /// <c>u16</c> or <c>s16</c>", and its own worked examples use the native
        /// spellings, so both forms are recognised. Falling back to two bytes
        /// for an unrecognised native type would silently read the wrong width.
        /// </para>
        /// </summary>
        private static int WireTypeLength(string wireType)
        {
            return wireType?.ToLowerInvariant() switch
            {
                "xsd:byte" or "xsd:unsignedbyte" or "xsd:boolean" => 1,
                "xsd:short" or "xsd:unsignedshort" => 2,
                "xsd:int" or "xsd:unsignedint" or "xsd:float" => 4,
                "xsd:long" or "xsd:unsignedlong" or "xsd:double" => 8,

                // Native spellings used throughout the specification's examples.
                "u8" or "s8" or "i8" or "bool" => 1,
                "u16" or "s16" or "i16" => 2,
                "u32" or "s32" or "i32" or "f32" or "float" => 4,
                "u64" or "s64" or "i64" or "f64" or "double" => 8,

                // Two bytes is the most common LoRaWAN field width, and is what
                // the specification's own example uses.
                _ => 2
            };
        }

        /// <summary>
        /// Throws when a form uses a binding term this driver does not decode,
        /// or one the binding has withdrawn.
        /// <para>
        /// The W3C binding defines derived-value terms (expression-based
        /// computation) and tag-based layouts that change how a payload must be
        /// read. Ignoring them yields a confidently wrong value, so an
        /// unsupported Thing Description is rejected at onboarding time instead.
        /// </para>
        /// <para>
        /// Withdrawn terms are reported with their replacement. They are not
        /// silently treated as their successor because the replacements are not
        /// plain renames - <c>lorav:presenceField</c> and
        /// <c>lorav:presentWhen</c>, for instance, carry different shapes - so
        /// guessing would change what a payload means.
        /// </para>
        /// </summary>
        private static void RejectUnsupportedTerms(object form, string variableId)
        {
            string json = form?.ToString();

            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            foreach (KeyValuePair<string, string> withdrawn in LoRaWANForm.WithdrawnTerms)
            {
                if (ContainsTerm(json, withdrawn.Key))
                {
                    throw new NotSupportedException(
                        $"The LoRaWAN form for '{variableId}' uses '{withdrawn.Key}', which the W3C WoT LoRaWAN binding withdrew. Use {withdrawn.Value} instead.");
                }
            }

            foreach (string term in LoRaWANForm.UnsupportedTerms)
            {
                if (ContainsTerm(json, term))
                {
                    throw new NotSupportedException(
                        $"The LoRaWAN form for '{variableId}' uses '{term}', which the W3C WoT LoRaWAN binding defines but this driver does not yet decode. Remove the term or decode the value at the application server.");
                }
            }
        }

        /// <summary>
        /// Matches a binding term as a whole JSON key.
        /// <para>
        /// A substring test would make <c>lorav:type</c> match
        /// <c>lorav:wireType</c> and reject a perfectly valid form, so the term
        /// must be followed by the end of the property name.
        /// </para>
        /// </summary>
        private static bool ContainsTerm(string json, string term)
        {
            return json.Contains("\"" + term + "\"", StringComparison.OrdinalIgnoreCase);
        }
    }
}
