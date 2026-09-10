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
            }

            return tag;
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

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{prefix}/{form.ByteOffset.Value}?quantity={quantity}");
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
        /// </summary>
        private static int WireTypeLength(string wireType)
        {
            return wireType?.ToLowerInvariant() switch
            {
                "xsd:byte" or "xsd:unsignedbyte" or "xsd:boolean" => 1,
                "xsd:short" or "xsd:unsignedshort" => 2,
                "xsd:int" or "xsd:unsignedint" or "xsd:float" => 4,
                "xsd:long" or "xsd:unsignedlong" or "xsd:double" => 8,

                // Two bytes is the most common LoRaWAN field width, and is what
                // the specification's own example uses.
                _ => 2
            };
        }

        /// <summary>
        /// Throws when a form uses a binding term this driver does not decode.
        /// <para>
        /// The W3C binding defines conditional-presence and derived-value terms
        /// (branching layouts, expression-based computation) that change how a
        /// payload must be read. Ignoring them yields a confidently wrong value,
        /// so an unsupported Thing Description is rejected at onboarding time
        /// instead.
        /// </para>
        /// </summary>
        private static void RejectUnsupportedTerms(object form, string variableId)
        {
            string json = form?.ToString();

            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            foreach (string term in LoRaWANForm.UnsupportedTerms)
            {
                if (json.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        $"The LoRaWAN form for '{variableId}' uses '{term}', which the W3C WoT LoRaWAN binding defines but this driver does not yet decode. Remove the term or decode the value at the application server.");
                }
            }
        }
    }
}
