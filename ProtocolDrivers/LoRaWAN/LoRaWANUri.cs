namespace Opc.Ua.Edge.Translator.Models
{
    using System;
    using System.Linq;

    /// <summary>
    /// A parsed LoRaWAN URI, as defined by the W3C WoT LoRaWAN binding.
    /// <para>
    /// The specification gives this ABNF:
    /// </para>
    /// <code>
    /// lorawan-URI = "lorawan://" authority "/" dev-eui "/" op-target
    /// authority   = host        ; the network-facing interface
    /// dev-eui     = 16HEXDIG    ; the value of lorav:devEUI, case-insensitive
    /// op-target   = "uplink" / "downlink"
    /// </code>
    /// <para>
    /// A consumer never addresses the end device over the radio; the URI
    /// identifies the network server (or application server / gateway bridge)
    /// and the device, "and nothing else". In particular the OTAA root keys are
    /// secrets and MUST NOT appear anywhere in the URI.
    /// </para>
    /// </summary>
    public sealed class LoRaWANUri
    {
        private const string _scheme = "lorawan://";

        /// <summary>The translator's own gateway-provisioning target.</summary>
        public const string RouterConfigTarget = "routerconfig";

        public const string UplinkTarget = "uplink";

        public const string DownlinkTarget = "downlink";

        /// <summary>The network-facing interface host.</summary>
        public string Authority { get; private init; }

        /// <summary>The 16 hex digit device EUI.</summary>
        public string DevEUI { get; private init; }

        /// <summary>The operation target: uplink, downlink or routerconfig.</summary>
        public string Target { get; private init; }

        /// <summary>
        /// True for the translator-specific gateway provisioning URI, which is
        /// not part of the W3C binding.
        /// </summary>
        public bool IsRouterConfig => string.Equals(Target, RouterConfigTarget, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// For the router-config form only: the gateway model whose stored
        /// Thing Description carries the router configuration.
        /// </summary>
        public string GatewayModel { get; private init; }

        /// <summary>
        /// Parses a LoRaWAN base URI.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The URI does not match the binding's ABNF, or it embeds a secret.
        /// </exception>
        public static LoRaWANUri Parse(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
            {
                throw new ArgumentException("A LoRaWAN base URI is required.", nameof(uri));
            }

            if (!uri.StartsWith(_scheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{uri}' is not a LoRaWAN URI; it must start with '{_scheme}'.", nameof(uri));
            }

            string[] parts = uri[_scheme.Length..]
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Gateway provisioning: lorawan://<devEUI>/<gatewayModel>/routerconfig
            // This predates the binding and is specific to this translator, so it
            // is matched first and left alone.
            if ((parts.Length == 3) && string.Equals(parts[2], RouterConfigTarget, StringComparison.OrdinalIgnoreCase))
            {
                return new LoRaWANUri
                {
                    Authority = string.Empty,
                    DevEUI = parts[0],
                    GatewayModel = parts[1],
                    Target = RouterConfigTarget
                };
            }

            // A Thing "normally sets base once and uses the relative targets
            // uplink and downlink in each form", so the base carries only the
            // interface and the device: "lorawan://ns.example.org/<devEUI>/".
            // The target is therefore optional here and supplied by the form.
            if ((parts.Length != 2) && (parts.Length != 3))
            {
                throw new ArgumentException(
                    $"'{uri}' does not match the LoRaWAN binding URI format 'lorawan://<host>/<devEUI>/' with an optional '<uplink|downlink>' target.",
                    nameof(uri));
            }

            string authority = parts[0];
            string devEui = parts[1];
            string target = parts.Length == 3 ? parts[2] : null;

            if (!IsDevEui(devEui))
            {
                // The most likely cause is the pre-standard layout, where the
                // devEUI sat in the authority position and a secret followed it.
                throw new ArgumentException(
                    $"'{devEui}' is not a 16 hex digit device EUI. The LoRaWAN binding expects 'lorawan://<host>/<devEUI>/'.",
                    nameof(uri));
            }

            if (target is not null
                && !string.Equals(target, UplinkTarget, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(target, DownlinkTarget, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"'{target}' is not a valid LoRaWAN operation target; the binding defines only '{UplinkTarget}' and '{DownlinkTarget}'.",
                    nameof(uri));
            }

            return new LoRaWANUri
            {
                Authority = authority,
                DevEUI = devEui,

                // Default to uplink: the binding is uplink-only for now, so a
                // base without an explicit target addresses the uplink stream.
                Target = (target ?? UplinkTarget).ToLowerInvariant()
            };
        }

        /// <summary>
        /// True when the value looks like an OTAA root key rather than a device
        /// EUI: 32 hex digits (128 bits). Used to reject Thing Descriptions that
        /// embed a secret in the URI, which the binding forbids outright.
        /// </summary>
        public static bool LooksLikeSecret(string value)
        {
            return (value?.Length == 32) && value.All(Uri.IsHexDigit);
        }

        private static bool IsDevEui(string value)
        {
            return (value?.Length == 16) && value.All(Uri.IsHexDigit);
        }
    }
}
