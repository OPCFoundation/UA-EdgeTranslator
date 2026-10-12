namespace Opc.Ua.Edge.Translator.Tests.WotCon
{
    using global::Opc.Ua.WotCon;
    using global::Opc.Ua.WotCon.Bindings;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Opc.Ua.Edge.Translator.WotCon;
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
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

        private static (WotProtocolBinderRegistry Registry, WotBindingPlan Plan, DriverWotBindingExecutor Executor) Plan(bool provisioning = false)
        {
            ProtocolDriverRegistry drivers = new();
            drivers.Register(new MockProtocolDriver());

            ThingDescriptionCache cache = new();
            cache.Set(Td);

            DriverWotBindingExecutor executor = new(drivers, cache, () => provisioning);
            WotEndpointPolicy policy = new() { AllowLoopback = true, AllowPrivateAddresses = true };
            policy.AllowedSchemes.Add(MockProtocolDriver.MockScheme);

            WotProtocolBinderRegistry registry = new([new DriverWotBinder(drivers)], [executor], endpointPolicy: policy);
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "/groups/td/resources/mock-1", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(Td)));

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
