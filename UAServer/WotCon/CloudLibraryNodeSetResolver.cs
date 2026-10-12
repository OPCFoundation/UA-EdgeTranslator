namespace Opc.Ua.Edge.Translator.WotCon
{
    using global::Opc.Ua.WotCon.Server.Materialization;
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Resolves companion-specification NodeSets a Thing Description depends on
    /// from the local <c>nodesets</c> folder, falling back to the UA Cloud
    /// Library, which is how the WoT v1.02 node manager loads them.
    /// </summary>
    public sealed class CloudLibraryNodeSetResolver : IWotNodeSetResolver
    {
        private readonly UACloudLibraryClient _client;

        public CloudLibraryNodeSetResolver(UACloudLibraryClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async ValueTask<Stream> TryResolveAsync(string namespaceUri, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return null;
            }

            ct.ThrowIfCancellationRequested();

            string nodesetXml = await _client.DownloadNodesetAsync(namespaceUri).ConfigureAwait(false);
            return string.IsNullOrEmpty(nodesetXml)
                ? null
                : new MemoryStream(Encoding.UTF8.GetBytes(nodesetXml));
        }
    }
}
