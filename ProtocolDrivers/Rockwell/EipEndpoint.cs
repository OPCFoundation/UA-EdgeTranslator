namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using System;

    /// <summary>
    /// An EtherNet/IP endpoint as the W3C WoT EtherNet/IP binding writes it:
    /// <c>eip://host[:port][/route-path][/]</c>, where the route path is the CIP route
    /// from the communication module to the controller (default <c>1,0</c>, backplane
    /// slot 0) and the port defaults to 44818.
    /// </summary>
    public sealed class EipEndpoint
    {
        public const string Scheme = "eip";

        public const int DefaultPort = 44818;

        public const string DefaultRoutePath = "1,0";

        private EipEndpoint(string host, int port, string routePath)
        {
            Host = host;
            Port = port;
            RoutePath = routePath;
        }

        public string Host { get; }

        public int Port { get; }

        public string RoutePath { get; }

        /// <summary>The libplctag gateway: the host, with the port when it is not the default.</summary>
        public string Gateway => Port == DefaultPort ? Host : Host + ":" + Port;

        /// <summary>The binding form of the endpoint, ending in <c>/</c> so relative tag hrefs resolve below it.</summary>
        public string ToBaseUri()
        {
            string host = Host.Contains(':') ? "[" + Host + "]" : Host;
            string port = Port == DefaultPort ? string.Empty : ":" + Port;
            return $"{Scheme}://{host}{port}/{RoutePath}/";
        }

        /// <summary>Parses an <c>eip</c> URI, such as a Thing Description <c>base</c>.</summary>
        public static bool TryParse(string uri, out EipEndpoint endpoint)
        {
            endpoint = null;
            if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri.Trim(), UriKind.Absolute, out Uri parsed)
                || !string.Equals(parsed.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(parsed.Host))
            {
                return false;
            }

            int port = parsed.IsDefaultPort || parsed.Port <= 0 ? DefaultPort : parsed.Port;

            // The route path is the first path segment; anything after it is a tag reference.
            string path = Uri.UnescapeDataString(parsed.AbsolutePath).Trim('/');
            int slash = path.IndexOf('/');
            string routePath = slash >= 0 ? path.Substring(0, slash) : path;
            if (string.IsNullOrWhiteSpace(routePath))
            {
                routePath = DefaultRoutePath;
            }

            endpoint = new EipEndpoint(parsed.IdnHost.Trim('[', ']'), port, routePath);
            return true;
        }
    }

    /// <summary>
    /// The tag a form <c>href</c> addresses: the percent-decoded Logix tag path and the
    /// optional raw byte <c>offset</c> query parameter.
    /// </summary>
    public readonly struct EipTagReference
    {
        public EipTagReference(string tagPath, int offset)
        {
            TagPath = tagPath;
            Offset = offset;
        }

        /// <summary>The Logix tag expression, such as <c>Program:Packaging.Counter</c>.</summary>
        public string TagPath { get; }

        /// <summary>Byte offset into the tag value; 0 when none is given.</summary>
        public int Offset { get; }

        /// <summary>
        /// The href the binding requires for <paramref name="tagPath"/>: reserved
        /// characters are percent-encoded and a tag path with a colon starts with
        /// <c>./</c>, so it is not read as a URI scheme.
        /// </summary>
        public static string ToHref(string tagPath, int offset = 0)
        {
            string encoded = Uri.EscapeDataString(tagPath ?? string.Empty).Replace("%3A", ":").Replace("%2E", ".");
            string href = encoded.Contains(':') ? "./" + encoded : encoded;
            return offset > 0 ? href + "?offset=" + offset : href;
        }

        /// <summary>
        /// Parses a form href, relative (<c>./Program:Main.Speed</c>, <c>Recipe%5B3%5D</c>)
        /// or absolute (<c>eip://host/1,0/Speed</c>). The legacy bare <c>?N</c> query is
        /// read as a byte offset, which is how earlier drafts were implemented.
        /// </summary>
        public static EipTagReference Parse(string href)
        {
            string text = href?.Trim() ?? string.Empty;

            if (text.StartsWith(EipEndpoint.Scheme + "://", StringComparison.OrdinalIgnoreCase))
            {
                // eip://host[:port]/route-path/tag-path: drop the authority and route path.
                int pathStart = text.IndexOf('/', EipEndpoint.Scheme.Length + 3);
                text = pathStart < 0 ? string.Empty : text.Substring(pathStart + 1);
                int routeEnd = text.IndexOf('/');
                text = routeEnd < 0 ? string.Empty : text.Substring(routeEnd + 1);
            }

            int offset = 0;
            int query = text.IndexOf('?');
            if (query >= 0)
            {
                offset = ParseOffset(text.Substring(query + 1));
                text = text.Substring(0, query);
            }

            if (text.StartsWith("./", StringComparison.Ordinal))
            {
                text = text.Substring(2);
            }

            return new EipTagReference(Uri.UnescapeDataString(text), offset);
        }

        private static int ParseOffset(string query)
        {
            foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split('=', 2);
                string value = pair.Length == 2 && pair[0].Equals("offset", StringComparison.OrdinalIgnoreCase)
                    ? pair[1]
                    : pair.Length == 1 ? pair[0] : null;
                if (value != null && int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int offset))
                {
                    return offset;
                }
            }

            return 0;
        }
    }
}
