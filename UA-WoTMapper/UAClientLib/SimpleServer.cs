using System;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace WotOpcUaMapper.UAClientLib
{
    /// <summary>
    /// Minimal in-process OPC UA server that hosts a single <see cref="NodesetFileNodeManager"/>
    /// into which NodeSet2 XML files are loaded for browsing. Ported from the UA Cloud Library.
    /// </summary>
    public class SimpleServer : StandardServer
    {
        private readonly ApplicationInstance _app;
        private readonly NodesetFileNodeManagerFactory _nodeManagerFactory = new();

        public SimpleServer(ApplicationInstance app, uint port, ITelemetryContext telemetry)
            : base(telemetry)
        {
            _app = app;

            _app.ApplicationConfiguration!.ServerConfiguration!.BaseAddresses = new[] { "opc.tcp://localhost:" + port };

            AddNodeManager(_nodeManagerFactory);
        }

        /// <summary>The node manager NodeSet2 files are loaded into, available once the server has started.</summary>
        public NodesetFileNodeManager? NodesetNodeManager => _nodeManagerFactory.Instance;

        private sealed class NodesetFileNodeManagerFactory : INodeManagerFactory
        {
            public NodesetFileNodeManager? Instance { get; private set; }

            // Namespaces are registered on demand as NodeSet2 files are loaded.
            public ArrayOf<string> NamespacesUris => Array.Empty<string>();

            public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
            {
                Instance = new NodesetFileNodeManager(server, configuration);
                return Instance;
            }
        }
    }
}
