namespace Opc.Ua.Edge.Translator.Tests.WotCon
{
    using global::Opc.Ua.WotCon;
    using global::Opc.Ua.WotCon.Bindings;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Opc.Ua.Edge.Translator.WotCon;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// <c>subscribeevent</c> through the driver bridge: events pushed by an
    /// <see cref="IEventingAsset"/> reach the WoT listener, and a driver that cannot
    /// push events reports the missing capability instead of an empty stream.
    /// </summary>
    public sealed class DriverWotEventTests
    {
        private const string EventTd = """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
              "id": "urn:evt-1",
              "name": "evt-1",
              "title": "evt-1",
              "base": "mock://device-1:1502/1",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": ["nosec_sc"],
              "events": {
                "alarm": {
                  "data": { "type": "object", "properties": { "Severity": { "type": "integer" }, "Message": { "type": "string" } } },
                  "forms": [ { "href": "/alarm", "op": ["subscribeevent", "unsubscribeevent"] } ]
                }
              }
            }
            """;

        private static WotCompiledForm PlanEventForm(ProtocolDriverRegistry drivers, out WotProtocolBinderRegistry registry, DriverWotBindingExecutor executor)
        {
            WotEndpointPolicy policy = new() { AllowLoopback = true, AllowPrivateAddresses = true };
            policy.AllowedSchemes.Add(MockProtocolDriver.MockScheme);

            registry = new([new DriverWotBinder(drivers)], [executor], endpointPolicy: policy);
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "/groups/td/resources/evt-1", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(EventTd)));

            Assert.True(plan.FullySupported, string.Join("; ", plan.Diagnostics.Select(d => d.Message)));
            return plan.CompiledForms.Single(f => f.Operation == WoTBindingCapabilityEnum.SubscribeEvent);
        }

        [Fact]
        public async Task Event_form_is_planned_and_a_non_eventing_driver_reports_not_supported()
        {
            ProtocolDriverRegistry drivers = new();
            drivers.Register(new MockProtocolDriver());
            ThingDescriptionCache cache = new();
            cache.Set(EventTd);
            await using DriverWotBindingExecutor executor = new(drivers, cache, () => false);

            WotCompiledForm form = PlanEventForm(drivers, out WotProtocolBinderRegistry registry, executor);
            Assert.True(form.IsExecutable);

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form);
            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.SubscribeEventAsync(_ => { }));
            Assert.Equal(StatusCodes.BadNotSupported, ex.StatusCode);
        }

        [Fact]
        public async Task An_event_only_driver_serves_subscribeevent_without_creating_a_tag()
        {
            // Event-only drivers (such as OPC A&E) refuse to create tags. An event
            // channel needs none, so activation must not ask for one.
            FakeEventingAsset asset = new();
            ProtocolDriverRegistry drivers = new();
            drivers.Register(new EventOnlyDriver(asset));
            ThingDescriptionCache cache = new();
            cache.Set(EventTd);
            await using DriverWotBindingExecutor executor = new(drivers, cache, () => false);

            WotCompiledForm form = PlanEventForm(drivers, out WotProtocolBinderRegistry registry, executor);
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(form);

            List<WotNotification> received = new();
            await using (IWotSubscription subscription = await channel.SubscribeEventAsync(received.Add))
            {
                asset.Raise(new AlarmEvent { Severity = 500, Message = "Door open" });
            }

            Assert.Single(received);
            Assert.Equal(1, asset.Starts);
        }

        /// <summary>A mock-scheme driver whose assets only push events and that cannot create tags.</summary>
        private sealed class EventOnlyDriver : IProtocolDriver
        {
            private readonly IEventingAsset _asset;

            public EventOnlyDriver(IEventingAsset asset) => _asset = asset;

            public string Scheme => MockProtocolDriver.MockScheme;

            public string WoTBindingUri => MockProtocolDriver.MockBindingUri;

            public IEnumerable<string> Discover() => [];

            public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint) => throw new NotSupportedException();

            public Task<AssetConnection> CreateAndConnectAssetAsync(ThingDescription td, CancellationToken cancellationToken = default) =>
                Task.FromResult(new AssetConnection(_asset, 1));

            public AssetTag CreateTag(ThingDescription td, object form, string assetId, byte unitId, string variableId, string mappedUAExpandedNodeId, string mappedUAFieldPath) =>
                throw new NotSupportedException("This driver is event-driven and does not create polling tags.");
        }

        [Fact]
        public async Task Pushed_alarm_reaches_the_listener_as_named_event_data()
        {
            FakeEventingAsset asset = new();
            List<WotNotification> received = new();

            await using (DriverEventSubscription subscription = new(null, asset, received.Add, "evt-1"))
            {
                Assert.Equal(1, asset.Starts);

                asset.Raise(new AlarmEvent { Severity = 700, Message = "Overheating", Active = true, ConditionName = "Temp" });
            }

            WotNotification notification = Assert.Single(received);
            Assert.True(notification.Data.Members.TryGetValue("Severity", out WotEventData severity));
            Assert.Equal(700, severity.Value.WrappedValue.AsBoxedObject());
            Assert.True(notification.Data.Members.TryGetValue("Message", out WotEventData message));
            Assert.Equal("Overheating", message.Value.WrappedValue.AsBoxedObject());
            Assert.Equal(1, asset.Stops);
        }

        [Fact]
        public async Task Driver_subscription_is_shared_across_listeners_and_stopped_by_the_last()
        {
            FakeEventingAsset asset = new();
            int first = 0;
            int second = 0;

            DriverEventSubscription a = new(null, asset, _ => Interlocked.Increment(ref first), "evt-1");
            DriverEventSubscription b = new(null, asset, _ => Interlocked.Increment(ref second), "evt-1");
            Assert.Equal(1, asset.Starts);

            asset.Raise(new AlarmEvent { Message = "one" });
            await a.DisposeAsync();
            Assert.Equal(0, asset.Stops);

            asset.Raise(new AlarmEvent { Message = "two" });
            await b.DisposeAsync();
            await b.DisposeAsync();

            Assert.Equal(1, first);
            Assert.Equal(2, second);
            Assert.Equal(1, asset.Stops);
        }

        [Fact]
        public async Task A_throwing_listener_does_not_break_the_driver_event_pump()
        {
            FakeEventingAsset asset = new();
            await using DriverEventSubscription subscription = new(null, asset, _ => throw new InvalidOperationException("listener"), "evt-1");

            asset.Raise(new AlarmEvent { Message = "ignored" });
        }

        [Fact]
        public async Task A_new_listener_during_the_last_stop_keeps_the_subscription_running()
        {
            // The last old listener is stopping the driver subscription when a new
            // listener arrives. The new start must come after that stop, so the
            // subscription ends up running; a stop that lands last would silently
            // disable events for the live listener.
            BlockingEventingAsset asset = new();
            DriverEventSubscription old = new(null, asset, _ => { }, "evt-race");

            Task stopping = Task.Run(() => old.DisposeAsync().AsTask());
            Assert.True(asset.StopEntered.Wait(TimeSpan.FromSeconds(10)), "stop was not called");

            Task<DriverEventSubscription> starting = Task.Run(() => new DriverEventSubscription(null, asset, _ => { }, "evt-race"));

            // While the stop is still running, the new listener must wait for it.
            Assert.False(starting.Wait(TimeSpan.FromMilliseconds(300)), "the new listener started while the stop was in progress");

            asset.ReleaseStop.Set();
            await stopping;
            await using DriverEventSubscription current = await starting;

            Assert.Equal(new[] { "start", "stop", "start" }, asset.Calls);
            Assert.True(asset.Running);
        }

        private sealed class BlockingEventingAsset : IEventingAsset
        {
            public ManualResetEventSlim StopEntered { get; } = new();

            public ManualResetEventSlim ReleaseStop { get; } = new();

            public List<string> Calls { get; } = new();

            public bool Running { get; private set; }

#pragma warning disable CS0067 // never raised: this fake only tracks subscription calls
            public event EventHandler<AlarmEvent> AlarmReceived;
#pragma warning restore CS0067

            public bool IsConnected => true;

            public void StartEventSubscription()
            {
                lock (Calls)
                {
                    Calls.Add("start");
                }

                Running = true;
            }

            public void RefreshEventSubscription()
            {
            }

            public void StopEventSubscription()
            {
                StopEntered.Set();
                ReleaseStop.Wait(TimeSpan.FromSeconds(10));
                lock (Calls)
                {
                    Calls.Add("stop");
                }

                Running = false;
            }

            public string GetRemoteEndpoint() => "fake://race";

            public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default) => Task.FromResult<object>(null);

            public Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default) =>
                Task.FromResult(AssetActionResult.FromStatus("ok"));

            public ValueTask DisposeAsync() => default;
        }

        private sealed class FakeEventingAsset : IEventingAsset
        {
            public event EventHandler<AlarmEvent> AlarmReceived;

            public int Starts { get; private set; }

            public int Stops { get; private set; }

            public bool IsConnected => true;

            public void Raise(AlarmEvent alarm) => AlarmReceived?.Invoke(this, alarm);

            public void StartEventSubscription() => Starts++;

            public void RefreshEventSubscription()
            {
            }

            public void StopEventSubscription() => Stops++;

            public string GetRemoteEndpoint() => "fake://evt";

            public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default) => Task.FromResult<object>(null);

            public Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default) =>
                Task.FromResult(AssetActionResult.FromStatus("ok"));

            public ValueTask DisposeAsync() => default;
        }
    }
}
