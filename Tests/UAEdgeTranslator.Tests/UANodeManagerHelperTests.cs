namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Xunit;

    /// <summary>
    /// UANodeManager extends the OPC UA SDK's CustomNodeManager2, whose
    /// constructor requires a fully initialized server instance. The helper
    /// methods exercised here (asset-name validation, reconnect bookkeeping)
    /// are independent of any SDK state, so the tests instantiate
    /// UANodeManager via <see cref="RuntimeHelpers.GetUninitializedObject"/>
    /// and reach the helpers via reflection.
    /// </summary>
    public class UANodeManagerHelperTests
    {
        private static readonly Type _sut = typeof(UANodeManager);

        private static UANodeManager NewBareInstance()
        {
            UANodeManager nm = (UANodeManager)RuntimeHelpers.GetUninitializedObject(_sut);

            // Ensure the reconnect dictionary exists for tests that read/write it.
            FieldInfo reconnectField = _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance)!;
            if (reconnectField.GetValue(nm) == null)
            {
                Type dictType = reconnectField.FieldType;
                reconnectField.SetValue(nm, Activator.CreateInstance(dictType));
            }

            return nm;
        }

        // ---------------- IsSafeAssetName ----------------

        [Theory]
        [InlineData("plain")]
        [InlineData("with.dot")]
        [InlineData("with-dash")]
        [InlineData("with_underscore")]
        [InlineData("Mixed123")]
        public void IsSafeAssetName_accepts_safe_inputs(string name)
        {
            Assert.True(InvokeIsSafeAssetName(name));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData(".hidden")]
        [InlineData("..")]
        [InlineData("../etc/passwd")]
        [InlineData("with/slash")]
        [InlineData("with\\backslash")]
        [InlineData("with space")]
        [InlineData("with!bang")]
        [InlineData("with*star")]
        [InlineData("with:colon")]
        [InlineData("name\0withnull")]
        public void IsSafeAssetName_rejects_unsafe_inputs(string name)
        {
            Assert.False(InvokeIsSafeAssetName(name));
        }

        [Fact]
        public void IsSafeAssetName_rejects_overly_long_names()
        {
            string tooLong = new string('a', 129);
            Assert.False(InvokeIsSafeAssetName(tooLong));
        }

        [Fact]
        public void IsSafeAssetName_accepts_max_length_name()
        {
            string atLimit = new string('a', 128);
            Assert.True(InvokeIsSafeAssetName(atLimit));
        }

        // ---------------- IsReconnectAttemptDue / Reset / Schedule ----------------

        [Fact]
        public void IsReconnectAttemptDue_returns_true_when_no_state_recorded()
        {
            UANodeManager nm = NewBareInstance();
            Assert.True(InvokeBool(nm, "IsReconnectAttemptDue", "asset-1"));
        }

        [Fact]
        public void ResetReconnectState_removes_existing_entry()
        {
            UANodeManager nm = NewBareInstance();
            object dict = GetReconnectStates(nm);
            object state = NewReconnectState();
            AddReconnectState(dict, "asset-1", state);

            _sut.GetMethod("ResetReconnectState", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(nm, new object[] { "asset-1" });

            Assert.Equal(0, ReconnectStateCount(dict));
        }

        [Fact]
        public void ScheduleNextReconnect_increases_failures_and_doubles_backoff_until_capped()
        {
            UANodeManager nm = NewBareInstance();
            object state = NewReconnectState();

            SetReconnectField(state, "CurrentBackoffMs", 1_000);
            SetReconnectField(state, "ConsecutiveFailures", 0);

            MethodInfo schedule = _sut.GetMethod("ScheduleNextReconnect", BindingFlags.NonPublic | BindingFlags.Instance)!;

            // First failure: 1s -> doubles to 2s, NextAttemptTimestamp set in future.
            schedule.Invoke(nm, new object[] { state });
            Assert.Equal(1, GetReconnectField<int>(state, "ConsecutiveFailures"));
            Assert.Equal(2_000, GetReconnectField<int>(state, "CurrentBackoffMs"));
            Assert.True(GetReconnectField<long>(state, "NextAttemptTimestamp") > 0);

            // Drive the doubling several times and ensure we cap at the documented max.
            for (int i = 0; i < 20; i++)
            {
                schedule.Invoke(nm, new object[] { state });
            }

            int max = GetMaxBackoffMs();
            Assert.Equal(max, GetReconnectField<int>(state, "CurrentBackoffMs"));
            Assert.Equal(21, GetReconnectField<int>(state, "ConsecutiveFailures"));
        }

        [Fact]
        public void IsReconnectAttemptDue_respects_scheduled_future_timestamp()
        {
            UANodeManager nm = NewBareInstance();
            object dict = GetReconnectStates(nm);
            object state = NewReconnectState();

            // Mark the next attempt far in the future so the helper returns false.
            SetReconnectField(state, "NextAttemptTimestamp", long.MaxValue);
            AddReconnectState(dict, "asset-2", state);

            Assert.False(InvokeBool(nm, "IsReconnectAttemptDue", "asset-2"));

            // Backdate the timestamp so the helper now returns true.
            SetReconnectField(state, "NextAttemptTimestamp", 0L);
            Assert.True(InvokeBool(nm, "IsReconnectAttemptDue", "asset-2"));
        }

        // ---------------- private helpers ----------------

        private static bool InvokeIsSafeAssetName(string name)
        {
            UANodeManager nm = NewBareInstance();
            return (bool)_sut
                .GetMethod("IsSafeAssetName", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(nm, new object[] { name })!;
        }

        private static bool InvokeBool(UANodeManager nm, string methodName, params object[] args)
            => (bool)_sut
                .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(nm, args)!;

        private static object GetReconnectStates(UANodeManager nm)
            => _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(nm)!;

        private static int ReconnectStateCount(object dict)
            => (int)dict.GetType().GetProperty("Count")!.GetValue(dict)!;

        private static void AddReconnectState(object dict, string key, object value)
            => dict.GetType().GetMethod("set_Item")!.Invoke(dict, new[] { key, value });

        private static object NewReconnectState()
        {
            Type t = _sut.GetNestedType("ReconnectState", BindingFlags.NonPublic)!;
            return Activator.CreateInstance(t)!;
        }

        private static void SetReconnectField(object state, string fieldName, object value)
        {
            Type t = state.GetType();
            t.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)!.SetValue(state, value);
        }

        private static T GetReconnectField<T>(object state, string fieldName)
        {
            Type t = state.GetType();
            return (T)t.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)!.GetValue(state)!;
        }

        private static int GetMaxBackoffMs()
        {
            FieldInfo f = _sut.GetField("_reconnectMaxBackoffMs", BindingFlags.NonPublic | BindingFlags.Static)
                       ?? _sut.GetField("_reconnectMaxBackoffMs", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)f!.GetValue(null)!;
        }
    }
}
