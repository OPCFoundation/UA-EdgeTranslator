namespace Opc.Ua.Edge.Translator.Tests
{
    using global::Opc.Ua.WotCon.Bindings;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.WotCon;
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// The WoT-Con binding channel every planned form reads, writes and observes
    /// through: provisioning mode, reconnects after a lost connection, driver
    /// exceptions, structure values without a target type, and channel release.
    /// </summary>
    public sealed class DriverWotBindingChannelTests
    {
        private sealed class StubAsset : IAsset
        {
            public bool IsConnected { get; set; } = true;

            public Func<object> Read { get; set; } = () => 42;

            public Exception WriteFailure { get; set; }

            public object Written;

            public int Reads;

            public int Connects;

            public string GetRemoteEndpoint() => "10.0.0.5:102";

            public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Connects);
                return Task.CompletedTask;
            }

            public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Reads);
                return Task.FromResult(Read());
            }

            public Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default)
            {
                if (WriteFailure != null)
                {
                    throw WriteFailure;
                }

                Written = value;
                return Task.CompletedTask;
            }

            public Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public ValueTask DisposeAsync() => default;
        }

        private static IWotBindingChannel NewChannel(IAsset asset, bool blocked = false, Func<ValueTask> release = null)
        {
            Type channelType = typeof(UANodeManager).Assembly.GetType("Opc.Ua.Edge.Translator.WotCon.DriverWotBindingChannel", throwOnError: true);
            AssetTag tag = new() { Name = "speed", Address = "40001", PollingInterval = 250 };
            Func<bool> isBlocked = () => blocked;

            return (IWotBindingChannel)Activator.CreateInstance(
                channelType,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                args: new object[] { null, asset, tag, "plc", isBlocked, release },
                culture: null);
        }

        [Fact]
        public async Task Provisioning_mode_blocks_reads_writes_and_invokes_without_touching_the_asset()
        {
            StubAsset asset = new();
            IWotBindingChannel channel = NewChannel(asset, blocked: true);

            WotReadResult read = await channel.ReadAsync();
            WotWriteResult write = await channel.WriteAsync(new DataValue(new Variant(1)));
            WotInvokeResult invoke = await channel.InvokeAsync(Array.Empty<Variant>());

            Assert.Equal(StatusCodes.BadNotReadable, read.Status);
            Assert.Equal(StatusCodes.BadNotWritable, write.Status);
            Assert.Equal(StatusCodes.BadUserAccessDenied, invoke.Status);
            Assert.Equal(0, asset.Reads);
            Assert.Null(asset.Written);
        }

        [Fact]
        public async Task A_scalar_read_returns_the_driver_value()
        {
            IWotBindingChannel channel = NewChannel(new StubAsset { Read = () => 42 });

            WotReadResult result = await channel.ReadAsync();

            Assert.True(result.Success);
            Assert.Equal(StatusCodes.Good, result.Status);
            Assert.Equal(42, result.Value.WrappedValue.ClrValue());
        }

        [Fact]
        public async Task A_duration_is_read_as_milliseconds()
        {
            IWotBindingChannel channel = NewChannel(new StubAsset { Read = () => TimeSpan.FromSeconds(1.5) });

            WotReadResult result = await channel.ReadAsync();

            Assert.Equal(1500.0, result.Value.WrappedValue.ClrValue());
        }

        [Fact]
        public async Task A_structure_without_a_target_type_is_a_type_mismatch_not_a_silent_failure()
        {
            Dictionary<string, object> fields = new() { ["Running"] = true, ["Speed"] = 1450.5f };

            WotReadResult single = await NewChannel(new StubAsset { Read = () => fields }).ReadAsync();
            WotReadResult array = await NewChannel(new StubAsset { Read = () => new[] { fields, fields } }).ReadAsync();

            Assert.Equal(StatusCodes.BadTypeMismatch, single.Status);
            Assert.Equal(StatusCodes.BadTypeMismatch, array.Status);
        }

        [Fact]
        public async Task A_lost_connection_reports_unavailable_data_and_reconnects()
        {
            StubAsset asset = new();
            asset.Read = () =>
            {
                asset.IsConnected = false;
                return null;
            };

            WotReadResult result = await NewChannel(asset).ReadAsync();

            Assert.Equal(StatusCodes.BadDataUnavailable, result.Status);
            Assert.Equal(1, asset.Connects);
            WotAssetReconnector.Forget(asset);
        }

        [Fact]
        public async Task A_driver_exception_on_read_reports_unavailable_data()
        {
            StubAsset asset = new() { Read = () => throw new InvalidOperationException("socket closed") };

            WotReadResult result = await NewChannel(asset).ReadAsync();

            Assert.Equal(StatusCodes.BadDataUnavailable, result.Status);
            WotAssetReconnector.Forget(asset);
        }

        [Fact]
        public async Task A_write_passes_the_clr_value_to_the_driver()
        {
            StubAsset asset = new();

            WotWriteResult result = await NewChannel(asset).WriteAsync(new DataValue(new Variant(1234)));

            Assert.Equal(StatusCodes.Good, result.Status);
            Assert.Equal(1234, asset.Written);
        }

        [Fact]
        public async Task A_driver_exception_on_write_reports_unavailable_data()
        {
            StubAsset asset = new() { WriteFailure = new InvalidOperationException("rejected") };

            WotWriteResult result = await NewChannel(asset).WriteAsync(new DataValue(new Variant(1)));

            Assert.Equal(StatusCodes.BadDataUnavailable, result.Status);
            WotAssetReconnector.Forget(asset);
        }

        [Fact]
        public async Task Subscribing_to_events_needs_an_eventing_driver()
        {
            IWotBindingChannel channel = NewChannel(new StubAsset());

            ServiceResultException ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.SubscribeEventAsync(_ => { }));

            Assert.Equal(StatusCodes.BadNotSupported, ex.StatusCode);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await channel.ObserveAsync(null));
        }

        [Fact]
        public async Task Disposing_releases_the_shared_asset_exactly_once()
        {
            int releases = 0;
            IWotBindingChannel channel = NewChannel(new StubAsset(), release: () =>
            {
                releases++;
                return default;
            });

            await channel.DisposeAsync();
            await channel.DisposeAsync();

            Assert.Equal(1, releases);
        }
    }

    /// <summary>
    /// <see cref="UANodeManager"/> helpers that the structured driver values rely on:
    /// array members of a structure and the host of an asset endpoint.
    /// </summary>
    public sealed class UANodeManagerStructureArrayTests
    {
        private static UANodeManager NewBareInstance()
        {
            PropertyInfo telemetry = typeof(Program).GetProperty(nameof(Program.Telemetry), BindingFlags.Public | BindingFlags.Static);
            if (telemetry.GetValue(null) == null)
            {
                telemetry.SetValue(null, new Opc.Ua.Cloud.ConsoleTelemetry());
            }

            return (UANodeManager)RuntimeHelpers.GetUninitializedObject(typeof(UANodeManager));
        }

        private static byte[] Encode(StructureField field, object value)
        {
            MethodInfo encodeField = typeof(UANodeManager).GetMethod("EncodeField", BindingFlags.NonPublic | BindingFlags.Instance);
            using BinaryEncoder encoder = new(ServiceMessageContext.Create(Program.Telemetry));
            encodeField.Invoke(NewBareInstance(), new object[] { encoder, field, value });
            return encoder.CloseAndReturnBuffer();
        }

        [Fact]
        public void An_array_member_is_encoded_as_a_length_followed_by_its_elements()
        {
            StructureField field = new()
            {
                Name = "Steps",
                DataType = new NodeId(DataTypes.Int16, 0),
                ValueRank = ValueRanks.OneDimension
            };

            byte[] body = Encode(field, new short[] { 10, -11, 12 });

            using BinaryDecoder decoder = new(body, ServiceMessageContext.Create(Program.Telemetry));
            Assert.Equal(3, decoder.ReadInt32(null));
            Assert.Equal(10, decoder.ReadInt16(null));
            Assert.Equal(-11, decoder.ReadInt16(null));
            Assert.Equal(12, decoder.ReadInt16(null));
        }

        [Fact]
        public void A_missing_array_member_is_encoded_as_an_empty_array()
        {
            StructureField field = new()
            {
                Name = "Steps",
                DataType = new NodeId(DataTypes.Double, 0),
                ValueRank = ValueRanks.OneDimension
            };

            byte[] body = Encode(field, null);

            using BinaryDecoder decoder = new(body, ServiceMessageContext.Create(Program.Telemetry));
            Assert.Equal(0, decoder.ReadInt32(null));
        }

        [Fact]
        public void A_string_array_member_keeps_its_elements()
        {
            StructureField field = new()
            {
                Name = "Names",
                DataType = new NodeId(DataTypes.String, 0),
                ValueRank = ValueRanks.OneDimension
            };

            byte[] body = Encode(field, new List<object> { "a", "bc" });

            using BinaryDecoder decoder = new(body, ServiceMessageContext.Create(Program.Telemetry));
            Assert.Equal(2, decoder.ReadInt32(null));
            Assert.Equal("a", decoder.ReadString(null));
            Assert.Equal("bc", decoder.ReadString(null));
        }

        [Fact]
        public void Structure_arrays_are_recognized_and_rejected_when_not_structures()
        {
            UANodeManager nm = NewBareInstance();

            Assert.True(UANodeManager.IsStructureArray(new[] { new Dictionary<string, object>() }));
            Assert.False(UANodeManager.IsStructureArray(new[] { 1, 2 }));
            Assert.False(nm.TryEncodeStructureArray(new NodeId(1u, 2), null, out _));
            Assert.False(nm.TryEncodeStructureArray(new NodeId(1u, 2), new object[] { 1 }, out _));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("  ", "")]
        [InlineData("opc.tcp://server:4840/path", "server")]
        [InlineData("s7://192.168.0.10/0,1/", "192.168.0.10")]
        [InlineData("modbus://[fe80::1]:502", "fe80::1")]
        [InlineData("[fe80::1]:502", "fe80::1")]
        [InlineData("192.168.1.2:502", "192.168.1.2")]
        [InlineData("plc/route", "plc")]
        [InlineData("fe80::1", "fe80::1")]
        [InlineData("plc", "plc")]
        public void The_host_of_an_asset_endpoint_is_extracted(string endpoint, string host)
        {
            Assert.Equal(host, UANodeManager.GetEndpointHost(endpoint));
        }
    }
}
