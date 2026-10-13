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
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Exercises the WoT.con registry data plane end to end without a server:
    /// the package's binder registry plans a Thing Description with our binder,
    /// opens channels through our executor, and the Mock driver performs the I/O.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class DriverWotBindingTests : IDisposable
    {
        private const string BaseUri = "mock://device-1:1502/1";

        private const string Td = """
            {
              "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
              "id": "urn:mock-1",
              "name": "mock-1",
              "title": "mock-1",
              "base": "mock://device-1:1502/1",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": ["nosec_sc"],
              "properties": {
                "setpoint": {
                  "type": "number",
                  "forms": [ { "href": "/setpoint", "op": ["readproperty", "writeproperty"], "type": "xsd:float" } ]
                }
              },
              "actions": {
                "reset": { "forms": [ { "href": "/reset", "op": ["invokeaction"] } ] }
              }
            }
            """;

        private readonly string _previousDirectory;

        private readonly string _previousIgnore;

        private readonly string _workingDirectory;

        public DriverWotBindingTests()
        {
            _previousDirectory = Directory.GetCurrentDirectory();
            _previousIgnore = Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE");
            _workingDirectory = Path.Combine(Path.GetTempPath(), "uaedge-wotcon-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workingDirectory);
            Directory.SetCurrentDirectory(_workingDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_previousDirectory);
            Environment.SetEnvironmentVariable("IGNORE_PROVISIONING_MODE", _previousIgnore);
            try
            {
                Directory.Delete(_workingDirectory, true);
            }
            catch (IOException)
            {
            }
        }

        private static (WotProtocolBinderRegistry Registry, WotBindingPlan Plan, DriverWotBindingExecutor Executor) Plan(bool provisioning = false, string td = Td, IProtocolDriver driver = null)
        {
            ProtocolDriverRegistry drivers = new();
            drivers.Register(driver ?? new MockProtocolDriver());

            ThingDescriptionCache cache = new();
            cache.Set(td);

            DriverWotBindingExecutor executor = new(drivers, cache, () => provisioning);
            WotEndpointPolicy policy = new() { AllowLoopback = true, AllowPrivateAddresses = true };
            policy.AllowedSchemes.Add(MockProtocolDriver.MockScheme);

            WotProtocolBinderRegistry registry = new([new DriverWotBinder(drivers)], [executor], endpointPolicy: policy);
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "/groups/td/resources/mock-1", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));

            return (registry, plan, executor);
        }

        [Fact]
        public void Relative_forms_are_planned_against_the_td_base_and_are_executable()
        {
            (_, WotBindingPlan plan, DriverWotBindingExecutor executor) = Plan();

            Assert.True(plan.FullySupported, string.Join("; ", plan.Diagnostics.Select(d => d.Message)));
            Assert.All(plan.CompiledForms, form =>
            {
                Assert.Equal(DriverWotBinder.BindingId, form.Binding.Id);
                Assert.Equal(BaseUri, form.Endpoint.BaseUri);
                Assert.True(form.IsExecutable);
            });
            Assert.Contains(plan.CompiledForms, f => f.AffordanceName == "setpoint" && f.Operation == WoTBindingCapabilityEnum.WriteProperty);
            Assert.Contains(plan.CompiledForms, f => f.AffordanceName == "reset" && f.Operation == WoTBindingCapabilityEnum.InvokeAction);
            Assert.True(executor.CanExecute(plan.CompiledForms[0]));
        }

        [Fact]
        public void Absolute_forms_keep_the_td_base_as_the_connection_identity()
        {
            // As in Samples/BACNet.td.jsonld: the base names the device, while each
            // absolute href names a point on it. All forms must share the base as their
            // endpoint, so they reuse one connection and find the cached TD.
            const string absoluteTd = """
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1", { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }],
                  "@type": ["Thing", "uav:object"],
                  "id": "urn:abs-1",
                  "title": "abs-1",
                  "base": "mock://device-1:1502/1",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": ["nosec_sc"],
                  "properties": {
                    "a": { "type": "number", "forms": [ { "href": "mock://1/a", "op": ["readproperty"] } ] },
                    "b": { "type": "number", "forms": [ { "href": "mock://1/b", "op": ["readproperty"] } ] }
                  }
                }
                """;

            (_, WotBindingPlan plan, _) = Plan(td: absoluteTd);

            Assert.True(plan.FullySupported, string.Join("; ", plan.Diagnostics.Select(d => d.Message)));
            Assert.Equal(2, plan.CompiledForms.Length);
            Assert.All(plan.CompiledForms, form => Assert.Equal(BaseUri, form.Endpoint.BaseUri));
            Assert.Contains(plan.CompiledForms, form => form.Addressing.Target == "mock://1/a");
        }

        [Fact]
        public async Task The_last_channel_to_close_disconnects_the_shared_asset()
        {
            CountingDriver driver = new();
            (WotProtocolBinderRegistry registry, WotBindingPlan plan, DriverWotBindingExecutor executor) = Plan(driver: driver);
            await using DriverWotBindingExecutor ownedExecutor = executor;

            WotCompiledForm write = plan.CompiledForms.Single(f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);
            WotCompiledForm invoke = plan.CompiledForms.Single(f => f.Operation == WoTBindingCapabilityEnum.InvokeAction);

            IWotBindingChannel first = await registry.OpenChannelAsync(write);
            IWotBindingChannel second = await registry.OpenChannelAsync(invoke);
            Assert.Equal(1, driver.Connects);

            await first.DisposeAsync();
            Assert.Equal(0, driver.Disposals);

            await second.DisposeAsync();
            Assert.Equal(1, driver.Disposals);

            // A channel opened afterwards connects again rather than reusing the closed asset.
            await using IWotBindingChannel third = await registry.OpenChannelAsync(write);
            Assert.Equal(2, driver.Connects);
        }

        [Fact]
        public async Task A_failed_connect_is_retried_by_the_next_activation()
        {
            CountingDriver driver = new() { FailNextConnect = true };
            (WotProtocolBinderRegistry registry, WotBindingPlan plan, DriverWotBindingExecutor executor) = Plan(driver: driver);
            await using DriverWotBindingExecutor ownedExecutor = executor;

            WotCompiledForm write = plan.CompiledForms.Single(f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);

            await Assert.ThrowsAnyAsync<Exception>(async () => await registry.OpenChannelAsync(write));

            await using IWotBindingChannel channel = await registry.OpenChannelAsync(write);
            WotWriteResult written = await channel.WriteAsync(new DataValue(new Variant(1.5)));
            Assert.True(written.Success, $"write: {written.Status} {written.Error}");
            Assert.Equal(2, driver.Connects);
        }

        [Fact]
        public async Task A_canceled_activation_stops_waiting_for_a_hung_connect()
        {
            CountingDriver driver = new() { HangConnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            (WotProtocolBinderRegistry registry, WotBindingPlan plan, DriverWotBindingExecutor executor) = Plan(driver: driver);
            await using DriverWotBindingExecutor ownedExecutor = executor;

            WotCompiledForm write = plan.CompiledForms.Single(f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);
            using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(200));

            Task activation = registry.OpenChannelAsync(write, cts.Token).AsTask();
            Task finished = await Task.WhenAny(activation, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.Same(activation, finished);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activation);

            // Once the hung connect completes, its abandoned connection is disposed.
            driver.HangConnect.SetResult();
            for (int i = 0; i < 100 && Volatile.Read(ref driver.Disposals) == 0; i++)
            {
                await Task.Delay(20);
            }

            Assert.Equal(1, Volatile.Read(ref driver.Disposals));
        }

        /// <summary>
        /// The mock driver, counting connects and the disposal of the connected assets,
        /// optionally failing the first connect.
        /// </summary>
        private sealed class CountingDriver : IProtocolDriver
        {
            private readonly MockProtocolDriver _inner = new();

            public bool FailNextConnect { get; set; }

            public TaskCompletionSource HangConnect { get; set; }

            public int Connects;

            public int Disposals;

            public string Scheme => _inner.Scheme;

            public string WoTBindingUri => _inner.WoTBindingUri;

            public IEnumerable<string> Discover() => _inner.Discover();

            public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint) => _inner.BrowseAndGenerateTD(assetName, assetEndpoint);

            public async Task<AssetConnection> CreateAndConnectAssetAsync(ThingDescription td, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Connects);
                if (HangConnect != null)
                {
                    // Ignore the token, like a driver stuck in a blocking connect.
                    await HangConnect.Task;
                }

                if (FailNextConnect)
                {
                    FailNextConnect = false;

                    // Fail asynchronously, as a real network connect does: the task is
                    // still running when the executor caches it, and faults later.
                    await Task.Delay(50, cancellationToken);
                    throw new InvalidOperationException("device unreachable");
                }

                AssetConnection connection = await _inner.CreateAndConnectAssetAsync(td, cancellationToken);
                return new AssetConnection(new DisposalCountingAsset(connection.Asset, this), connection.UnitId);
            }

            public AssetTag CreateTag(ThingDescription td, object form, string assetId, byte unitId, string variableId, string mappedUAExpandedNodeId, string mappedUAFieldPath) =>
                _inner.CreateTag(td, form, assetId, unitId, variableId, mappedUAExpandedNodeId, mappedUAFieldPath);

            private sealed class DisposalCountingAsset : IAsset
            {
                private readonly IAsset _inner;

                private readonly CountingDriver _owner;

                public DisposalCountingAsset(IAsset inner, CountingDriver owner)
                {
                    _inner = inner;
                    _owner = owner;
                }

                public bool IsConnected => _inner.IsConnected;

                public string GetRemoteEndpoint() => _inner.GetRemoteEndpoint();

                public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default) => _inner.ConnectAsync(ipAddress, port, cancellationToken);

                public Task DisconnectAsync(CancellationToken cancellationToken = default) => _inner.DisconnectAsync(cancellationToken);

                public Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default) => _inner.ReadAsync(tag, cancellationToken);

                public Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default) => _inner.WriteAsync(tag, value, cancellationToken);

                public Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default) =>
                    _inner.ExecuteActionAsync(method, inputArgs, cancellationToken);

                public async ValueTask DisposeAsync()
                {
                    Interlocked.Increment(ref _owner.Disposals);
                    await _inner.DisposeAsync();
                }
            }
        }

        [Fact]
        public async Task Write_read_and_invoke_reach_the_driver()
        {
            (WotProtocolBinderRegistry registry, WotBindingPlan plan, DriverWotBindingExecutor executor) = Plan();
            await using DriverWotBindingExecutor ownedExecutor = executor;

            WotCompiledForm write = plan.CompiledForms.Single(f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(write);

            WotWriteResult written = await channel.WriteAsync(new DataValue(new Variant(42.5)));
            Assert.True(written.Success, $"write: {written.Status} {written.Error}");

            WotReadResult read = await channel.ReadAsync();
            Assert.True(read.Success, $"read: {read.Status} {read.Error}");
            Assert.Equal(42.5, read.Value.WrappedValue.AsBoxedObject());

            // Mock reports "mock:reset:ok", which is not "ok"/"success", so the
            // channel surfaces it as a Bad result carrying the driver's text.
            WotCompiledForm invoke = plan.CompiledForms.Single(f => f.Operation == WoTBindingCapabilityEnum.InvokeAction);
            await using IWotBindingChannel action = await registry.OpenChannelAsync(invoke);
            WotInvokeResult invoked = await action.InvokeAsync([]);
            Assert.True(StatusCode.IsBad(invoked.Status));
            Assert.Equal("mock:reset:ok", invoked.Error);
        }

        [Fact]
        public async Task Provisioning_mode_blocks_reads_and_writes()
        {
            (WotProtocolBinderRegistry registry, WotBindingPlan plan, DriverWotBindingExecutor executor) = Plan(provisioning: true);
            await using DriverWotBindingExecutor ownedExecutor = executor;

            WotCompiledForm write = plan.CompiledForms.Single(f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);
            await using IWotBindingChannel channel = await registry.OpenChannelAsync(write);

            WotWriteResult written = await channel.WriteAsync(new DataValue(new Variant(1.0)));
            WotReadResult read = await channel.ReadAsync();

            Assert.True(written.Status == StatusCodes.BadNotWritable);
            Assert.True(read.Status == StatusCodes.BadNotReadable);
        }
    }
}
