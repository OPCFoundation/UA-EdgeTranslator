namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Server;

    /// <summary>
    /// Creates the UA Edge Translator node manager for the hosted OPC UA server.
    /// Registered with <c>IOpcUaServerBuilder.AddNodeManager</c> so the SDK's hosted
    /// service adds it to the server instead of a hand-written
    /// <c>CreateMasterNodeManager</c> override.
    /// </summary>
    public sealed class UANodeManagerFactory : INodeManagerFactory
    {
        public ArrayOf<string> NamespacesUris => new[] { "http://opcfoundation.org/UA/EdgeTranslator/" };

        public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new UANodeManager(server, configuration);
        }
    }
}
