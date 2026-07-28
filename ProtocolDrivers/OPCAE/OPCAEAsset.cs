namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;

    public sealed class OPCAEAsset : IEventingAsset
    {
        private const string ApiAssemblyFileName = "OpcNetApi.dll";
        private const string ComAssemblyFileName = "OpcNetApi.Com.dll";
        private const string EventChangedName = "EventChanged";

        private readonly object _sync = new();
        private Uri _endpoint;
        private OpcAeEventForm _form;
        private object _server;
        private object _subscription;
        private EventInfo _eventChanged;
        private Delegate _eventHandler;

        public event EventHandler<AlarmEvent> AlarmReceived;

        public bool IsConnected { get; private set; }

        public void Configure(Uri endpoint, OpcAeEventForm form)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _form = form ?? new OpcAeEventForm { Href = endpoint.AbsoluteUri };
        }

        public void Connect(string ipAddress, int port)
        {
            if (_endpoint == null)
            {
                throw new InvalidOperationException("Configure must be called before connecting an OPC A&E asset.");
            }

            lock (_sync)
            {
                StopEventSubscription();

                try
                {
                    Assembly api = LoadRuntimeAssembly(ApiAssemblyFileName);
                    Assembly com = LoadRuntimeAssembly(ComAssemblyFileName);
                    Type factoryType = RequireType(com, "OpcCom.Factory");
                    Type urlType = RequireType(api, "Opc.URL");
                    Type serverType = RequireType(api, "Opc.Ae.Server");

                    object factory = Activator.CreateInstance(factoryType);
                    string classicEndpoint = "opcae://" + _endpoint.Host + "/" + _endpoint.AbsolutePath.Trim('/');
                    object url = Activator.CreateInstance(urlType, classicEndpoint);
                    _server = Activator.CreateInstance(serverType, factory, url);
                    serverType.GetMethod("Connect", [urlType, api.GetType("Opc.ConnectData")])?.Invoke(_server, [url, null]);
                    IsConnected = true;
                    Log.Logger.Information("Connected to OPC A&E server {ProgId} on {Host}", _endpoint.AbsolutePath.Trim('/'), _endpoint.Host);
                }
                catch (FileNotFoundException ex)
                {
                    throw new InvalidOperationException(
                        "OPC Classic Core Components are required for OPC A&E. Install the 32-bit OPC Foundation Core Components redistributable, then restart UA Edge Translator.", ex);
                }
                catch
                {
                    IsConnected = false;
                    throw;
                }
            }
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                StopEventSubscription();
                try
                {
                    _server?.GetType().GetMethod("Disconnect", Type.EmptyTypes)?.Invoke(_server, null);
                }
                finally
                {
                    _server = null;
                    IsConnected = false;
                }
            }
        }

        public string GetRemoteEndpoint() => _endpoint?.AbsoluteUri ?? string.Empty;

        public object Read(AssetTag tag) => throw new NotSupportedException("OPC A&E does not expose polling data-access tags.");

        public void Write(AssetTag tag, object value) => throw new NotSupportedException("OPC A&E alarms are read-only in this driver.");

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs) => null;

        public void StartEventSubscription()
        {
            lock (_sync)
            {
                if (!IsConnected || _subscription != null)
                {
                    return;
                }

                try
                {
                    Assembly api = _server.GetType().Assembly;
                    Type stateType = RequireType(api, "Opc.Ae.SubscriptionState");
                    Type filtersType = RequireType(api, "Opc.Ae.SubscriptionFilters");
                    object state = Activator.CreateInstance(stateType);
                    SetRequiredProperty(state, "Name", "UAEdgeTranslatorA&E");
                    SetRequiredProperty(state, "Active", true);
                    SetRequiredProperty(state, "BufferTime", Math.Max(0, _form.BufferTime));
                    SetRequiredProperty(state, "MaxSize", Math.Max(1, _form.MaxEvents));

                    _subscription = InvokeRequired(_server, "CreateSubscription", [stateType], [state])
                        ?? throw new InvalidOperationException("OPC A&E server did not create a subscription.");

                    object filters = Activator.CreateInstance(filtersType);
                    SetRequiredProperty(filters, "EventTypes", _form.EventTypes);
                    SetRequiredProperty(filters, "LowSeverity", Math.Clamp(_form.LowSeverity, 1, 1000));
                    SetRequiredProperty(filters, "HighSeverity", Math.Clamp(_form.HighSeverity, 1, 1000));
                    PopulateCollection(filters, "Categories", _form.Categories);
                    PopulateCollection(filters, "Areas", _form.Areas);
                    PopulateCollection(filters, "Sources", _form.Sources);
                    InvokeRequired(_subscription, "SetFilters", [filtersType], [filters]);

                    _eventChanged = _subscription.GetType().GetEvent(EventChangedName)
                        ?? throw new InvalidOperationException("OPC A&E subscription does not expose EventChanged.");
                    _eventHandler = CreateEventHandler(_eventChanged.EventHandlerType);
                    _eventChanged.AddEventHandler(_subscription, _eventHandler);

                    if (_form.RefreshOnConnect)
                    {
                        TryRefreshOnConnect();
                    }
                }
                catch
                {
                    StopEventSubscription();
                    throw;
                }
            }
        }

        public void RefreshEventSubscription()
        {
            lock (_sync)
            {
                _subscription?.GetType().GetMethod("Refresh", Type.EmptyTypes)?.Invoke(_subscription, null);
            }
        }

        public void StopEventSubscription()
        {
            lock (_sync)
            {
                if (_subscription == null)
                {
                    return;
                }

                try
                {
                    if (_eventChanged != null && _eventHandler != null)
                    {
                        _eventChanged.RemoveEventHandler(_subscription, _eventHandler);
                    }

                    _subscription.GetType().GetMethod("Dispose", Type.EmptyTypes)?.Invoke(_subscription, null);
                }
                finally
                {
                    _eventHandler = null;
                    _eventChanged = null;
                    _subscription = null;
                }
            }
        }

        private void OnEvents(object notifications, bool refresh, bool lastRefresh)
        {
            if (notifications is not IEnumerable events)
            {
                return;
            }

            foreach (object notification in events)
            {
                try
                {
                    string source = GetProperty<string>(notification, "SourceID") ?? string.Empty;
                    string condition = GetProperty<string>(notification, "ConditionName") ?? string.Empty;
                    string subCondition = GetProperty<string>(notification, "SubConditionName") ?? string.Empty;
                    int state = GetProperty<int>(notification, "NewState");
                    var alarm = new AlarmEvent
                    {
                        ConditionKey = source + "|" + condition,
                        Source = source,
                        ConditionName = condition,
                        SubConditionName = subCondition,
                        Category = GetProperty<int>(notification, "EventCategory"),
                        Severity = GetProperty<int>(notification, "Severity"),
                        Message = GetProperty<string>(notification, "Message"),
                        Time = GetProperty<DateTime>(notification, "Time"),
                        Enabled = (state & 1) != 0,
                        Active = (state & 2) != 0,
                        Acknowledged = (state & 4) != 0,
                        ActorId = GetProperty<string>(notification, "ActorID")
                    };

                    AlarmReceived?.Invoke(this, alarm);
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning(ex, "Failed to normalize OPC A&E event from {Endpoint}", GetRemoteEndpoint());
                }
            }
        }

        private Delegate CreateEventHandler(Type handlerType)
        {
            MethodInfo invoke = handlerType?.GetMethod("Invoke")
                ?? throw new InvalidOperationException("OPC A&E EventChanged delegate is unavailable.");
            ParameterInfo[] parameters = invoke.GetParameters();

            if (invoke.ReturnType != typeof(void) || parameters.Length != 3 ||
                parameters[1].ParameterType != typeof(bool) || parameters[2].ParameterType != typeof(bool))
            {
                throw new InvalidOperationException("OPC A&E EventChanged delegate has an unsupported signature.");
            }

            ParameterExpression[] callbackParameters = parameters
                .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
                .ToArray();
            MethodInfo callback = typeof(OPCAEAsset).GetMethod(
                nameof(OnEvents),
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("OPC A&E event callback is unavailable.");

            return Expression.Lambda(
                handlerType,
                Expression.Call(
                    Expression.Constant(this),
                    callback,
                    Expression.Convert(callbackParameters[0], typeof(object)),
                    callbackParameters[1],
                    callbackParameters[2]),
                callbackParameters).Compile();
        }

        private void TryRefreshOnConnect()
        {
            try
            {
                RefreshEventSubscription();
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    "OPC A&E condition refresh is unavailable for {Endpoint}; continuing with live events. {Reason}",
                    GetRemoteEndpoint(),
                    ex.GetBaseException().Message);
            }
        }

        private static Assembly LoadRuntimeAssembly(string fileName)
        {
            string path = Path.Combine(Path.GetDirectoryName(typeof(OPCAEAsset).Assembly.Location)!, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Required OPC A&E runtime assembly was not packaged.", path);
            }

            return Assembly.LoadFrom(path);
        }

        private static Type RequireType(Assembly assembly, string typeName) =>
            assembly.GetType(typeName) ?? throw new InvalidOperationException($"Required OPC A&E type {typeName} is unavailable.");

        private static object InvokeRequired(object target, string methodName, Type[] parameterTypes, object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, parameterTypes)
                ?? throw new InvalidOperationException($"Required OPC A&E method {methodName} is unavailable.");
            return method.Invoke(target, arguments);
        }

        private static void SetRequiredProperty(object target, string propertyName, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName)
                ?? throw new InvalidOperationException($"Required OPC A&E property {propertyName} is unavailable.");
            property.SetValue(target, value);
        }

        private static T GetProperty<T>(object target, string propertyName)
        {
            object value = target.GetType().GetProperty(propertyName)?.GetValue(target);
            return value is T typed ? typed : default;
        }

        private static void PopulateCollection(object target, string propertyName, IEnumerable values)
        {
            if (values == null)
            {
                return;
            }

            object collection = target.GetType().GetProperty(propertyName)?.GetValue(target);
            MethodInfo add = collection?.GetType().GetMethod("Add");
            if (add == null)
            {
                return;
            }

            foreach (object value in values)
            {
                add.Invoke(collection, [value]);
            }
        }
    }
}