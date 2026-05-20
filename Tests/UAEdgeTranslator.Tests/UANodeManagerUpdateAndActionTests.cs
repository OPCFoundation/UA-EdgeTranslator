namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for <see cref="UANodeManager"/> private methods
    /// that maintain the per-tag UA variable / property cache and dispatch WoT
    /// action method calls. None of these helpers require a fully wired OPC UA
    /// stack, so the tests use <see cref="RuntimeHelpers.GetUninitializedObject"/>
    /// and reach the methods via reflection — mirroring the established pattern
    /// in <see cref="UANodeManagerHelperTests"/> and
    /// <see cref="UANodeManagerHelperBranchesTests"/>.
    ///
    /// Joined to the working-directory collection so the tests don't race the
    /// integration fixture (which mutates and disposes <c>Program.Telemetry</c>
    /// and <c>Program.Drivers</c>).
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UANodeManagerUpdateAndActionTests
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

        private static void EnsureProgramTelemetry()
        {
            // Some methods touch Program.Telemetry counters; integration fixtures
            // dispose the singleton between runs, so always install a fresh one.
            PropertyInfo prop = typeof(Program).GetProperty(nameof(Program.Telemetry), BindingFlags.Public | BindingFlags.Static);
            prop.SetValue(null, new Opc.Ua.Cloud.ConsoleTelemetry());
        }

        // ---------------- UpdateUAServerVariable ----------------

        [Fact]
        public void UpdateUAServerVariable_short_circuits_when_variable_not_registered()
        {
            UANodeManager nm = NewBareInstance();

            AssetTag tag = new() { Name = "missing-tag" };

            MethodInfo update = _sut.GetMethod("UpdateUAServerVariable", BindingFlags.NonPublic | BindingFlags.Instance);

            // No entry exists in _uaVariables for this tag name. The helper must
            // simply early-return without throwing.
            update.Invoke(nm, new object[] { tag, 42, true });

            // _uaVariables must still be empty afterwards.
            object dict = _sut.GetField("_uaVariables", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);
            Assert.Equal(0, (int)dict.GetType().GetProperty("Count").GetValue(dict));
        }

        [Fact]
        public void UpdateUAServerVariable_writes_simple_value_and_marks_status_good()
        {
            UANodeManager nm = NewBareInstance();

            BaseDataVariableState variable = new(null)
            {
                NodeId = new NodeId("simpleVar", 1),
                BrowseName = new QualifiedName("simpleVar", 1),
                DisplayName = new LocalizedText("en", "simpleVar"),
                DataType = new NodeId((uint)DataTypes.Int32, 0),
                Value = 0
            };

            AddUaVariable(nm, "simpleVar", variable);

            AssetTag tag = new() { Name = "simpleVar" };

            MethodInfo update = _sut.GetMethod("UpdateUAServerVariable", BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(nm, new object[] { tag, 123, true });

            Assert.Equal(123, variable.Value);
            Assert.Equal(StatusCodes.Good, variable.StatusCode.Code);
        }

        [Fact]
        public void UpdateUAServerVariable_writes_BadDataUnavailable_when_disconnected()
        {
            UANodeManager nm = NewBareInstance();

            BaseDataVariableState variable = new(null)
            {
                NodeId = new NodeId("disconnectedVar", 1),
                BrowseName = new QualifiedName("disconnectedVar", 1),
                DisplayName = new LocalizedText("en", "disconnectedVar"),
                DataType = new NodeId((uint)DataTypes.Int32, 0),
                Value = 0
            };

            AddUaVariable(nm, "disconnectedVar", variable);

            AssetTag tag = new() { Name = "disconnectedVar" };

            MethodInfo update = _sut.GetMethod("UpdateUAServerVariable", BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(nm, new object[] { tag, 0, false });

            Assert.Equal(StatusCodes.BadDataUnavailable, variable.StatusCode.Code);
        }

        [Fact]
        public void UpdateUAServerVariable_converts_TimeSpan_to_milliseconds_double()
        {
            UANodeManager nm = NewBareInstance();

            BaseDataVariableState variable = new(null)
            {
                NodeId = new NodeId("durationVar", 1),
                BrowseName = new QualifiedName("durationVar", 1),
                DisplayName = new LocalizedText("en", "durationVar"),
                DataType = new NodeId((uint)DataTypes.Double, 0),
                Value = 0d
            };

            AddUaVariable(nm, "durationVar", variable);

            AssetTag tag = new() { Name = "durationVar" };

            MethodInfo update = _sut.GetMethod("UpdateUAServerVariable", BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(nm, new object[] { tag, TimeSpan.FromMilliseconds(2500), true });

            Assert.Equal(2500d, variable.Value);
        }

        [Fact]
        public void UpdateUAServerVariable_converts_DateTimeOffset_to_UtcDateTime()
        {
            UANodeManager nm = NewBareInstance();

            BaseDataVariableState variable = new(null)
            {
                NodeId = new NodeId("dateVar", 1),
                BrowseName = new QualifiedName("dateVar", 1),
                DisplayName = new LocalizedText("en", "dateVar"),
                DataType = new NodeId((uint)DataTypes.DateTime, 0),
                Value = DateTime.MinValue
            };

            AddUaVariable(nm, "dateVar", variable);

            AssetTag tag = new() { Name = "dateVar" };

            DateTimeOffset offset = new(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
            MethodInfo update = _sut.GetMethod("UpdateUAServerVariable", BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(nm, new object[] { tag, offset, true });

            Assert.Equal(offset.UtcDateTime, variable.Value);
        }

        // ---------------- UpdateUAServerProperty ----------------

        [Fact]
        public void UpdateUAServerProperty_short_circuits_when_property_not_registered()
        {
            UANodeManager nm = NewBareInstance();

            AssetTag tag = new() { Name = "missing-prop" };

            MethodInfo update = _sut.GetMethod("UpdateUAServerProperty", BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(nm, new object[] { tag, "value", true });

            object dict = _sut.GetField("_uaProperties", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);
            Assert.Equal(0, (int)dict.GetType().GetProperty("Count").GetValue(dict));
        }

        [Fact]
        public void UpdateUAServerProperty_writes_value_and_status_good_when_connected()
        {
            UANodeManager nm = NewBareInstance();

            PropertyState property = new(null)
            {
                NodeId = new NodeId("simpleProp", 1),
                BrowseName = new QualifiedName("simpleProp", 1),
                DisplayName = new LocalizedText("en", "simpleProp"),
                DataType = new NodeId((uint)DataTypes.String, 0),
                Value = string.Empty
            };

            AddUaProperty(nm, "simpleProp", property);

            AssetTag tag = new() { Name = "simpleProp" };

            MethodInfo update = _sut.GetMethod("UpdateUAServerProperty", BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(nm, new object[] { tag, "hello", true });

            Assert.Equal("hello", property.Value);
            Assert.Equal(StatusCodes.Good, property.StatusCode.Code);
        }

        [Fact]
        public void UpdateUAServerProperty_writes_status_bad_when_disconnected()
        {
            UANodeManager nm = NewBareInstance();

            PropertyState property = new(null)
            {
                NodeId = new NodeId("disconnectedProp", 1),
                BrowseName = new QualifiedName("disconnectedProp", 1),
                DisplayName = new LocalizedText("en", "disconnectedProp"),
                DataType = new NodeId((uint)DataTypes.String, 0),
                Value = "x"
            };

            AddUaProperty(nm, "disconnectedProp", property);

            AssetTag tag = new() { Name = "disconnectedProp" };

            MethodInfo update = _sut.GetMethod("UpdateUAServerProperty", BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(nm, new object[] { tag, null, false });

            Assert.Equal(StatusCodes.BadDataUnavailable, property.StatusCode.Code);
        }

        // ---------------- OnTDActionCalled ----------------

        [Fact]
        public void OnTDActionCalled_returns_BadInternalError_when_asset_is_unknown()
        {
            UANodeManager nm = NewBareInstance();

            // Arrange: a method whose parent BrowseName.Name maps to an
            // assetId that is NOT in _assets — this throws KeyNotFoundException
            // inside the try-block and exercises the catch path.
            BaseObjectState parent = new(null)
            {
                BrowseName = new QualifiedName("unknown-asset", 1),
                NodeId = new NodeId("parent", 1),
                DisplayName = new LocalizedText("en", "unknown-asset")
            };

            MethodState method = new(parent)
            {
                BrowseName = new QualifiedName("reset", 1),
                NodeId = new NodeId("reset", 1),
                DisplayName = new LocalizedText("en", "reset")
            };

            IList<object> inputs = new List<object>();
            IList<object> outputs = new List<object>();

            MethodInfo m = _sut.GetMethod("OnTDActionCalled", BindingFlags.NonPublic | BindingFlags.Instance);
            object result = m.Invoke(nm, new object[] { null, method, inputs, outputs });

            ServiceResult sr = (ServiceResult)result;
            Assert.Equal(StatusCodes.BadInternalError, sr.StatusCode.Code);
        }

        [Theory]
        [InlineData("ok", StatusCodes.Good)]
        [InlineData("OK", StatusCodes.Good)]
        [InlineData("success", StatusCodes.Good)]
        [InlineData("Success", StatusCodes.Good)]
        public void OnTDActionCalled_returns_Good_when_asset_returns_ok_or_success(string actionResult, uint expectedStatus)
        {
            UANodeManager nm = NewBareInstance();
            AddAsset(nm, "asset-1", new ActionAsset { Result = actionResult });

            ServiceResult sr = InvokeOnTdAction(nm, "asset-1");

            Assert.Equal(expectedStatus, sr.StatusCode.Code);
        }

        [Fact]
        public void OnTDActionCalled_returns_Bad_with_message_when_asset_returns_failure_string()
        {
            UANodeManager nm = NewBareInstance();
            AddAsset(nm, "asset-fail", new ActionAsset { Result = "device offline" });

            ServiceResult sr = InvokeOnTdAction(nm, "asset-fail");

            Assert.Equal(StatusCodes.Bad, sr.StatusCode.Code);
            Assert.Contains("device offline", sr.LocalizedText.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void OnTDActionCalled_returns_Uncertain_when_asset_returns_null()
        {
            UANodeManager nm = NewBareInstance();
            AddAsset(nm, "asset-null", new ActionAsset { Result = null });

            ServiceResult sr = InvokeOnTdAction(nm, "asset-null");

            Assert.Equal(StatusCodes.Uncertain, sr.StatusCode.Code);
            Assert.Contains("no result", sr.LocalizedText.Text, StringComparison.OrdinalIgnoreCase);
        }

        // ---------------- AssetConnectionTest ----------------

        [Fact]
        public void AssetConnectionTest_throws_when_no_driver_is_registered_for_base_uri()
        {
            UANodeManager nm = NewBareInstance();

            // Make sure no driver is registered for the (custom) scheme used here.
            ClearProgramDrivers();

            ThingDescription td = new()
            {
                Name = "no-driver-asset",
                Base = "noscheme://device:1/1"
            };

            MethodInfo m = _sut.GetMethod("AssetConnectionTest", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = new object[] { td, (byte)0 };
            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(() => m.Invoke(nm, args));

            Assert.Contains("No driver installed for base URI", tie.InnerException.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AssetConnectionTest_creates_and_stores_asset_when_driver_is_registered()
        {
            UANodeManager nm = NewBareInstance();

            ClearProgramDrivers();
            Program.Drivers.Register(new MockProtocolDriver());

            ThingDescription td = new()
            {
                Name = "mock-asset",
                Base = "mock://device:1502/3"
            };

            MethodInfo m = _sut.GetMethod("AssetConnectionTest", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = new object[] { td, (byte)0 };
            m.Invoke(nm, args);

            // unitId out arg is the second positional argument; MockProtocolDriver
            // parses the trailing "/3" path segment as the unit id.
            Assert.Equal((byte)3, (byte)args[1]);

            // The asset must have been recorded under td.Name.
            object assetsObj = _sut.GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);
            IDictionary assetsDict = (IDictionary)assetsObj;
            Assert.True(assetsDict.Contains("mock-asset"));
            Assert.IsType<MockAsset>(assetsDict["mock-asset"]);
        }

        // ---------------- helpers ----------------

        private static void AddUaVariable(UANodeManager nm, string key, BaseDataVariableState variable)
        {
            object dict = _sut.GetField("_uaVariables", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);
            dict.GetType().GetMethod("set_Item").Invoke(dict, new object[] { key, variable });
        }

        private static void AddUaProperty(UANodeManager nm, string key, PropertyState property)
        {
            object dict = _sut.GetField("_uaProperties", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);
            dict.GetType().GetMethod("set_Item").Invoke(dict, new object[] { key, property });
        }

        private static void AddAsset(UANodeManager nm, string key, Opc.Ua.Edge.Translator.Interfaces.IAsset asset)
        {
            object dict = _sut.GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);
            dict.GetType().GetMethod("set_Item").Invoke(dict, new object[] { key, asset });
        }

        private static ServiceResult InvokeOnTdAction(UANodeManager nm, string assetId)
        {
            BaseObjectState parent = new(null)
            {
                BrowseName = new QualifiedName(assetId, 1),
                NodeId = new NodeId("parent", 1),
                DisplayName = new LocalizedText("en", assetId)
            };

            MethodState method = new(parent)
            {
                BrowseName = new QualifiedName("act", 1),
                NodeId = new NodeId("act", 1),
                DisplayName = new LocalizedText("en", "act")
            };

            IList<object> inputs = new List<object>();
            IList<object> outputs = new List<object>();

            MethodInfo m = _sut.GetMethod("OnTDActionCalled", BindingFlags.NonPublic | BindingFlags.Instance);
            return (ServiceResult)m.Invoke(nm, new object[] { null, method, inputs, outputs });
        }

        private static void ClearProgramDrivers()
        {
            PropertyInfo registryProperty = typeof(Program).GetProperty(nameof(Program.Drivers), BindingFlags.Public | BindingFlags.Static);
            object registry = registryProperty.GetValue(null);
            if (registry == null)
            {
                return;
            }

            FieldInfo driversField = registry.GetType().GetField("_drivers", BindingFlags.Instance | BindingFlags.NonPublic);
            if (driversField?.GetValue(registry) is IDictionary driversMap)
            {
                driversMap.Clear();
            }
        }

        // Test double for the IAsset surface used by OnTDActionCalled. Records
        // every invocation so individual tests can assert if needed and returns
        // a configurable ExecuteAction result so each branch can be exercised.
        private sealed class ActionAsset : Opc.Ua.Edge.Translator.Interfaces.IAsset
        {
            public string Result { get; set; }

            public bool IsConnected => true;

            public void Connect(string ipAddress, int port) { }

            public void Disconnect() { }

            public string GetRemoteEndpoint() => string.Empty;

            public object Read(AssetTag tag) => null;

            public void Write(AssetTag tag, object value) { }

            public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
            {
                outputArgs ??= new List<object>();
                return Result;
            }
        }
    }
}
