namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Xunit;

    /// <summary>
    /// Targeted reflection-driven coverage for cheap remaining branches in
    /// <see cref="UANodeManager"/> that the existing reflection suites stop
    /// short of: bad-input early returns of <c>OnDeleteAsset</c> /
    /// <c>OnCreateAssetForEndpoint</c>, the <c>OnDiscoverAssets</c> catch
    /// branch, the <c>AddTag</c> missing-driver throw, the
    /// <c>AddNamespace</c> input-validation throw, the <c>Dispose</c>
    /// already-disposed CTS catch, and the <c>ParseExpandedNodeId</c>
    /// success / unknown-namespace branches.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UANodeManagerSmallBranchesTests
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

            // _shutdownCts is read by Dispose / OnConnectionTest.
            FieldInfo cts = _sut.GetField("_shutdownCts", BindingFlags.NonPublic | BindingFlags.Instance);
            cts?.SetValue(nm, new CancellationTokenSource());

            // CustomNodeManager2.Lock has no setter; write the backing field so
            // the lock(Lock) statements in Dispose() / lookup paths don't NRE.
            FieldInfo lockField = typeof(Opc.Ua.Server.CustomNodeManager2).GetField("<Lock>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (lockField != null && lockField.GetValue(nm) == null)
            {
                lockField.SetValue(nm, new object());
            }

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
            if (prop?.GetValue(null) == null)
            {
                prop?.SetValue(null, new Opc.Ua.Cloud.ConsoleTelemetry());
            }
        }

        // ---------------- OnDeleteAsset ----------------

        [Fact]
        public void OnDeleteAsset_returns_BadInvalidArgument_for_null_input()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("OnDeleteAsset", BindingFlags.Public | BindingFlags.Instance);

            ServiceResult sr = (ServiceResult)m.Invoke(
                nm,
                new object[] { null, null, new List<object> { null }, new List<object>() });

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, sr.StatusCode);
        }

        // ---------------- OnCreateAssetForEndpoint ----------------

        [Fact]
        public void OnCreateAssetForEndpoint_returns_BadInvalidArgument_when_inputs_are_null()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("OnCreateAssetForEndpoint", BindingFlags.NonPublic | BindingFlags.Instance);

            ServiceResult sr = (ServiceResult)m.Invoke(
                nm,
                new object[] { null, null, new List<object> { null, null }, new List<object>() });

            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, sr.StatusCode);
        }

        // ---------------- OnDiscoverAssets ----------------

        [Fact]
        public void OnDiscoverAssets_returns_BadTimeout_when_a_driver_throws_during_discovery()
        {
            UANodeManager nm = NewBareInstance();

            // Register a throwing driver in the shared registry, run, then unregister.
            ProtocolDriverRegistry registry = Program.Drivers;
            FieldInfo driversField = typeof(ProtocolDriverRegistry).GetField("_drivers", BindingFlags.NonPublic | BindingFlags.Instance);
            System.Collections.IDictionary map = (System.Collections.IDictionary)driversField.GetValue(registry);

            DiscoveryThrowingDriver throwing = new();
            map[throwing.Scheme] = throwing;
            try
            {
                MethodInfo m = _sut.GetMethod("OnDiscoverAssets", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                ServiceResult sr = (ServiceResult)m.Invoke(
                    nm,
                    new object[] { null, null, new List<object>(), new List<object> { null } });

                Assert.Equal((StatusCode)StatusCodes.BadTimeout, sr.StatusCode);
            }
            finally
            {
                map.Remove(throwing.Scheme);
            }
        }

        // ---------------- AddTag missing-driver branch ----------------

        [Fact]
        public void AddTag_throws_when_no_driver_is_registered_for_base_uri()
        {
            UANodeManager nm = NewBareInstance();

            MethodInfo m = _sut.GetMethod("AddTag", BindingFlags.NonPublic | BindingFlags.Instance);
            ThingDescription td = new() { Base = "no-such-scheme://h:1/1", Name = "noop" };

            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(() =>
                m.Invoke(nm, new object[] { td, new object(), "asset", (byte)1, "v", string.Empty }));
            Assert.IsType<Exception>(tie.InnerException);
            Assert.Contains("No driver installed", tie.InnerException.Message);
        }

        // ---------------- AddNamespace input validation ----------------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void AddNamespace_throws_for_null_or_whitespace_input(string ns)
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("AddNamespace", BindingFlags.NonPublic | BindingFlags.Instance);

            TargetInvocationException tie = Assert.Throws<TargetInvocationException>(() =>
                m.Invoke(nm, new object[] { ns }));
            Assert.IsType<ArgumentNullException>(tie.InnerException);
        }

        // ---------------- ParseExpandedNodeId additional branches ----------------

        [Fact]
        public void ParseExpandedNodeId_returns_null_when_namespace_uri_is_not_registered()
        {
            UANodeManager nm = NewBareInstance();
            // Write the backing field directly: the public NamespaceUris setter
            // calls SetNamespaces() which needs more infrastructure than a bare
            // reflection instance has.
            FieldInfo nsField = typeof(Opc.Ua.Server.CustomNodeManager2).GetField("m_namespaceUris", BindingFlags.NonPublic | BindingFlags.Instance);
            nsField.SetValue(nm, (System.Collections.Generic.IReadOnlyList<string>)new[] { "http://opcfoundation.org/UA/EdgeTranslator/" });

            MethodInfo parse = _sut.GetMethod("ParseExpandedNodeId", BindingFlags.NonPublic | BindingFlags.Instance);
            object result = parse.Invoke(nm, new object[] { "nsu=http://not-registered/;i=42" });

            Assert.Null(result);
        }

        // ---------------- Dispose double-cancel safety ----------------

        [Fact]
        public void Dispose_does_not_rethrow_when_shutdownCts_is_already_disposed()
        {
            UANodeManager nm = NewBareInstance();

            // Pre-dispose the CTS so Cancel() throws ObjectDisposedException
            // and exercises the catch branch.
            FieldInfo ctsField = _sut.GetField("_shutdownCts", BindingFlags.NonPublic | BindingFlags.Instance);
            CancellationTokenSource cts = (CancellationTokenSource)ctsField.GetValue(nm);
            cts.Dispose();

            MethodInfo dispose = _sut.GetMethod("Dispose", BindingFlags.NonPublic | BindingFlags.Instance, binder: null,
                types: new[] { typeof(bool) }, modifiers: null);
            // Must not throw.
            dispose.Invoke(nm, new object[] { true });
        }

        private sealed class DiscoveryThrowingDriver : IProtocolDriver
        {
            public string Scheme => "discovery-throws";

            public string WoTBindingUri => "https://example/discovery-throws";

            public IEnumerable<string> Discover()
                => throw new InvalidOperationException("simulated discovery failure");

            public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
                => throw new NotSupportedException();

            public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
                => throw new NotSupportedException();

            public AssetTag CreateTag(
                ThingDescription td,
                object form,
                string assetId,
                byte unitId,
                string variableId,
                string mappedUAExpandedNodeId,
                string mappedUAFieldPath)
                => throw new NotSupportedException();
        }
    }
}
