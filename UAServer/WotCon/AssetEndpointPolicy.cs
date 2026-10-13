namespace Opc.Ua.Edge.Translator.WotCon
{
    using global::Opc.Ua.WotCon.Bindings;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Linq;

    /// <summary>
    /// The single outbound-endpoint policy of UA Edge Translator.
    /// </summary>
    /// <remarks>
    /// WoT-Con 1.1 §Security requires the same endpoint allowlist, trust policy and
    /// size limits for the <c>AssetEndpoint</c> argument of <c>ConnectionTest</c> and
    /// <c>CreateAssetForEndpoint</c>, for endpoints <c>DiscoverAssets</c> probes, and
    /// for forms the binder executes. Both the 1.02 asset surface and the registry
    /// data plane therefore validate against this one policy.
    /// <para>
    /// Assets live on plant networks, so private ranges and loopback are allowed,
    /// but the cloud instance-metadata endpoint is always blocked so the server
    /// cannot be used to probe it.
    /// </para>
    /// </remarks>
    public static class AssetEndpointPolicy
    {
        private static readonly string[] s_alwaysBlockedHosts =
        [
            "169.254.169.254",
            "fd00:ec2::254",
            "metadata.google.internal"
        ];

        public static WotEndpointPolicy Create(ProtocolDriverRegistry drivers)
        {
            ArgumentNullException.ThrowIfNull(drivers);

            WotEndpointPolicy policy = new()
            {
                AllowLoopback = true,
                AllowPrivateAddresses = true
            };

            foreach (string scheme in drivers.AllDrivers.Select(driver => driver.Scheme))
            {
                policy.AllowedSchemes.Add(scheme);
            }

            foreach (string host in s_alwaysBlockedHosts)
            {
                policy.BlockedHosts.Add(host);

                // A fully qualified name with a trailing root dot resolves to the same
                // host, and the validator compares names literally, so block that form too.
                if (!host.Contains(':', StringComparison.Ordinal) && !char.IsDigit(host[^1]))
                {
                    policy.BlockedHosts.Add(host + ".");
                }
            }

            return policy;
        }

        /// <summary>
        /// Validates a caller-supplied or discovered asset endpoint.
        /// </summary>
        public static ServiceResult Validate(string endpoint, ProtocolDriverRegistry drivers)
        {
            return WotEndpointValidator.Validate(endpoint, Create(drivers), out _);
        }

        /// <summary>
        /// True when <paramref name="host"/> is never a legitimate asset, such as the
        /// cloud instance-metadata service. Used where the endpoint is a bare
        /// <c>host[:port]</c> that carries no scheme to validate.
        /// </summary>
        public static bool IsAlwaysBlockedHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            // Strip IPv6 brackets and the DNS root dot: "metadata.google.internal." is
            // the same host as "metadata.google.internal".
            string normalized = host.Trim().Trim('[', ']').TrimEnd('.');
            return s_alwaysBlockedHosts.Any(blocked => string.Equals(blocked, normalized, StringComparison.OrdinalIgnoreCase));
        }
    }
}
