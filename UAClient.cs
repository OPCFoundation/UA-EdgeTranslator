
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Client.ComplexTypes;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading.Tasks;

    public class UAClient : IAsset
    {
        private ISession _session = null;
        private string _endpoint = string.Empty;

        private List<SessionReconnectHandler> _reconnectHandlers = new List<SessionReconnectHandler>();
        private object _reconnectHandlersLock = new object();

        private Dictionary<string, uint> _missedKeepAlives = new Dictionary<string, uint>();
        private object _missedKeepAlivesLock = new object();

        private readonly Dictionary<ISession, ComplexTypeSystem> _complexTypeList = new Dictionary<ISession, ComplexTypeSystem>();

        public void Connect(string ipAddress, int port)
        {
            string url = "opc.tcp://" + ipAddress + ":" + port;
            string username = Environment.GetEnvironmentVariable("OPCUA_CLIENT_USERNAME");
            string password = Environment.GetEnvironmentVariable("OPCUA_CLIENT_PASSWORD");
            ConnectSessionAsync(url, username, password).GetAwaiter().GetResult();
        }

        public void Disconnect()
        {
            if (_session != null)
            {
                _session.Close();
                _session = null;
            }
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            if (_session != null)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(new ExpandedNodeId(addressWithinAsset), _session.NamespaceUris);
                DataValue value = _session.ReadValue(nodeId);

                BinaryFormatter bf = new();
                using (MemoryStream ms = new())
                {

#pragma warning disable SYSLIB0011
                    bf.Serialize(ms, value.Value);
#pragma warning restore SYSLIB0011

                    return Task.FromResult(ms.ToArray());
                }
            }
            else
            {
                return Task.FromResult(new byte[0]);
            }
        }

        public Task Write(string addressWithinAsset, byte unitID, byte[] values, bool singleBitOnly)
        {
            using (MemoryStream memStream = new(values))
            {
                BinaryFormatter binForm = new();

#pragma warning disable SYSLIB0011
                object value = binForm.Deserialize(memStream);
#pragma warning restore SYSLIB0011

                WriteValue nodeToWrite = new()
                {
                    NodeId = new NodeId(addressWithinAsset),
                    Value = new DataValue(new Variant(value))
                };

                WriteValueCollection nodesToWrite = new(){ nodeToWrite };

                RequestHeader requestHeader = new()
                {
                    ReturnDiagnostics = (uint)DiagnosticsMasks.All
                };

                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                ResponseHeader responseHeader = _session.Write(
                    requestHeader,
                    nodesToWrite,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToWrite);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToWrite);

                if (StatusCode.IsBad(results[0]))
                {
                    throw ServiceResultException.Create(results[0], 0, diagnosticInfos, responseHeader.StringTable);
                }

                return Task.CompletedTask;
            }
        }

        private async Task ConnectSessionAsync(string endpointUrl, string username, string password)
        {
            _endpoint = endpointUrl;

            // check if the required session is already available
            if ((_session != null) && (_session.Endpoint.EndpointUrl == endpointUrl))
            {
                return;
            }

            EndpointDescription selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointUrl, true);
            ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create(Program.App.ApplicationConfiguration));

            uint timeout = (uint)Program.App.ApplicationConfiguration.ClientConfiguration.DefaultSessionTimeout;

            UserIdentity userIdentity = null;
            if (username == null)
            {
                userIdentity = new UserIdentity(new AnonymousIdentityToken());
            }
            else
            {
                userIdentity = new UserIdentity(username, password);
            }

            try
            {
                _session = await Session.Create(
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

                await _complexTypeList[_session].Load().ConfigureAwait(false);
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
                    string endpoint = session.ConfiguredEndpoint.EndpointUrl.AbsoluteUri;

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
                                bool reconnectInProgress = false;
                                lock (_reconnectHandlersLock)
                                {
                                    foreach (SessionReconnectHandler handler in _reconnectHandlers)
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
                                    SessionReconnectHandler reconnectHandler = new SessionReconnectHandler();
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
                            if (_missedKeepAlives.ContainsKey(endpoint) && (_missedKeepAlives[endpoint] != 0))
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
                foreach (SessionReconnectHandler handler in _reconnectHandlers)
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
    }
}
