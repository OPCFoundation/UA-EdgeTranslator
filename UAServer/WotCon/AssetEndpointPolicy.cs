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
            ServiceResult result = WotEndpointValidator.Validate(endpoint, Create(drivers), out Uri normalized);
            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            // The package validator compares host names literally, so an equivalent IP
            // spelling of a blocked address (such as ::ffff:169.254.169.254) passes it.
            return IsAlwaysBlockedHost(normalized?.Host)
                ? ServiceResult.Create(StatusCodes.BadSecurityChecksFailed, "Endpoint host '{0}' is blocked by the endpoint policy.", normalized?.Host)
                : result;
        }

        /// <summary>
        /// Rejects an endpoint whose host is always blocked. Used where an endpoint has
        /// already passed the package validator, which compares host names literally.
        /// </summary>
        public static void ThrowIfBlocked(string endpoint)
        {
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) && IsAlwaysBlockedHost(uri.Host))
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed, "Endpoint host '{0}' is blocked by the endpoint policy.", uri.Host);
            }
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

            // An IP literal has many spellings (IPv4-mapped IPv6, expanded or compressed
            // IPv6, leading zeros). Compare addresses, not strings.
            if (TryParseAddress(normalized, out System.Net.IPAddress address))
            {
                return s_alwaysBlockedHosts.Any(blocked => TryParseAddress(blocked, out System.Net.IPAddress blockedAddress) && blockedAddress.Equals(address));
            }

            return s_alwaysBlockedHosts.Any(blocked => string.Equals(blocked, normalized, StringComparison.OrdinalIgnoreCase))
                || ResolvesToBlockedAddress(normalized);
        }

        /// <summary>The longest a host name lookup may take.</summary>
        public static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);

        /// <summary>Resolves a host name; replaceable so tests do not depend on DNS.</summary>
        public static Func<string, System.Net.IPAddress[]> ResolveHost { get; set; } = ResolveWithTimeout;

        /// <summary>Resolves a host name, giving up after <see cref="DnsTimeout"/>.</summary>
        public static System.Net.IPAddress[] ResolveWithTimeout(string host)
        {
            using System.Threading.CancellationTokenSource timeout = new(DnsTimeout);
            return System.Net.Dns.GetHostAddressesAsync(host, timeout.Token).GetAwaiter().GetResult();
        }

        /// <summary>
        /// True when a DNS name resolves to a blocked address, so an alias of the
        /// metadata service is refused like the service's own name. A name that does not
        /// resolve here is not blocked: the connection to it fails anyway.
        /// </summary>
        private static bool ResolvesToBlockedAddress(string host)
        {
            System.Net.IPAddress[] addresses;
            try
            {
                addresses = ResolveHost(host) ?? Array.Empty<System.Net.IPAddress>();
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException or OperationCanceledException)
            {
                return false;
            }

            return addresses.Any(resolved => IsAlwaysBlockedHost(resolved.ToString()));
        }

        /// <summary>
        /// Parses an IP literal into its canonical form: an IPv4-mapped IPv6 address
        /// becomes the IPv4 address, and a scope id is dropped.
        /// </summary>
        private static bool TryParseAddress(string text, out System.Net.IPAddress address)
        {
            if (!System.Net.IPAddress.TryParse(text, out address))
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            else if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            {
                address = new System.Net.IPAddress(address.GetAddressBytes());
            }

            return true;
        }
    }
}
