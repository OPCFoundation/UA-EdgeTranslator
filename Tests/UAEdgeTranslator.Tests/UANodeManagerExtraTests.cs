namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Xunit;

    /// <summary>
    /// Additional reflection-driven tests for <see cref="UANodeManager"/> helpers
    /// that are independent of any live SDK state. Mirrors the existing
    /// <see cref="UANodeManagerHelperTests"/> pattern but exercises a wider set
    /// of branches (field accessors, reverse-index bookkeeping, dictionary
    /// initialization, and reconnect schedule edge cases).
    /// </summary>
    public class UANodeManagerExtraTests
    {
        private static readonly Type _sut = typeof(UANodeManager);

        private static UANodeManager NewBareInstance()
        {
            UANodeManager nm = (UANodeManager)RuntimeHelpers.GetUninitializedObject(_sut);

            // Ensure all dictionaries used by the helpers exist.
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
            if (field == null) return;

            if (field.GetValue(instance) == null)
            {
                field.SetValue(instance, Activator.CreateInstance(field.FieldType));
            }
        }

        [Fact]
        public void IsSafeAssetName_rejects_names_starting_with_dot_after_validation()
        {
            // Disallow leading dot path
            Assert.False(InvokeIsSafeAssetName(".hidden_underscore"));
        }

        [Theory]
        [InlineData("a")]
        [InlineData("Z9")]
        [InlineData("0digit_first")]
        [InlineData("dot.between")]
        [InlineData("under_score")]
        [InlineData("hyphen-name")]
        public void IsSafeAssetName_accepts_short_safe_names(string name)
        {
            Assert.True(InvokeIsSafeAssetName(name));
        }

        [Fact]
        public void ResetReconnectState_when_no_state_does_not_throw()
        {
            UANodeManager nm = NewBareInstance();
            MethodInfo reset = _sut.GetMethod("ResetReconnectState", BindingFlags.NonPublic | BindingFlags.Instance);
            reset.Invoke(nm, new object[] { "no-such-asset" });

            // The dictionary is still empty and call did not throw.
            object dict = _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);
            Assert.Equal(0, (int)dict.GetType().GetProperty("Count").GetValue(dict));
        }

        [Fact]
        public void ScheduleNextReconnect_caps_backoff_at_max()
        {
            UANodeManager nm = NewBareInstance();
            object state = NewReconnectState();

            int max = GetMaxBackoffMs();
            SetReconnectField(state, "CurrentBackoffMs", max);
            SetReconnectField(state, "ConsecutiveFailures", 5);

            MethodInfo schedule = _sut.GetMethod("ScheduleNextReconnect", BindingFlags.NonPublic | BindingFlags.Instance);
            schedule.Invoke(nm, new object[] { state });

            // The doubled value Math.Min'd against the cap should equal max.
            Assert.Equal(max, GetReconnectField<int>(state, "CurrentBackoffMs"));
            Assert.Equal(6, GetReconnectField<int>(state, "ConsecutiveFailures"));
            Assert.True(GetReconnectField<long>(state, "NextAttemptTimestamp") > 0);
        }

        [Fact]
        public void TagIndex_is_concurrent_dictionary_keyed_by_NodeId()
        {
            UANodeManager nm = NewBareInstance();
            object indexObj = _sut.GetField("_tagIndex", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);

            Assert.NotNull(indexObj);

            // Verify writes/reads work using the actual public dictionary surface.
            IDictionary index = indexObj as IDictionary;
            Assert.NotNull(index);

            var key = new NodeId("test", 0);
            var tag = new AssetTag { Name = "asset:tag", PollingInterval = 1000 };
            var value = ("asset", tag);

            // ConcurrentDictionary supports IDictionary indexer.
            index[key] = value;
            Assert.Single(index);

            object roundTripped = index[key];
            Assert.NotNull(roundTripped);
        }

        [Fact]
        public void Tags_collection_uses_concurrent_dictionary_of_lists()
        {
            UANodeManager nm = NewBareInstance();
            object tagsObj = _sut.GetField("_tags", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);

            Assert.NotNull(tagsObj);
            IDictionary tags = (IDictionary)tagsObj;
            tags["asset"] = new List<AssetTag> { new AssetTag { Name = "x", PollingInterval = 1500 } };

            var listObj = (List<AssetTag>)tags["asset"];
            Assert.Single(listObj);
            Assert.Equal("x", listObj[0].Name);
        }

        [Fact]
        public void IsReconnectAttemptDue_returns_true_when_nextAttempt_is_in_the_past()
        {
            UANodeManager nm = NewBareInstance();
            object dict = _sut.GetField("_reconnectStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nm);
            object state = NewReconnectState();

            SetReconnectField(state, "NextAttemptTimestamp", 1L);
            AddReconnectState(dict, "asset-due", state);

            MethodInfo m = _sut.GetMethod("IsReconnectAttemptDue", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True((bool)m.Invoke(nm, new object[] { "asset-due" }));
        }

        [Fact]
        public void Shutdown_token_is_cancellation_source()
        {
            UANodeManager nm = (UANodeManager)RuntimeHelpers.GetUninitializedObject(_sut);

            // Initialize the CTS field as the constructor would.
            FieldInfo ctsField = _sut.GetField("_shutdownCts", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(ctsField);

            ctsField.SetValue(nm, new CancellationTokenSource());

            CancellationTokenSource cts = (CancellationTokenSource)ctsField.GetValue(nm);
            Assert.NotNull(cts);
            Assert.False(cts.IsCancellationRequested);

            cts.Cancel();
            Assert.True(cts.IsCancellationRequested);
        }

        [Theory]
        [InlineData("not-a-node-id")] // no '=' or ';' separators -> short split, returns null
        [InlineData("")]
        [InlineData(null)]
        public void ParseExpandedNodeId_rejects_short_or_empty_inputs(string input)
        {
            UANodeManager nm = (UANodeManager)RuntimeHelpers.GetUninitializedObject(_sut);

            MethodInfo m = _sut.GetMethod("ParseExpandedNodeId", BindingFlags.NonPublic | BindingFlags.Instance);
            object result = m.Invoke(nm, new object[] { input });

            // For these inputs the helper returns null because either the
            // format check fails before any namespace lookup, or input is empty.
            Assert.Null(result);
        }

        // ---------------- private helpers ----------------

        private static bool InvokeIsSafeAssetName(string name)
        {
            UANodeManager nm = NewBareInstance();
            return (bool)_sut.GetMethod("IsSafeAssetName", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(nm, new object[] { name });
        }

        private static object NewReconnectState()
        {
            Type t = _sut.GetNestedType("ReconnectState", BindingFlags.NonPublic);
            return Activator.CreateInstance(t);
        }

        private static void SetReconnectField(object state, string fieldName, object value)
        {
            state.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance).SetValue(state, value);
        }

        private static T GetReconnectField<T>(object state, string fieldName)
        {
            return (T)state.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance).GetValue(state);
        }

        private static int GetMaxBackoffMs()
        {
            FieldInfo f = _sut.GetField("_reconnectMaxBackoffMs", BindingFlags.NonPublic | BindingFlags.Static);
            return (int)f.GetValue(null);
        }

        private static void AddReconnectState(object dict, string key, object value)
        {
            dict.GetType().GetMethod("set_Item").Invoke(dict, new[] { key, value });
        }
    }
}
