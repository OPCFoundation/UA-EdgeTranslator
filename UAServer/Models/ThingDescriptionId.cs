namespace Opc.Ua.Edge.Translator.Models
{
    using System;
    using System.Text;

    /// <summary>
    /// Builds the <c>id</c> of a generated Thing Description.
    /// <para>
    /// TD 1.1 requires <c>id</c> to be a valid URI. Asset names are free-form
    /// and routinely contain spaces (for example "Matrikon OPC DA Simulation
    /// Server"), which a bare <c>"urn:" + assetName</c> would turn into an
    /// invalid URN that standards-compliant WoT consumers reject.
    /// </para>
    /// </summary>
    public static class ThingDescriptionId
    {
        /// <summary>
        /// Returns a URN for <paramref name="assetName"/>, percent-encoding any
        /// character that is not permitted in a URN namespace-specific string.
        /// </summary>
        public static string FromAssetName(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                throw new ArgumentException("Asset name must not be null or empty.", nameof(assetName));
            }

            StringBuilder nss = new("urn:");
            foreach (char c in assetName)
            {
                // RFC 8141 allows unreserved characters plus a small set of
                // sub-delims directly; everything else is percent-encoded.
                if (char.IsAsciiLetterOrDigit(c) || c == '-' || c == '.' || c == '_' || c == '~' || c == ':')
                {
                    nss.Append(c);
                }
                else
                {
                    foreach (byte b in Encoding.UTF8.GetBytes(new[] { c }))
                    {
                        nss.Append('%').Append(b.ToString("X2"));
                    }
                }
            }

            return nss.ToString();
        }
    }
}
