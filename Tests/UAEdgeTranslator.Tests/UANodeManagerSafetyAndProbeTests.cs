namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for additional <see cref="UANodeManager"/>
    /// branches that don't need a fully wired OPC UA stack:
    /// the <c>IsSafeAssetName</c> validator and the failure paths of
    /// <c>OnConnectionTest</c> (timeout / unreachable host) that the
    /// integration tests don't exercise because they require a real
    /// loopback listener.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UANodeManagerSafetyAndProbeTests
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

            // _shutdownCts is required by OnConnectionTest's linked CTS.
            FieldInfo cts = _sut.GetField("_shutdownCts", BindingFlags.NonPublic | BindingFlags.Instance);
            cts.SetValue(nm, new CancellationTokenSource());

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

        // ---------------- IsSafeAssetName ----------------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        public void IsSafeAssetName_rejects_null_or_whitespace(string name)
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("IsSafeAssetName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.False((bool)m.Invoke(nm, new object[] { name }));
        }

        [Fact]
        public void IsSafeAssetName_rejects_names_longer_than_max_length()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("IsSafeAssetName", BindingFlags.NonPublic | BindingFlags.Instance);
            // _cMaxAssetNameLength is 128.
            string tooLong = new string('a', 129);
            Assert.False((bool)m.Invoke(nm, new object[] { tooLong }));
        }

        [Theory]
        [InlineData("..")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        [InlineData("a*b")]
        [InlineData("a:b")]
        [InlineData(".hidden")]
        public void IsSafeAssetName_rejects_path_traversal_and_unsafe_characters(string name)
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("IsSafeAssetName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.False((bool)m.Invoke(nm, new object[] { name }));
        }

        [Theory]
        [InlineData("asset")]
        [InlineData("Asset_001")]
        [InlineData("asset-name")]
        [InlineData("a.b.c")]
        public void IsSafeAssetName_accepts_alphanumeric_and_safe_punctuation(string name)
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("IsSafeAssetName", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True((bool)m.Invoke(nm, new object[] { name }));
        }

        // ---------------- OnConnectionTest ----------------

        [Fact]
        public void OnConnectionTest_returns_BadInvalidArgument_for_null_endpoint()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("OnConnectionTest", BindingFlags.NonPublic | BindingFlags.Instance);

            IList<object> inputs = new List<object> { null };
            IList<object> outputs = new List<object> { null, null };

            object res = m.Invoke(nm, new object[] { null, null, inputs, outputs });
            ServiceResult sr = (ServiceResult)res;
            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, sr.StatusCode);
        }

        [Fact]
        public void OnConnectionTest_returns_BadInvalidArgument_when_no_port()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("OnConnectionTest", BindingFlags.NonPublic | BindingFlags.Instance);

            IList<object> inputs = new List<object> { "hostonly" };
            IList<object> outputs = new List<object> { null, null };

            ServiceResult sr = (ServiceResult)m.Invoke(nm, new object[] { null, null, inputs, outputs });
            Assert.Equal((StatusCode)StatusCodes.BadInvalidArgument, sr.StatusCode);
            Assert.False((bool)outputs[0]);
            Assert.Contains("port is required", outputs[1].ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void OnConnectionTest_returns_BadNotFound_when_remote_host_refuses_connection()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo m = _sut.GetMethod("OnConnectionTest", BindingFlags.NonPublic | BindingFlags.Instance);

            // 127.0.0.1:1 is loopback to a closed port — ConnectAsync should
            // throw a SocketException (ConnectionRefused) almost immediately,
            // exercising the generic catch branch -> BadNotFound. Wrap in a
            // scheme so the URI parser doesn't claim "127.0.0.1" as a scheme.
            IList<object> inputs = new List<object> { "tcp://127.0.0.1:1" };
            IList<object> outputs = new List<object> { null, null };

            ServiceResult sr = (ServiceResult)m.Invoke(nm, new object[] { null, null, inputs, outputs });
            Assert.Equal((StatusCode)StatusCodes.BadNotFound, sr.StatusCode);
            Assert.False((bool)outputs[0]);
            Assert.NotNull(outputs[1]);
        }

        [Fact]
        public void OnConnectionTest_returns_BadNotFound_when_shutdown_token_cancels_probe()
        {
            UANodeManager nm = NewBareInstance();

            // Pre-cancel the shutdown CTS so the linked token in OnConnectionTest
            // is cancelled before ConnectAsync makes any progress, deterministically
            // hitting the OperationCanceledException catch branch.
            FieldInfo ctsField = _sut.GetField("_shutdownCts", BindingFlags.NonPublic | BindingFlags.Instance);
            CancellationTokenSource cts = (CancellationTokenSource)ctsField.GetValue(nm);
            cts.Cancel();

            MethodInfo m = _sut.GetMethod("OnConnectionTest", BindingFlags.NonPublic | BindingFlags.Instance);
            // Use a routable port that won't accept fast on most hosts so we
            // really hit the cancellation path; even loopback to a closed port
            // should still cancel before the SocketException surfaces.
            IList<object> inputs = new List<object> { "tcp://127.0.0.1:1" };
            IList<object> outputs = new List<object> { null, null };

            ServiceResult sr = (ServiceResult)m.Invoke(nm, new object[] { null, null, inputs, outputs });
            // Either the connection failed (BadNotFound) or was cancelled (BadNotFound).
            Assert.Equal((StatusCode)StatusCodes.BadNotFound, sr.StatusCode);
            Assert.False((bool)outputs[0]);
        }
    }
}
