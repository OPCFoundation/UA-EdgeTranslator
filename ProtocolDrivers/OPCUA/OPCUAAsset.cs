namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
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

        /// <summary>For a method, the expanded node id of the object it is called on.</summary>
        public string ObjectExpandedNodeId { get; set; }

        /// <summary>For a method, its input arguments by name.</summary>
        public Dictionary<string, Property> InputArguments { get; set; }

        /// <summary>For a method, its output arguments by name.</summary>
        public Dictionary<string, Property> OutputArguments { get; set; }

        public bool IsMethod => ObjectExpandedNodeId != null;
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

        public async Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
        {
            _endpoint = ipAddress + ":" + port;
            string url = BuildEndpointUrl(ipAddress, port);

            var username = Environment.GetEnvironmentVariable("OPCUA_CLIENT_USERNAME");
            var password = Environment.GetEnvironmentVariable("OPCUA_CLIENT_PASSWORD");
            await ConnectSessionAsync(url, username, password).ConfigureAwait(false);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_session != null)
            {
                await _session.CloseAsync().ConfigureAwait(false);
                _session = null;
            }

            DisposeComplexTypes();
        }

        /// <summary>
        /// Disposes the type systems kept per session, which releases their node caches.
        /// When <paramref name="keep"/> is given, its type system is kept.
        /// </summary>
        private void DisposeComplexTypes(ISession keep = null)
        {
            foreach (ISession session in new List<ISession>(_complexTypeList.Keys))
            {
                if (!ReferenceEquals(session, keep))
                {
                    _complexTypeList[session].Dispose();
                    _complexTypeList.Remove(session);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectAsync().ConfigureAwait(false);
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

        public async Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default)
        {
            object value = null;

            byte[] tagBytes = await Read(tag.Address, 0, null, 0).ConfigureAwait(false);

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

        public async Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default)
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

            await WriteValue(tag.Address, typedValue).ConfigureAwait(false);
        }

        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            if (_session != null)
            {
                var nodeId = ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(addressWithinAsset), _session.NamespaceUris);

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

                // Unwrap to the CLR value before switching. Pattern-matching the Variant
                // itself would never match (it is a struct, not a float/int/...), so every
                // reading would silently fall through to the text-encoding default branch.
                object rawValue = VariantConversion.ToClrValue(value.WrappedValue);
                if (rawValue == null)
                {
                    return Task.FromResult(Array.Empty<byte>());
                }

                byte[] bytes = rawValue switch
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
                    _          => Encoding.UTF8.GetBytes(rawValue.ToString() ?? string.Empty)
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

            // Resolve exactly as the read path does — new NodeId(string) ignores ns= prefixes.
            NodeId nodeId = ExpandedNodeId.ToNodeId(
                ExpandedNodeId.Parse(addressWithinAsset),
                _session.NamespaceUris);

            // The value is runtime-typed (it arrives as object from the driver contract),
            // so the (object, TypeInfo) constructor is the only applicable overload; see
            // NodeFactory.CreateVariable for the rationale.
#pragma warning disable CS0618 // Type or member is obsolete
            Variant variant = new(value, TypeInfo.Construct(value));
#pragma warning restore CS0618

            WriteValue nodeToWrite = new()
            {
                NodeId = nodeId,
                Value = new DataValue(variant)
            };

            ArrayOf<WriteValue> nodesToWrite = new(new[] { nodeToWrite });

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
                    default
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
                    // SDK 2.0.0 moved ComplexTypeSystem to Opc.Ua.Core.Schema; it now resolves
                    // types through an IComplexTypeResolver rather than the session directly.
                    // The type system owns the resolver and its node cache, so disposing it
                    // (see DisposeComplexTypes) releases both. Loaded types stay registered in
                    // the session's encodeable factory.
                    _complexTypeList.Add(_session, new ComplexTypeSystem(
                        new NodeCacheResolver(_session, Program.Telemetry),
                        new DefaultComplexTypeFactory(),
                        Program.Telemetry,
                        ownsResolver: true));
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

            // a reconnect may hand back a new session; drop the type systems of replaced ones
            DisposeComplexTypes(_session);


            lock (_reconnectHandlersLock)
            {
                _reconnectHandlers.Remove(reconnectHandler);
            }
            reconnectHandler.Dispose();
        }

        /// <summary>
        /// The asset's Thing Description, whose action forms name the remote methods.
        /// </summary>
        public ThingDescription ThingDescription { get; set; }

        /// <summary>
        /// Maps a translator MethodState to the method on the remote server. The TD action
        /// of the same name must name both the method (href) and the object it is called
        /// on (uav:objectId) as expanded node ids; nothing is browsed to guess them.
        /// </summary>
        private bool TryResolveRemoteMethod(MethodState method, out NodeId objectId, out NodeId methodId)
        {
            objectId = NodeId.Null;
            methodId = NodeId.Null;

            string name = method.BrowseName.Name;
            if (ThingDescription?.Actions == null
                || string.IsNullOrEmpty(name)
                || !ThingDescription.Actions.TryGetValue(name, out TDAction action)
                || action.Forms == null)
            {
                return false;
            }

            foreach (object rawForm in action.Forms)
            {
                UAForm form = rawForm as UAForm
                    ?? (rawForm == null ? null : JsonConvert.DeserializeObject<UAForm>(rawForm.ToString()));
                if (form == null || string.IsNullOrEmpty(form.Href) || string.IsNullOrEmpty(form.ObjectId))
                {
                    continue;
                }

                try
                {
                    methodId = ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(form.Href), _session.NamespaceUris);
                    objectId = ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(form.ObjectId), _session.NamespaceUris);
                }
                catch (Exception ex) when (ex is ServiceResultException or FormatException or ArgumentException)
                {
                    Log.Logger.Warning($"Action {method.BrowseName.Name} has a form whose href or uav:objectId is not an expanded node id.");
                    objectId = NodeId.Null;
                    methodId = NodeId.Null;
                    continue;
                }

                if (!objectId.IsNull && !methodId.IsNull)
                {
                    return true;
                }
            }

            objectId = NodeId.Null;
            methodId = NodeId.Null;
            return false;
        }

        public async Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default)
        {
            // NodeId is a value type in SDK 2.0.0, so assignment already copies it (the old
            // NodeId(NodeId) copy constructor is gone).
            // The MethodState belongs to the translator's address space, so its NodeIds mean
            // nothing to the remote server: call the method and object its form names.
            if (!TryResolveRemoteMethod(method, out NodeId objectId, out NodeId methodId))
            {
                return AssetActionResult.Failure($"Action {method.BrowseName.Name} does not name its method (href) and object (uav:objectId) as expanded node ids.");
            }

            CallMethodRequest request = new()
            {
                ObjectId = objectId,
                MethodId = methodId
            };

            if (inputArgs != null)
            {
                List<Variant> inputArguments = new(inputArgs.Count);

                foreach (var arg in inputArgs)
                {
                    // Driver inputs are runtime-typed; see NodeFactory.CreateVariable.
#pragma warning disable CS0618 // Type or member is obsolete
                    inputArguments.Add(new Variant(arg, TypeInfo.Construct(arg)));
#pragma warning restore CS0618
                }

                request.InputArguments = new ArrayOf<Variant>(inputArguments.ToArray());
            }

            ArrayOf<CallMethodRequest> requests = new(new[] { request });

            CallResponse response = await _session.CallAsync(
                null,
                requests,
                cancellationToken).ConfigureAwait(false);

            ClientBase.ValidateResponse(response.Results, requests);
            ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, requests);

            StatusCode status = new StatusCode(0);
            IList<object> outputArgs = null;
            if (response.Results.Count > 0)
            {
                status = response.Results[0].StatusCode;

                if (StatusCode.IsBad(response.Results[0].StatusCode) && (response.ResponseHeader.StringTable.Count > 0))
                {
                    return AssetActionResult.FromStatus(response.ResponseHeader.StringTable[0]);
                }

                if (StatusCode.IsBad(status))
                {
                    return AssetActionResult.Failure(status.ToString());
                }

                ArrayOf<Variant> outputArguments = response.Results[0].OutputArguments;
                if (outputArguments.Count > 0)
                {
                    outputArgs = new List<object>(outputArguments.Count);

                    for (int i = 0; i < outputArguments.Count; i++)
                    {
                        // Outputs go back through the object-based AssetActionResult, so
                        // unwrap to the CLR value rather than boxing the Variant itself.
                        outputArgs.Add(VariantConversion.ToClrValue(outputArguments[i]));
                    }
                }
            }

            return AssetActionResult.Success("Action executed successfully.", outputArgs);
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
            if (nodeId.IsNull)
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
                NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.Method),
                ResultMask = (uint)BrowseResultMask.All
            };

            ArrayOf<BrowseDescription> nodesToBrowse = new(new[] { nodeToBrowse });

            List<ReferenceDescription> references = new();

            try
            {
                BrowseResponse browseResponse = _session.BrowseAsync(
                    null,
                    null,
                    0u,
                    nodesToBrowse,
                    CancellationToken.None).GetAwaiter().GetResult();

                // A null ArrayOf reports Count == 0, so this covers the old null check.
                ArrayOf<BrowseResult> browseResults = browseResponse.Results;
                if (browseResults.Count == 0)
                {
                    return;
                }

                ClientBase.ValidateResponse(browseResults, nodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(browseResponse.DiagnosticInfos, nodesToBrowse);

                if (StatusCode.IsBad(browseResults[0].StatusCode))
                {
                    return;
                }

                AddReferences(references, browseResults[0].References);

                // follow continuation points to make sure the browse is exhaustive.
                // ContinuationPoint is a ByteString struct now; a null one has Length 0, so
                // this matches the old `!= null && Length > 0` exactly.
                ByteString continuationPoint = browseResults[0].ContinuationPoint;
                while (continuationPoint.Length > 0)
                {
                    ArrayOf<ByteString> continuationPoints = new(new[] { continuationPoint });

                    BrowseNextResponse browseNextResponse = _session.BrowseNextAsync(
                        null,
                        false,
                        continuationPoints,
                        CancellationToken.None).GetAwaiter().GetResult();

                    ArrayOf<BrowseResult> nextResults = browseNextResponse.Results;
                    if (nextResults.Count == 0 || StatusCode.IsBad(nextResults[0].StatusCode))
                    {
                        break;
                    }

                    AddReferences(references, nextResults[0].References);
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
                if (reference.NodeId.IsNull)
                {
                    continue;
                }

                // QualifiedName/LocalizedText are structs; a default one has a null
                // Name/Text, so the ?? fallback chain behaves as it did before.
                string browseName = reference.BrowseName.Name ?? reference.DisplayName.Text ?? string.Empty;
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

                // A method becomes an action called on the object it was browsed from. Its
                // argument properties are read here, not recursed into as tags.
                if (reference.NodeClass == NodeClass.Method)
                {
                    BrowsedNode method = BuildBrowsedMethod(nodeId, childNodeId, childPath);
                    if (method != null)
                    {
                        results.Add(method);
                    }

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

        // ArrayOf<T> does not implement IEnumerable<T>, so List.AddRange cannot take it.
        private static void AddReferences(List<ReferenceDescription> target, ArrayOf<ReferenceDescription> source)
        {
            foreach (ReferenceDescription reference in source)
            {
                target.Add(reference);
            }
        }

        private BrowsedNode BuildBrowsedMethod(NodeId objectId, NodeId methodId, string browsePath)
        {
            try
            {
                Dictionary<string, Property> inputs = null;
                Dictionary<string, Property> outputs = null;

                ArrayOf<BrowseDescription> nodesToBrowse = new(new[]
                {
                    new BrowseDescription
                    {
                        NodeId = methodId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Variable,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                });

                BrowseResponse response = _session.BrowseAsync(null, null, 0u, nodesToBrowse, CancellationToken.None).GetAwaiter().GetResult();
                if (response.Results.Count > 0 && StatusCode.IsGood(response.Results[0].StatusCode))
                {
                    foreach (ReferenceDescription argument in response.Results[0].References)
                    {
                        string name = argument.BrowseName.Name;
                        if (name == BrowseNames.InputArguments)
                        {
                            inputs = ReadArguments(ExpandedNodeId.ToNodeId(argument.NodeId, _session.NamespaceUris));
                        }
                        else if (name == BrowseNames.OutputArguments)
                        {
                            outputs = ReadArguments(ExpandedNodeId.ToNodeId(argument.NodeId, _session.NamespaceUris));
                        }
                    }
                }

                return new BrowsedNode()
                {
                    BrowsePath = browsePath,
                    ExpandedNodeId = NodeId.ToExpandedNodeId(methodId, _session.NamespaceUris).ToString(),
                    ObjectExpandedNodeId = NodeId.ToExpandedNodeId(objectId, _session.NamespaceUris).ToString(),
                    InputArguments = inputs,
                    OutputArguments = outputs
                };
            }
            catch (Exception ex)
            {
                Log.Logger.Warning($"Failed to read the arguments of method {methodId}: {ex.Message}");
                return null;
            }
        }

        private Dictionary<string, Property> ReadArguments(NodeId argumentsId)
        {
            ArrayOf<ReadValueId> nodesToRead = new(new[] { new ReadValueId() { NodeId = argumentsId, AttributeId = Attributes.Value } });
            ReadResponse readResponse = _session.ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, CancellationToken.None).GetAwaiter().GetResult();
            if (readResponse.Results.Count == 0 || VariantConversion.ToClrValue(readResponse.Results[0].WrappedValue) is not System.Collections.IEnumerable values)
            {
                return null;
            }

            Dictionary<string, Property> arguments = new();
            foreach (object value in values)
            {
                Argument argument = value switch
                {
                    Argument a => a,
                    ExtensionObject extension when extension.TryGetValue(out Argument decoded, _session.MessageContext) => decoded,
                    _ => null
                };

                if (argument == null || string.IsNullOrEmpty(argument.Name) || arguments.ContainsKey(argument.Name))
                {
                    continue;
                }

                MapTypes(TypeInfo.GetBuiltInType(argument.DataType, _session.TypeTree), out TypeEnum wotType, out TypeString _);
                arguments.Add(argument.Name, new Property() { Type = argument.ValueRank >= 0 ? TypeEnum.Object : wotType });
            }

            return arguments.Count > 0 ? arguments : null;
        }

        private BrowsedNode BuildBrowsedNode(NodeId nodeId, string browsePath)
        {
            try
            {
                ArrayOf<ReadValueId> nodesToRead = new(new[]
                {
                    new ReadValueId() { NodeId = nodeId, AttributeId = Attributes.DataType },
                    new ReadValueId() { NodeId = nodeId, AttributeId = Attributes.ValueRank },
                    new ReadValueId() { NodeId = nodeId, AttributeId = Attributes.AccessLevel }
                });

                ReadResponse readResponse = _session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    CancellationToken.None).GetAwaiter().GetResult();

                // A null ArrayOf reports Count == 0, so this covers the old null check.
                ArrayOf<DataValue> values = readResponse.Results;
                if (values.Count < 3)
                {
                    return null;
                }

                // Unwrap each Variant before pattern matching; matching the Variant struct
                // itself would never succeed and every node would map to TypeEnum.Object.
                NodeId dataTypeId = VariantConversion.ToClrValue(values[0].WrappedValue) is NodeId id ? id : NodeId.Null;
                int valueRank = VariantConversion.ToClrValue(values[1].WrappedValue) is int rank ? rank : ValueRanks.Scalar;
                byte accessLevel = VariantConversion.ToClrValue(values[2].WrappedValue) is byte access ? access : (byte)0;

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
