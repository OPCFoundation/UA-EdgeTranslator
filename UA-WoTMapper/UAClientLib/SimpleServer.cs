using System.Collections.Generic;
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

        public SimpleServer(ApplicationInstance app, uint port)
        {
            _app = app;

            _app.ApplicationConfiguration.ServerConfiguration.BaseAddresses[0] = "opc.tcp://localhost:" + port;
        }

        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            List<INodeManager> nodeManagers = new()
            {
                new NodesetFileNodeManager(server, configuration)
            };

            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }
    }
}
