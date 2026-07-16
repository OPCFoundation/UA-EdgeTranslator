using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Configuration;
using ISession = Opc.Ua.Client.ISession;

namespace WotOpcUaMapper.UAClientLib
{
    /// <summary>
    /// Loads one or more NodeSet2 XML documents into an in-process OPC UA server and
    /// exposes browse/read operations for the right-pane type explorer. Rewritten from the
    /// UA Cloud Library reference to consume nodeset XML strings (from file or Cloud Library)
    /// instead of a database.
    /// </summary>
    public class UAClient : IAsyncDisposable
    {
        /// <summary>namespaceUri -&gt; (requestedVersion, loadedVersion)</summary>
        public Dictionary<string, Tuple<string, string>> LoadedNamespaces { get; } = new();

        /// <summary>Required namespaces that could not be resolved from the Cloud Library.</summary>
        public List<string> MissingNamespaces { get; } = new();

        private readonly OpcUaApplication _opcUaApp;
        private static uint _port = 5000;

        private SimpleServer? _server;
        private ISession? _session;
        private ITelemetryContext _telemetry = null!;

        public UAClient(OpcUaApplication opcUaApp)
        {
            _opcUaApp = opcUaApp;
        }

        public bool IsLoaded => _session is { Connected: true };

        /// <summary>
        /// Loads the given main nodeset XML together with all of its dependencies. The
        /// <paramref name="dependencyResolver"/> is invoked for each required namespace URI and
        /// should return the NodeSet2 XML for it (typically downloaded from the Cloud Library).
        /// </summary>
        public async Task LoadNodesetAsync(string mainXml, Func<string, Task<string?>> dependencyResolver)
        {
            await DisposeSessionAndServerAsync().ConfigureAwait(false);
            LoadedNamespaces.Clear();
            MissingNamespaces.Clear();

            ApplicationInstance app = await _opcUaApp.GetAppAsync().ConfigureAwait(false);
            _telemetry = _opcUaApp.Telemetry;

            EndpointDescription? selectedEndpoint = null;
            int maxRetry = 200;
            while (selectedEndpoint == null)
            {
                if (maxRetry-- < 1)
                {
                    throw new InvalidOperationException("Failed to start local OPC UA server (no free port).");
                }

                try
                {
                    _server = new SimpleServer(app, _port);
                    await app.StartAsync(_server).ConfigureAwait(false);
                    selectedEndpoint = await CoreClientUtils.SelectEndpointAsync(
                        app.ApplicationConfiguration, "opc.tcp://localhost:" + _port, false, _telemetry).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to start OPC UA server on port {_port}: {ex.Message}");
                    selectedEndpoint = null;
                }

                _port++;
                if (_port > 10000)
                {
                    _port = 5000;
                }
            }

            var nodeManager = (NodesetFileNodeManager)_server!.CurrentInstance.NodeManager.NodeManagers[2];

            var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // load dependencies (depth-first) then the main nodeset
            await LoadDependenciesRecursiveAsync(mainXml, nodeManager, dependencyResolver, loaded).ConfigureAwait(false);
            LoadSingleNodeset(mainXml, nodeManager, loaded);

            // connect a client session to the just-started server
            var configuredEndpoint = new ConfiguredEndpoint(null, selectedEndpoint,
                EndpointConfiguration.Create(app.ApplicationConfiguration));

            _session = await new DefaultSessionFactory(_telemetry).CreateAsync(
                app.ApplicationConfiguration,
                configuredEndpoint,
                true,
                false,
                string.Empty,
                30000,
                new UserIdentity(new AnonymousIdentityToken()),
                null).ConfigureAwait(false);

            if (_session == null || !_session.Connected)
            {
                throw new InvalidOperationException("Failed to create OPC UA session for the loaded nodeset.");
            }

            await _session.FetchNamespaceTablesAsync().ConfigureAwait(false);
        }

        private async Task LoadDependenciesRecursiveAsync(
            string nodesetXml,
            NodesetFileNodeManager nodeManager,
            Func<string, Task<string?>> resolver,
            HashSet<string> loaded)
        {
            foreach (var (modelUri, version) in ParseRequiredModels(nodesetXml))
            {
                if (string.Equals(modelUri, Namespaces.OpcUa, StringComparison.Ordinal))
                {
                    // base UA nodeset is always present
                    continue;
                }

                if (loaded.Contains(modelUri) || nodeManager.NamespaceUris.Contains(modelUri))
                {
                    continue;
                }

                string? depXml = await resolver(modelUri).ConfigureAwait(false);
                if (string.IsNullOrEmpty(depXml))
                {
                    if (!MissingNamespaces.Contains(modelUri))
                    {
                        MissingNamespaces.Add(modelUri);
                    }
                    continue;
                }

                // resolve the dependency's own dependencies first
                await LoadDependenciesRecursiveAsync(depXml, nodeManager, resolver, loaded).ConfigureAwait(false);
                LoadSingleNodeset(depXml, nodeManager, loaded, version);
            }
        }

        private void LoadSingleNodeset(string xml, NodesetFileNodeManager nodeManager, HashSet<string> loaded, string? requestedVersion = null)
        {
            nodeManager.AddNamespace(xml);
            nodeManager.AddNodes(xml);

            var (ns, loadedVersion) = ParseModelNamespaceAndVersion(xml);
            if (!string.IsNullOrEmpty(ns))
            {
                loaded.Add(ns);
                if (!LoadedNamespaces.ContainsKey(ns))
                {
                    LoadedNamespaces.Add(ns, new Tuple<string, string>(requestedVersion ?? loadedVersion, loadedVersion));
                }
            }
        }

        private static IEnumerable<(string ModelUri, string Version)> ParseRequiredModels(string xml)
        {
            var results = new List<(string, string)>();
            try
            {
                XDocument doc = XDocument.Parse(xml);
                XNamespace ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
                foreach (var required in doc.Descendants(ns + "RequiredModel"))
                {
                    var uri = required.Attribute("ModelUri")?.Value;
                    var version = required.Attribute("Version")?.Value ?? string.Empty;
                    if (!string.IsNullOrEmpty(uri))
                    {
                        results.Add((uri!, version));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ParseRequiredModels: " + ex.Message);
            }
            return results;
        }

        private static (string Namespace, string Version) ParseModelNamespaceAndVersion(string xml)
        {
            try
            {
                XDocument doc = XDocument.Parse(xml);
                XNamespace ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
                var model = doc.Descendants(ns + "Model").FirstOrDefault();
                if (model != null)
                {
                    return (model.Attribute("ModelUri")?.Value ?? string.Empty,
                            model.Attribute("Version")?.Value ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ParseModelNamespaceAndVersion: " + ex.Message);
            }
            return (string.Empty, string.Empty);
        }

        public async Task<List<NodesetViewerNode>?> GetChildren(string nodeId, bool includeTypes = true)
        {
            if (_session == null || !_session.Connected)
            {
                return null;
            }

            uint mask = (uint)(NodeClass.Object | NodeClass.Variable);
            if (includeTypes)
            {
                mask |= (uint)(NodeClass.DataType | NodeClass.VariableType | NodeClass.ObjectType);
            }

            ReferenceDescriptionCollection references;
            try
            {
                references = await Browse(new BrowseDescription
                {
                    NodeId = ExpandedNodeId.ToNodeId(nodeId, _session.NamespaceUris),
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = mask,
                    ResultMask = (uint)BrowseResultMask.All
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetChildren: " + ex.Message);
                return null;
            }

            var nodes = new List<NodesetViewerNode>();
            foreach (ReferenceDescription description in references)
            {
                NodeId id = ExpandedNodeId.ToNodeId(description.NodeId, _session.NamespaceUris);
                nodes.Add(new NodesetViewerNode
                {
                    Id = NodeId.ToExpandedNodeId(id, _session.NamespaceUris).ToString(),
                    Text = description.DisplayName.ToString(),
                    NodeClass = description.NodeClass.ToString(),
                    Children = new List<NodesetViewerNode>()
                });
            }

            return nodes;
        }

        private async Task<ReferenceDescriptionCollection> Browse(BrowseDescription nodeToBrowse)
        {
            (_, byte[]? continuationPoints, ReferenceDescriptionCollection referencesList) = await _session!.BrowseAsync(
                null, null, nodeToBrowse.NodeId, 0, BrowseDirection.Forward, nodeToBrowse.ReferenceTypeId ?? ReferenceTypeIds.HierarchicalReferences, true, nodeToBrowse.NodeClassMask).ConfigureAwait(false);

            while (referencesList.Count > 0 && continuationPoints != null)
            {
                (_, byte[]? continuationPointsNext, ReferenceDescriptionCollection referencesListNext) =
                    await _session!.BrowseNextAsync(null, false, continuationPoints).ConfigureAwait(false);

                if (referencesListNext.Count > 0)
                {
                    referencesList.AddRange(referencesListNext);
                }

                if (continuationPointsNext == null)
                {
                    break;
                }
                continuationPoints = continuationPointsNext;
            }

            return referencesList;
        }

        /// <summary>
        /// Returns mapping information for a selected node so that a WoT property mapping can be
        /// created: the node id, the data type node id (nsu=...;i=...) and, for complex/structure
        /// types, the list of selectable field paths.
        /// </summary>
        public async Task<NodeMappingInfo?> GetNodeMappingAsync(string nodeId)
        {
            if (_session == null || !_session.Connected)
            {
                return null;
            }

            try
            {
                var localNodeId = ExpandedNodeId.ToNodeId(nodeId, _session.NamespaceUris);
                Node node = await _session.ReadNodeAsync(localNodeId).ConfigureAwait(false);

                var info = new NodeMappingInfo
                {
                    NodeId = ShortNodeId(localNodeId),
                    DisplayName = node.DisplayName?.ToString() ?? string.Empty
                };

                if (node is VariableNode variableNode)
                {
                    NodeId dataTypeId = ExpandedNodeId.ToNodeId(variableNode.DataType, _session.NamespaceUris);
                    info.TypeNodeId = ExpandedNodeIdString(dataTypeId);

                    var fields = await GetComplexTypeFieldsAsync(dataTypeId).ConfigureAwait(false);
                    info.Fields = fields;
                    info.IsComplex = fields.Count > 0;
                }
                else if (node.NodeClass == NodeClass.DataType)
                {
                    info.TypeNodeId = ExpandedNodeIdString(localNodeId);
                    var fields = await GetComplexTypeFieldsAsync(localNodeId).ConfigureAwait(false);
                    info.Fields = fields;
                    info.IsComplex = fields.Count > 0;
                }

                return info;
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetNodeMappingAsync: " + ex.Message);
                return null;
            }
        }

        private async Task<List<ComplexTypeField>> GetComplexTypeFieldsAsync(NodeId dataTypeId)
        {
            var fields = new List<ComplexTypeField>();
            try
            {
                Node dtNode = await _session!.ReadNodeAsync(dataTypeId).ConfigureAwait(false);
                if (dtNode is DataTypeNode dataTypeNode &&
                    dataTypeNode.DataTypeDefinition?.Body is StructureDefinition structure &&
                    structure.Fields != null)
                {
                    foreach (StructureField field in structure.Fields)
                    {
                        fields.Add(new ComplexTypeField
                        {
                            Name = field.Name,
                            DataType = field.DataType != null ? ExpandedNodeIdString(field.DataType) : string.Empty,
                            Path = field.Name
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetComplexTypeFieldsAsync: " + ex.Message);
            }
            return fields;
        }

        public async Task<string> ReadValueAsync(string nodeId)
        {
            if (_session == null || !_session.Connected)
            {
                return string.Empty;
            }

            try
            {
                var localNodeId = ExpandedNodeId.ToNodeId(nodeId, _session.NamespaceUris);
                if (await _session.ReadNodeAsync(localNodeId).ConfigureAwait(false) is not VariableNode variableNode)
                {
                    return string.Empty;
                }

                var nodesToRead = new ReadValueIdCollection
                {
                    new ReadValueId { NodeId = localNodeId, AttributeId = Attributes.Value }
                };

                try
                {
                    var complexTypeSystem = new ComplexTypeSystem(_session);
                    await complexTypeSystem.LoadTypeAsync(variableNode.DataType).ConfigureAwait(false);
                }
                catch
                {
                    // ignore complex type loading failures for display purposes
                }

                ReadResponse response = await _session.ReadAsync(null, 0, TimestampsToReturn.Both, nodesToRead, CancellationToken.None).ConfigureAwait(false);
                if (response.Results.Count > 0 && response.Results[0].Value != null)
                {
                    return response.Results[0].ToString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ReadValueAsync: " + ex.Message);
            }

            return string.Empty;
        }

        private string ShortNodeId(NodeId nodeId)
        {
            // e.g. "s=VoltageL-N" or "i=3005"
            return nodeId.ToString().Split(';').Last();
        }

        private string ExpandedNodeIdString(NodeId nodeId)
        {
            // e.g. "nsu=http://opcfoundation.org/UA/PNEM/;i=3005"
            ExpandedNodeId expanded = NodeId.ToExpandedNodeId(nodeId, _session!.NamespaceUris);
            return expanded.ToString() ?? nodeId.ToString();
        }

        private async Task DisposeSessionAndServerAsync()
        {
            if (_session != null)
            {
                try
                {
                    if (_session.Connected)
                    {
                        await _session.CloseAsync().ConfigureAwait(false);
                    }
                    _session.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("DisposeSession: " + ex.Message);
                }
                _session = null;
            }

            if (_server != null)
            {
                try
                {
                    await _server.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("StopServer: " + ex.Message);
                }
                _server = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeSessionAndServerAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Mapping information extracted from a selected OPC UA node for creating a WoT property mapping.
    /// </summary>
    public class NodeMappingInfo
    {
        public string NodeId { get; set; } = string.Empty;
        public string TypeNodeId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsComplex { get; set; }
        public List<ComplexTypeField> Fields { get; set; } = new();
    }
}
