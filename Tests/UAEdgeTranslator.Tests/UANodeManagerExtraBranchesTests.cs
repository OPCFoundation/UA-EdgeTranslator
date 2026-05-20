namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for additional <see cref="UANodeManager"/>
    /// helpers that don't need a fully wired OPC UA stack: <c>EncodeField</c>
    /// (every supported scalar branch + the unsupported-type throw),
    /// <c>ParseExpandedNodeId</c> (every parsing branch), the reconnect-state
    /// helpers, and the <c>TryReconnect</c> success / failure / empty-endpoint
    /// branches via a stubbed <see cref="IAsset"/>.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UANodeManagerExtraBranchesTests
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
            EnsureProgramTelemetry();
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

        private static void EnsureProgramTelemetry()
        {
            PropertyInfo prop = typeof(Program).GetProperty(nameof(Program.Telemetry), BindingFlags.Public | BindingFlags.Static);
            if (prop.GetValue(null) == null)
            {
                prop.SetValue(null, new Opc.Ua.Cloud.ConsoleTelemetry());
            }
        }

        // ---------------- EncodeField ----------------

        [Theory]
        [InlineData(DataTypes.Float, 1.5f)]
        [InlineData(DataTypes.Double, 2.5)]
        [InlineData(DataTypes.Boolean, true)]
        [InlineData(DataTypes.SByte, (sbyte)-3)]
        [InlineData(DataTypes.Byte, (byte)4)]
        [InlineData(DataTypes.Int16, (short)-5)]
        [InlineData(DataTypes.UInt16, (ushort)6)]
        [InlineData(DataTypes.Int32, -7)]
        [InlineData(DataTypes.UInt32, 8u)]
        [InlineData(DataTypes.Int64, -9L)]
        [InlineData(DataTypes.UInt64, 10ul)]
        [InlineData(DataTypes.String, "encoded")]
        public void EncodeField_writes_every_supported_scalar_branch(uint dataTypeId, object value)
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo encodeField = _sut.GetMethod("EncodeField", BindingFlags.NonPublic | BindingFlags.Instance);

            using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry));
            StructureField field = new()
            {
                Name = "field",
                DataType = new NodeId(dataTypeId, 0)
            };

            // Should not throw for any of the supported branches.
            encodeField.Invoke(nm, new object[] { encoder, field, value });

            byte[] buffer = encoder.CloseAndReturnBuffer();
            Assert.NotNull(buffer);
            Assert.NotEmpty(buffer);
        }

        [Theory]
        [InlineData(DataTypes.Float, null)]
        [InlineData(DataTypes.Double, null)]
        [InlineData(DataTypes.Boolean, null)]
        [InlineData(DataTypes.SByte, null)]
        [InlineData(DataTypes.Byte, null)]
        [InlineData(DataTypes.Int16, null)]
        [InlineData(DataTypes.UInt16, null)]
        [InlineData(DataTypes.Int32, null)]
        [InlineData(DataTypes.UInt32, null)]
        [InlineData(DataTypes.Int64, null)]
        [InlineData(DataTypes.UInt64, null)]
        [InlineData(DataTypes.String, null)]
        public void EncodeField_writes_default_value_when_value_is_null(uint dataTypeId, object value)
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo encodeField = _sut.GetMethod("EncodeField", BindingFlags.NonPublic | BindingFlags.Instance);

            using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry));
            StructureField field = new()
            {
                Name = "field",
                DataType = new NodeId(dataTypeId, 0)
            };

            // Should not throw — every branch falls back to a default value.
            encodeField.Invoke(nm, new object[] { encoder, field, value });
        }

        [Fact]
        public void EncodeField_throws_for_unsupported_data_type()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo encodeField = _sut.GetMethod("EncodeField", BindingFlags.NonPublic | BindingFlags.Instance);

            using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry));
            StructureField field = new()
            {
                Name = "field",
                // DateTime isn't included in the EncodeField switch, so it must
                // raise a NotImplementedException.
                DataType = new NodeId((uint)DataTypes.DateTime, 0)
            };

            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(
                () => encodeField.Invoke(nm, new object[] { encoder, field, DateTime.UtcNow }));
            Assert.IsType<NotImplementedException>(tie.InnerException);
        }

        // ---------------- ParseExpandedNodeId ----------------

        [Fact]
        public void ParseExpandedNodeId_returns_null_for_null_or_empty_input()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo parse = _sut.GetMethod("ParseExpandedNodeId", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.Null(parse.Invoke(nm, new object[] { null }));
            Assert.Null(parse.Invoke(nm, new object[] { string.Empty }));
        }

        [Fact]
        public void ParseExpandedNodeId_returns_null_when_format_is_unrecognized()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo parse = _sut.GetMethod("ParseExpandedNodeId", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.Null(parse.Invoke(nm, new object[] { "not-a-node-id" }));
            Assert.Null(parse.Invoke(nm, new object[] { "ns=2;i=42" }));
            Assert.Null(parse.Invoke(nm, new object[] { "nsu=http://x/;g=guidish" }));
        }

        // ---------------- Reconnect helpers ----------------

        [Fact]
        public void IsReconnectAttemptDue_returns_true_when_state_missing()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("IsReconnectAttemptDue", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.True((bool)m.Invoke(nm, new object[] { "missing" }));
        }

        [Fact]
        public void ResetReconnectState_removes_existing_state()
        {
            UANodeManager nm = NewBareInstance();
            FieldInfo statesField = _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance);
            object map = statesField.GetValue(nm);

            // Add a state via the dictionary's set_Item indexer.
            Type stateType = _sut.Assembly.GetType("Opc.Ua.Edge.Translator.UANodeManager+ReconnectState", throwOnError: false)
                ?? GetReconnectStateType();
            object state = Activator.CreateInstance(stateType);
            map.GetType().GetMethod("set_Item").Invoke(map, new object[] { "to-remove", state });

            MethodInfo reset = _sut.GetMethod("ResetReconnectState", BindingFlags.NonPublic | BindingFlags.Instance);
            reset.Invoke(nm, new object[] { "to-remove" });

            // Map should now be empty.
            int count = (int)map.GetType().GetProperty("Count").GetValue(map);
            Assert.Equal(0, count);
        }

        [Fact]
        public void TryReconnect_skips_when_remote_endpoint_is_empty_and_schedules_next_attempt()
        {
            UANodeManager nm = NewBareInstance();
            ReconnectAsset asset = new() { Endpoint = string.Empty };

            MethodInfo m = _sut.GetMethod("TryReconnect", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(nm, new object[] { "asset-empty", asset });

            // Endpoint was empty, so Disconnect/Connect must NOT have been called.
            Assert.Equal(0, asset.DisconnectCalls);
            Assert.Equal(0, asset.ConnectCalls);

            // Reconnect state should now exist with one consecutive failure.
            FieldInfo statesField = _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance);
            object map = statesField.GetValue(nm);
            object state = map.GetType().GetMethod("get_Item").Invoke(map, new object[] { "asset-empty" });
            Assert.NotNull(state);
            FieldInfo failuresField = state.GetType().GetField("ConsecutiveFailures");
            Assert.Equal(1, (int)failuresField.GetValue(state));
        }

        [Fact]
        public void TryReconnect_clears_reconnect_state_on_successful_reconnect()
        {
            UANodeManager nm = NewBareInstance();
            ReconnectAsset asset = new()
            {
                Endpoint = "host:1234",
                ConnectedAfterConnect = true
            };

            MethodInfo m = _sut.GetMethod("TryReconnect", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(nm, new object[] { "asset-ok", asset });

            Assert.Equal(1, asset.DisconnectCalls);
            Assert.Equal(1, asset.ConnectCalls);
            Assert.Equal("host", asset.LastConnectHost);
            Assert.Equal(1234, asset.LastConnectPort);

            // State must have been reset.
            FieldInfo statesField = _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance);
            object map = statesField.GetValue(nm);
            int count = (int)map.GetType().GetProperty("Count").GetValue(map);
            Assert.Equal(0, count);
        }

        [Fact]
        public void TryReconnect_defaults_port_to_zero_when_port_is_not_an_integer()
        {
            UANodeManager nm = NewBareInstance();
            ReconnectAsset asset = new()
            {
                Endpoint = "host:not-a-port",
                ConnectedAfterConnect = true
            };

            MethodInfo m = _sut.GetMethod("TryReconnect", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(nm, new object[] { "asset-bad-port", asset });

            Assert.Equal(1, asset.ConnectCalls);
            Assert.Equal("host", asset.LastConnectHost);
            Assert.Equal(0, asset.LastConnectPort);
        }

        [Fact]
        public void TryReconnect_increments_failure_counter_when_asset_remains_disconnected()
        {
            UANodeManager nm = NewBareInstance();
            ReconnectAsset asset = new()
            {
                Endpoint = "host:9000",
                ConnectedAfterConnect = false
            };

            MethodInfo m = _sut.GetMethod("TryReconnect", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(nm, new object[] { "asset-still-down", asset });

            FieldInfo statesField = _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance);
            object map = statesField.GetValue(nm);
            object state = map.GetType().GetMethod("get_Item").Invoke(map, new object[] { "asset-still-down" });
            FieldInfo failuresField = state.GetType().GetField("ConsecutiveFailures");
            Assert.Equal(1, (int)failuresField.GetValue(state));
        }

        [Fact]
        public void TryReconnect_increments_failure_counter_when_asset_throws()
        {
            UANodeManager nm = NewBareInstance();
            ReconnectAsset asset = new()
            {
                Endpoint = "host:9001",
                ThrowOnConnect = true
            };

            MethodInfo m = _sut.GetMethod("TryReconnect", BindingFlags.NonPublic | BindingFlags.Instance);
            // The catch path swallows the exception and still schedules a retry.
            m.Invoke(nm, new object[] { "asset-throws", asset });

            FieldInfo statesField = _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance);
            object map = statesField.GetValue(nm);
            object state = map.GetType().GetMethod("get_Item").Invoke(map, new object[] { "asset-throws" });
            FieldInfo failuresField = state.GetType().GetField("ConsecutiveFailures");
            Assert.Equal(1, (int)failuresField.GetValue(state));
        }

        // ---------------- helpers ----------------

        private static Type GetReconnectStateType()
        {
            // Find the nested ReconnectState type used by the reconnect helpers.
            Type nested = _sut.GetNestedType("ReconnectState", BindingFlags.NonPublic | BindingFlags.Public);
            if (nested != null) return nested;

            // Fall back: scan the assembly for a type with that simple name.
            foreach (Type t in _sut.Assembly.GetTypes())
            {
                if (t.Name == "ReconnectState") return t;
            }
            throw new InvalidOperationException("ReconnectState type not found.");
        }

        private sealed class ReconnectAsset : IAsset
        {
            public string Endpoint { get; set; }
            public bool ConnectedAfterConnect { get; set; }
            public bool ThrowOnConnect { get; set; }
            public int ConnectCalls { get; private set; }
            public int DisconnectCalls { get; private set; }
            public string LastConnectHost { get; private set; }
            public int LastConnectPort { get; private set; }

            private bool _isConnected;

            public bool IsConnected => _isConnected;

            public string GetRemoteEndpoint() => Endpoint;

            public void Connect(string ipAddress, int port)
            {
                ConnectCalls++;
                LastConnectHost = ipAddress;
                LastConnectPort = port;
                if (ThrowOnConnect)
                {
                    throw new InvalidOperationException("simulated reconnect failure");
                }

                _isConnected = ConnectedAfterConnect;
            }

            public void Disconnect()
            {
                DisconnectCalls++;
                _isConnected = false;
            }

            public object Read(AssetTag tag) => null;

            public void Write(AssetTag tag, object value) { }

            public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
            {
                outputArgs ??= new List<object>();
                return null;
            }
        }
    }
}
