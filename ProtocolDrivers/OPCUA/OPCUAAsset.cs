namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Client.ComplexTypes;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class BrowsedNode
    {
        public string BrowsePath { get; set; }

        public string ExpandedNodeId { get; set; }

        public TypeEnum WoTType { get; set; }

        public TypeString XsdType { get; set; }

        public bool ReadOnly { get; set; }
    }

    public class OPCUAAsset : IAsset
    {
        private ISession _session = null;
        private string _endpoint = string.Empty;

        private List<SessionReconnectHandler> _reconnectHandlers = new List<SessionReconnectHandler>();
        private object _reconnectHandlersLock = new object();

        private Dictionary<string, uint> _missedKeepAlives = new Dictionary<string, uint>();
        private object _missedKeepAlivesLock = new object();

        private readonly Dictionary<ISession, ComplexTypeSystem> _complexTypeList = new Dictionary<ISession, ComplexTypeSystem>();

        public bool IsConnected => _session != null && _session.Connected;

        public void Connect(string ipAddress, int port)
        {
            _endpoint = ipAddress + ":" + port;
            string url = BuildEndpointUrl(ipAddress, port);

            var username = Environment.GetEnvironmentVariable("OPCUA_CLIENT_USERNAME");
            var password = Environment.GetEnvironmentVariable("OPCUA_CLIENT_PASSWORD");
            ConnectSessionAsync(url, username, password).GetAwaiter().GetResult();
        }

        public void Disconnect()
        {
            if (_session != null)
            {
                _session.CloseAsync().GetAwaiter().GetResult();
                _session = null;
            }
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        private string BuildEndpointUrl(string ipAddress, int port)
        {
            // If the caller already provided a full OPC UA URL, use it as-is.
            if (!string.IsNullOrEmpty(ipAddress) && ipAddress.Contains("://"))
            {
                return ipAddress;
            }

            // If the caller passed only the scheme portion (e.g. "opc.tcp") because the
            // endpoint URL was split on ':', or supplied no usable host, fall back to the
            // last known endpoint.
            bool noUsableHost = string.IsNullOrWhiteSpace(ipAddress)
                                || ipAddress.Equals("opc.tcp", StringComparison.OrdinalIgnoreCase);

            if (noUsableHost && !string.IsNullOrEmpty(_endpoint))
            {
                return _endpoint;
            }

            // Plain host[:port] pair.
            return port > 0
                ? "opc.tcp://" + ipAddress + ":" + port
                : "opc.tcp://" + ipAddress;
        }

        public object Read(AssetTag tag)
        {
            object value = null;

            byte[] tagBytes = Read(tag.Address, 0, null, 0).GetAwaiter().GetResult();

            if ((tagBytes != null) && (tagBytes.Length > 0))
            {

                if (tag.Type == "Float")
                {
                    value = BitConverter.ToSingle(tagBytes);
                }
                else if (tag.Type == "Boolean")
                {
                    value = BitConverter.ToBoolean(tagBytes);
                }
                else if (tag.Type == "Integer")
                {
                    value = BitConverter.ToInt32(tagBytes);
                }
                else if (tag.Type == "String")
                {
                    value = Encoding.UTF8.GetString(tagBytes);
                }
                else if (tag.Type == "Byte")
                {
                    value = tagBytes[0];
                }
                else if (tag.Type == "SByte")
                {
                    value = (sbyte)tagBytes[0];
                }
                else if (tag.Type == "Short" || tag.Type == "Int16")
                {
                    value = BitConverter.ToInt16(tagBytes);
                }
                else if (tag.Type == "UShort" || tag.Type == "UInt16")
                {
                    value = BitConverter.ToUInt16(tagBytes);
                }
                else if (tag.Type == "UInteger" || tag.Type == "UInt32")
                {
                    value = BitConverter.ToUInt32(tagBytes);
                }
                else if (tag.Type == "Long" || tag.Type == "Int64")
                {
                    value = BitConverter.ToInt64(tagBytes);
                }
                else if (tag.Type == "ULong" || tag.Type == "UInt64")
                {
                    value = BitConverter.ToUInt64(tagBytes);
                }
                else if (tag.Type == "Double")
                {
                    value = BitConverter.ToDouble(tagBytes);
                }
                else
                {
                    throw new ArgumentException("Type not supported by OPC UA.");
                }
            }

            return value;
        }

        public void Write(AssetTag tag, object value)
        {
            object typedValue;
            if (tag.Type == "Float")
            {
                typedValue = float.Parse(value.ToString());
            }
            else if (tag.Type == "Boolean")
            {
                typedValue = bool.Parse(value.ToString());
            }
            else if (tag.Type == "Integer")
            {
                typedValue = int.Parse(value.ToString());
            }
            else if (tag.Type == "String")
            {
                typedValue = value.ToString();
            }
            else if (tag.Type == "Byte")
            {
                typedValue = byte.Parse(value.ToString());
            }
            else if (tag.Type == "SByte")
            {
                typedValue = sbyte.Parse(value.ToString());
            }
            else if (tag.Type == "Short" || tag.Type == "Int16")
            {
                typedValue = short.Parse(value.ToString());
            }
            else if (tag.Type == "UShort" || tag.Type == "UInt16")
            {
                typedValue = ushort.Parse(value.ToString());
            }
            else if (tag.Type == "UInteger" || tag.Type == "UInt32")
            {
                typedValue = uint.Parse(value.ToString());
            }
            else if (tag.Type == "Long" || tag.Type == "Int64")
            {
                typedValue = long.Parse(value.ToString());
            }
            else if (tag.Type == "ULong" || tag.Type == "UInt64")
            {
                typedValue = ulong.Parse(value.ToString());
            }
            else if (tag.Type == "Double")
            {
                typedValue = double.Parse(value.ToString());
            }
            else
            {
                throw new ArgumentException("Type not supported by OPC UA.");
            }

            WriteValue(tag.Address, typedValue).GetAwaiter().GetResult();
        }

        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            if (_session != null)
            {
                var nodeId = ExpandedNodeId.ToNodeId(new ExpandedNodeId(addressWithinAsset), _session.NamespaceUris);

                DataValue value;
                try
                {
                    value = _session.ReadValueAsync(nodeId).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // The node no longer exists on the server (e.g. ephemeral diagnostics
                    // nodes whose owning session/subscription has been closed). Treat as
                    // "no value" rather than failing the whole asset and triggering a reconnect.
                    Log.Logger.Warning($"Node {nodeId} not readable on server, skipping. ({ex.Message})");
                    return Task.FromResult(Array.Empty<byte>());
                }

                if (value?.Value == null)
                {
                    return Task.FromResult(Array.Empty<byte>());
                }

                byte[] bytes = value.Value switch
                {
                    float f    => BitConverter.GetBytes(f),
                    double d   => BitConverter.GetBytes(d),
                    bool b     => BitConverter.GetBytes(b),
                    int i      => BitConverter.GetBytes(i),
                    uint ui    => BitConverter.GetBytes(ui),
                    short s    => BitConverter.GetBytes(s),
                    ushort us  => BitConverter.GetBytes(us),
                    long l     => BitConverter.GetBytes(l),
                    ulong ul   => BitConverter.GetBytes(ul),
                    byte by    => new[] { by },
                    string str => Encoding.UTF8.GetBytes(str),
                    byte[] arr => arr,
                    _          => Encoding.UTF8.GetBytes(value.Value.ToString() ?? string.Empty)
                };

                return Task.FromResult(bytes);
            }
            else
            {
                return Task.FromResult(Array.Empty<byte>());
            }
        }

        private Task WriteValue(string addressWithinAsset, object value)
        {
            if (_session == null)
            {
                return Task.CompletedTask;
            }

            WriteValue nodeToWrite = new()
            {
                NodeId = new NodeId(addressWithinAsset),
                Value = new DataValue(new Variant(value))
            };

            WriteValueCollection nodesToWrite = new() { nodeToWrite };

            RequestHeader requestHeader = new()
            {
                ReturnDiagnostics = (uint)DiagnosticsMasks.All
            };

            WriteResponse response = _session.WriteAsync(
                requestHeader,
                nodesToWrite,
                CancellationToken.None).GetAwaiter().GetResult();

            ClientBase.ValidateResponse(response.Results, nodesToWrite);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, nodesToWrite);

            if (StatusCode.IsBad(response.Results[0]))
            {
                throw ServiceResultException.Create(response.Results[0], 0, response.DiagnosticInfos, response.ResponseHeader.StringTable);
            }

            return Task.CompletedTask;
        }

        private async Task ConnectSessionAsync(string endpointUrl, string username, string password)
        {
            // check if the required session is already available
            if (_session != null && _session.Endpoint.EndpointUrl == endpointUrl)
            {
                return;
            }

            var selectedEndpoint = await CoreClientUtils.SelectEndpointAsync(Program.App.ApplicationConfiguration, endpointUrl, true, Program.Telemetry).ConfigureAwait(false);
            var configuredEndpoint = new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create(Program.App.ApplicationConfiguration));

            var timeout = (uint)Program.App.ApplicationConfiguration.ClientConfiguration.DefaultSessionTimeout;

            UserIdentity userIdentity = null;
            if (username == null)
            {
                userIdentity = new UserIdentity(new AnonymousIdentityToken());
            }
            else
            {
                userIdentity = new UserIdentity(username, Encoding.UTF8.GetBytes(password));
            }

            try
            {
                _session = await new DefaultSessionFactory(Program.Telemetry).CreateAsync(
                    Program.App.ApplicationConfiguration,
                    configuredEndpoint,
                    true,
                    false,
                    Program.App.ApplicationConfiguration.ApplicationName,
                    timeout,
                    userIdentity,
                    null
                ).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return;
            }

            // enable diagnostics
            _session.ReturnDiagnostics = DiagnosticsMasks.All;

            // register keep alive callback
            _session.KeepAlive += KeepAliveHandler;

            // enable subscriptions transfer
            _session.DeleteSubscriptionsOnClose = false;
            _session.TransferSubscriptionsOnReconnect = true;


            // load complex type system
            try
            {
                if (!_complexTypeList.ContainsKey(_session))
                {
                    _complexTypeList.Add(_session, new ComplexTypeSystem(_session));
                }

                await _complexTypeList[_session].LoadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
            }
        }

        private void KeepAliveHandler(ISession session, KeepAliveEventArgs eventArgs)
        {
            if (eventArgs != null && session != null && session.ConfiguredEndpoint != null)
            {
                try
                {
                    var endpoint = session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;

                    lock (_missedKeepAlivesLock)
                    {
                        if (!ServiceResult.IsGood(eventArgs.Status))
                        {
                            if (session.Connected)
                            {
                                // add a new entry, if required
                                if (!_missedKeepAlives.ContainsKey(endpoint))
                                {
                                    _missedKeepAlives.Add(endpoint, 0);
                                }

                                _missedKeepAlives[endpoint]++;
                            }

                            // start reconnect if there are 3 missed keep alives
                            if (_missedKeepAlives[endpoint] >= 3)
                            {
                                // check if a reconnection is already in progress
                                var reconnectInProgress = false;
                                lock (_reconnectHandlersLock)
                                {
                                    foreach (var handler in _reconnectHandlers)
                                    {
                                        if (ReferenceEquals(handler.Session, session))
                                        {
                                            reconnectInProgress = true;
                                            break;
                                        }
                                    }
                                }

                                if (!reconnectInProgress)
                                {
                                    var reconnectHandler = new SessionReconnectHandler(Program.Telemetry);
                                    lock (_reconnectHandlersLock)
                                    {
                                        _reconnectHandlers.Add(reconnectHandler);
                                    }
                                    reconnectHandler.BeginReconnect(session, 10000, ReconnectCompleteHandler);
                                }
                            }
                        }
                        else
                        {
                            if (_missedKeepAlives.ContainsKey(endpoint) && _missedKeepAlives[endpoint] != 0)
                            {
                                // Reset missed keep alive count
                                _missedKeepAlives[endpoint] = 0;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.Message, ex);
                }
            }
        }

        private void ReconnectCompleteHandler(object sender, EventArgs e)
        {
            // find our reconnect handler
            SessionReconnectHandler reconnectHandler = null;
            lock (_reconnectHandlersLock)
            {
                foreach (var handler in _reconnectHandlers)
                {
                    if (ReferenceEquals(sender, handler))
                    {
                        reconnectHandler = handler;
                        break;
                    }
                }
            }

            // ignore callbacks from discarded objects
            if (reconnectHandler == null || reconnectHandler.Session == null)
            {
                return;
            }

            // update the session
            _session = reconnectHandler.Session;


            lock (_reconnectHandlersLock)
            {
                _reconnectHandlers.Remove(reconnectHandler);
            }
            reconnectHandler.Dispose();
        }

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            CallMethodRequestCollection requests = new CallMethodRequestCollection
            {
                new CallMethodRequest
                {
                    ObjectId = new NodeId(method.Parent.NodeId),
                    MethodId = method.NodeId
                }
            };

            if (inputArgs != null)
            {
                requests[0].InputArguments = new VariantCollection();

                foreach (var arg in inputArgs)
                {
                    requests[0].InputArguments.Add(new Variant(arg));
                }
            }

            CallResponse response = _session.CallAsync(
                null,
                requests,
                CancellationToken.None).GetAwaiter().GetResult();

            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);

            StatusCode status = new StatusCode(0);
            if ((response.Results != null) && (response.Results.Count > 0))
            {
                status = response.Results[0].StatusCode;

                if (StatusCode.IsBad(response.Results[0].StatusCode) && (response.ResponseHeader.StringTable != null) && (response.ResponseHeader.StringTable.Count > 0))
                {
                    return response.ResponseHeader.StringTable[0];
                }

                if ((response.Results[0].OutputArguments != null) && (response.Results[0].OutputArguments.Count > 0))
                {
                    outputArgs = new List<object>(response.Results[0].OutputArguments.Count);

                    for (int i = 0; i < response.Results[0].OutputArguments.Count; i++)
                    {
                        outputArgs.Add(response.Results[0].OutputArguments[i].Value);
                    }
                }
            }

            return "Action executed successfully.";
        }

        /// <summary>
        /// Exhaustively browses the server's Objects folder and returns every UA Variable
        /// (which includes UA Properties, since Properties are Variables with a PropertyType
        /// type definition) found in the address space, with the full dot-notation browse path.
        /// </summary>
        public List<BrowsedNode> BrowseObjectsFolder()
        {
            List<BrowsedNode> results = new();

            if (_session == null || !_session.Connected)
            {
                return results;
            }

            HashSet<NodeId> visited = new();
            BrowseRecursive(ObjectIds.ObjectsFolder, string.Empty, results, visited);

            return results;
        }

        private void BrowseRecursive(NodeId nodeId, string parentPath, List<BrowsedNode> results, HashSet<NodeId> visited)
        {
            if (nodeId == null || NodeId.IsNull(nodeId))
            {
                return;
            }

            // protect against cycles in the address space
            if (!visited.Add(nodeId))
            {
                return;
            }

            BrowseDescription nodeToBrowse = new()
            {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
                ResultMask = (uint)BrowseResultMask.All
            };

            BrowseDescriptionCollection nodesToBrowse = new() { nodeToBrowse };

            ReferenceDescriptionCollection references = new();

            try
            {
                BrowseResponse browseResponse = _session.BrowseAsync(
                    null,
                    null,
                    0u,
                    nodesToBrowse,
                    CancellationToken.None).GetAwaiter().GetResult();

                BrowseResultCollection browseResults = browseResponse.Results;
                if (browseResults == null || browseResults.Count == 0)
                {
                    return;
                }

                ClientBase.ValidateResponse(browseResults, nodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(browseResponse.DiagnosticInfos, nodesToBrowse);

                if (StatusCode.IsBad(browseResults[0].StatusCode))
                {
                    return;
                }

                references.AddRange(browseResults[0].References);

                // follow continuation points to make sure the browse is exhaustive
                byte[] continuationPoint = browseResults[0].ContinuationPoint;
                while (continuationPoint != null && continuationPoint.Length > 0)
                {
                    ByteStringCollection continuationPoints = new() { continuationPoint };

                    BrowseNextResponse browseNextResponse = _session.BrowseNextAsync(
                        null,
                        false,
                        continuationPoints,
                        CancellationToken.None).GetAwaiter().GetResult();

                    BrowseResultCollection nextResults = browseNextResponse.Results;
                    if (nextResults == null || nextResults.Count == 0 || StatusCode.IsBad(nextResults[0].StatusCode))
                    {
                        break;
                    }

                    references.AddRange(nextResults[0].References);
                    continuationPoint = nextResults[0].ContinuationPoint;
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Warning($"Failed to browse node {nodeId}: {ex.Message}");
                return;
            }

            foreach (ReferenceDescription reference in references)
            {
                if (reference.NodeId == null)
                {
                    continue;
                }

                string browseName = reference.BrowseName?.Name ?? reference.DisplayName?.Text ?? string.Empty;
                if (string.IsNullOrEmpty(browseName))
                {
                    continue;
                }

                string childPath = string.IsNullOrEmpty(parentPath) ? browseName : parentPath + "." + browseName;
                NodeId childNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, _session.NamespaceUris);

                // Skip the server diagnostics subtree: its instance NodeIds are created per
                // session/subscription and become invalid (BadNodeIdUnknown) as soon as those
                // sessions/subscriptions are closed, so persisting them as asset tags is unsafe.
                // Also skip the entire Server branch: it exposes diagnostics, redundancy and other
                // server-managed nodes whose instance NodeIds are created per session/subscription
                // and/or whose Value attribute is intentionally not implemented on non-redundant
                // servers (e.g. Server.ServerRedundancy.CurrentServerId / i=11312 returns
                // BadAttributeIdInvalid). Persisting any of these as asset tags is unsafe and
                // would raise exceptions every polling cycle.
                if ((childNodeId == ObjectIds.Server) || (reference.NodeId.NamespaceUri == "http://opcfoundation.org/UA/Diagnostics"))
                {
                    continue;
                }

                if (childPath.StartsWith("MemoryBuffers"))
                {
                    // Skip MemoryBuffers and all their children: these are used for large data transfers and have
                    // instance NodeIds that become invalid as soon as the transfer is complete, so persisting
                    // them as asset tags is unsafe.
                    continue;
                }

                // Only add variables to the results, but keep recursing through objects since variables can be nested under objects or other variables (e.g. Properties).
                if (reference.NodeClass == NodeClass.Variable)
                {
                    BrowsedNode node = BuildBrowsedNode(childNodeId, childPath);
                    if (node != null)
                    {
                        results.Add(node);
                    }
                }

                // Variables can themselves have children (e.g. UA Properties), so always recurse.
                BrowseRecursive(childNodeId, childPath, results, visited);
            }
        }

        private BrowsedNode BuildBrowsedNode(NodeId nodeId, string browsePath)
        {
            try
            {
                ReadValueIdCollection nodesToRead = new()
                {
                    new ReadValueId() { NodeId = nodeId, AttributeId = Attributes.DataType },
                    new ReadValueId() { NodeId = nodeId, AttributeId = Attributes.ValueRank },
                    new ReadValueId() { NodeId = nodeId, AttributeId = Attributes.AccessLevel }
                };

                ReadResponse readResponse = _session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    CancellationToken.None).GetAwaiter().GetResult();

                DataValueCollection values = readResponse.Results;
                if (values == null || values.Count < 3)
                {
                    return null;
                }

                NodeId dataTypeId = values[0].Value as NodeId;
                int valueRank = values[1].Value is int rank ? rank : ValueRanks.Scalar;
                byte accessLevel = values[2].Value is byte access ? access : (byte)0;

                // Skip nodes the server has marked as not readable; otherwise every polling
                // cycle would throw BadNotReadable on Read.
                if ((accessLevel & AccessLevels.CurrentRead) != AccessLevels.CurrentRead)
                {
                    return null;
                }

                BuiltInType builtInType = TypeInfo.GetBuiltInType(dataTypeId, _session.TypeTree);

                MapTypes(builtInType, out TypeEnum wotType, out TypeString xsdType);

                bool writable = (accessLevel & AccessLevels.CurrentWrite) == AccessLevels.CurrentWrite;

                return new BrowsedNode()
                {
                    BrowsePath = browsePath,
                    ExpandedNodeId = NodeId.ToExpandedNodeId(nodeId, _session.NamespaceUris).ToString(),
                    WoTType = wotType,
                    XsdType = xsdType,
                    ReadOnly = !writable
                };
            }
            catch (Exception ex)
            {
                Log.Logger.Warning($"Failed to read attributes for node {nodeId}: {ex.Message}");
                return null;
            }
        }

        private static void MapTypes(BuiltInType builtInType, out TypeEnum wotType, out TypeString xsdType)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    wotType = TypeEnum.Boolean;
                    xsdType = TypeString.Boolean;
                    break;
                case BuiltInType.SByte:
                case BuiltInType.Byte:
                    wotType = TypeEnum.Integer;
                    xsdType = TypeString.Byte;
                    break;
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                    wotType = TypeEnum.Integer;
                    xsdType = TypeString.Short;
                    break;
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                    wotType = TypeEnum.Integer;
                    xsdType = TypeString.Integer;
                    break;
                case BuiltInType.Float:
                    wotType = TypeEnum.Number;
                    xsdType = TypeString.Float;
                    break;
                case BuiltInType.Double:
                    wotType = TypeEnum.Number;
                    xsdType = TypeString.Double;
                    break;
                case BuiltInType.String:
                case BuiltInType.DateTime:
                case BuiltInType.Guid:
                case BuiltInType.ByteString:
                case BuiltInType.XmlElement:
                case BuiltInType.NodeId:
                case BuiltInType.ExpandedNodeId:
                case BuiltInType.QualifiedName:
                case BuiltInType.LocalizedText:
                case BuiltInType.StatusCode:
                    wotType = TypeEnum.String;
                    xsdType = TypeString.String;
                    break;
                default:
                    wotType = TypeEnum.Object;
                    xsdType = TypeString.String;
                    break;
            }
        }
    }
}
