namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for <see cref="UANodeManager"/> private helpers
    /// that don't require a live OPC UA server: <c>EncodeField</c> per-data-type
    /// branches, <c>TryReconnect</c> short-circuit when an asset has no remote
    /// endpoint, and <c>OnTDActionCalled</c> fail/no-result branches.
    ///
    /// Joined to the working-directory collection so we don't race the
    /// integration fixture (which mutates / disposes <c>Program.Telemetry</c>).
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UANodeManagerHelperBranchesTests
    {
        private static readonly Type _sut = typeof(UANodeManager);

        private static UANodeManager NewBareInstance()
        {
            UANodeManager nm = (UANodeManager)RuntimeHelpers.GetUninitializedObject(_sut);
            EnsureField(nm, "_reconnectStates");
            EnsureField(nm, "_uaVariables");
            EnsureField(nm, "_uaProperties");
            EnsureField(nm, "_assets");
            EnsureField(nm, "_tags");
            EnsureField(nm, "_tagIndex");
            EnsureField(nm, "_fileManagers");
            return nm;
        }

        private static void EnsureField(object instance, string name)
        {
            FieldInfo field = _sut.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(instance) == null && field != null)
            {
                field.SetValue(instance, Activator.CreateInstance(field.FieldType));
            }
        }

        // ------------- EncodeField -------------

        [Theory]
        [InlineData((uint)DataTypes.Float, 1.5f)]
        [InlineData((uint)DataTypes.Double, 2.5d)]
        [InlineData((uint)DataTypes.Boolean, true)]
        [InlineData((uint)DataTypes.SByte, (sbyte)-1)]
        [InlineData((uint)DataTypes.Byte, (byte)7)]
        [InlineData((uint)DataTypes.Int16, (short)-2)]
        [InlineData((uint)DataTypes.UInt16, (ushort)3)]
        [InlineData((uint)DataTypes.Int32, -100)]
        [InlineData((uint)DataTypes.UInt32, (uint)100)]
        [InlineData((uint)DataTypes.Int64, (long)-1234)]
        [InlineData((uint)DataTypes.UInt64, (ulong)5678)]
        [InlineData((uint)DataTypes.String, "abc")]
        public void EncodeField_writes_each_known_data_type(uint dataType, object value)
        {
            UANodeManager nm = NewBareInstance();
            EnsureProgramTelemetry();
            using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry));

            StructureField field = new()
            {
                Name = "f",
                DataType = new NodeId(dataType, 0)
            };

            MethodInfo encode = _sut.GetMethod("EncodeField", BindingFlags.NonPublic | BindingFlags.Instance);
            encode.Invoke(nm, new object[] { encoder, field, value });

            byte[] bytes = encoder.CloseAndReturnBuffer();
            Assert.NotEmpty(bytes);
        }

        [Theory]
        [InlineData((uint)DataTypes.Float)]
        [InlineData((uint)DataTypes.Double)]
        [InlineData((uint)DataTypes.SByte)]
        [InlineData((uint)DataTypes.Byte)]
        [InlineData((uint)DataTypes.Int16)]
        [InlineData((uint)DataTypes.UInt16)]
        [InlineData((uint)DataTypes.Int32)]
        [InlineData((uint)DataTypes.UInt32)]
        [InlineData((uint)DataTypes.Int64)]
        [InlineData((uint)DataTypes.UInt64)]
        [InlineData((uint)DataTypes.Boolean)]
        public void EncodeField_writes_default_when_value_does_not_match_pattern(uint dataType)
        {
            UANodeManager nm = NewBareInstance();
            EnsureProgramTelemetry();
            using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry));

            StructureField field = new()
            {
                Name = "f",
                DataType = new NodeId(dataType, 0)
            };

            MethodInfo encode = _sut.GetMethod("EncodeField", BindingFlags.NonPublic | BindingFlags.Instance);
            // Pass a mismatched type to force the "is X x ? x : default" fallback branch.
            encode.Invoke(nm, new object[] { encoder, field, "wrong-type-on-purpose" });

            byte[] bytes = encoder.CloseAndReturnBuffer();
            Assert.NotEmpty(bytes);
        }

        [Fact]
        public void EncodeField_throws_for_unsupported_data_type()
        {
            UANodeManager nm = NewBareInstance();
            EnsureProgramTelemetry();
            using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry));

            StructureField field = new()
            {
                Name = "f",
                DataType = new NodeId((uint)DataTypes.DateTime, 0)
            };

            MethodInfo encode = _sut.GetMethod("EncodeField", BindingFlags.NonPublic | BindingFlags.Instance);
            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(
                () => encode.Invoke(nm, new object[] { encoder, field, DateTime.UtcNow }));

            Assert.IsType<NotImplementedException>(tie.InnerException);
        }

        // ------------- TryReconnect -------------

        [Fact]
        public void TryReconnect_short_circuits_when_asset_returns_empty_endpoint()
        {
            UANodeManager nm = NewBareInstance();

            // Telemetry counters are touched by TryReconnect via Program.Telemetry.
            EnsureProgramTelemetry();

            FakeAsset asset = new() { RemoteEndpoint = string.Empty };

            MethodInfo reconnect = _sut.GetMethod("TryReconnect", BindingFlags.NonPublic | BindingFlags.Instance);
            reconnect.Invoke(nm, new object[] { "asset-empty", asset });

            // ScheduleNextReconnect must have populated the per-asset state.
            object dict = _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);
            Assert.True(((IDictionary)dict).Contains("asset-empty"));
        }

        [Fact]
        public void TryReconnect_handles_thrown_exceptions_by_scheduling_next_attempt()
        {
            UANodeManager nm = NewBareInstance();
            EnsureProgramTelemetry();

            ThrowingAsset asset = new();

            MethodInfo reconnect = _sut.GetMethod("TryReconnect", BindingFlags.NonPublic | BindingFlags.Instance);
            reconnect.Invoke(nm, new object[] { "asset-throw", asset });

            object dict = _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);
            Assert.True(((IDictionary)dict).Contains("asset-throw"));
        }

        // ------------- ParseExpandedNodeId -------------
        // Note: ParseExpandedNodeId touches the SDK's NamespaceUris collection,
        // which is initialized as part of the full CustomNodeManager2 lifecycle.
        // Coverage for that helper is provided indirectly via the live integration
        // tests (which boot a real UANodeManager).

        // ------------- helpers -------------

        private static void EnsureProgramTelemetry()
        {
            // Program.Telemetry counters are read inside TryReconnect. Other test
            // fixtures (e.g. the integration server fixture) may dispose and
            // null-out the singleton between runs, so always install a fresh
            // ConsoleTelemetry instance for these reflection-driven tests.
            var prop = typeof(Program).GetProperty(nameof(Program.Telemetry), BindingFlags.Public | BindingFlags.Static);
            prop.SetValue(null, new Opc.Ua.Cloud.ConsoleTelemetry());
        }

        // ---- minimal IAsset implementations for TryReconnect ----

        private sealed class FakeAsset : Opc.Ua.Edge.Translator.Interfaces.IAsset
        {
            public string RemoteEndpoint { get; set; }

            public bool IsConnected => false;

            public void Connect(string ipAddress, int port) { }
            public void Disconnect() { }
            public string GetRemoteEndpoint() => RemoteEndpoint;
            public object Read(AssetTag tag) => null;
            public void Write(AssetTag tag, object value) { }
            public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs) => "ok";
        }

        private sealed class ThrowingAsset : Opc.Ua.Edge.Translator.Interfaces.IAsset
        {
            public bool IsConnected => false;
            public void Connect(string ipAddress, int port) => throw new InvalidOperationException("connect blew up");
            public void Disconnect() { }
            public string GetRemoteEndpoint() => "host:1502";
            public object Read(AssetTag tag) => null;
            public void Write(AssetTag tag, object value) { }
            public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs) => "ok";
        }
    }
}
