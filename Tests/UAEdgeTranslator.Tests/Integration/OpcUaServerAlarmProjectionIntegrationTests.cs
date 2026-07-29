namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Concurrent;
    using System.Reflection;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Minimal coverage requested in PR review for the read-only alarm projection
    /// path: condition instance reuse, state mapping (including boolean Id fields),
    /// and Retain/Time/ReceiveTime behavior.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class OpcUaServerAlarmProjectionIntegrationTests : IAsyncLifetime
    {
        private static readonly Type _sut = typeof(UANodeManager);

        private OpcUaServerFixture _fixture;
        private UANodeManager _nodeManager;

        public Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();
            _nodeManager = UANodeManager.Instance;
            Assert.NotNull(_nodeManager);
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            if (_fixture != null)
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
                _fixture = null;
            }
        }

        [Fact]
        public void ReportReadOnlyAlarm_reuses_same_condition_instance_for_same_conditionKey()
        {
            string assetId = "asset-reuse-" + Guid.NewGuid().ToString("N");
            string conditionKey = "pump-overheat";
            EnsureEventSource(assetId);

            AlarmEvent first = new AlarmEvent
            {
                ConditionKey = conditionKey,
                Source = "A",
                ConditionName = "C",
                Severity = 100,
                Message = "first",
                Enabled = true,
                Active = true,
                Acknowledged = false,
                Time = DateTime.UtcNow.AddSeconds(-5)
            };

            AlarmEvent second = new AlarmEvent
            {
                ConditionKey = conditionKey,
                Source = "A",
                ConditionName = "C",
                Severity = 600,
                Message = "second",
                Enabled = true,
                Active = false,
                Acknowledged = true,
                Time = DateTime.UtcNow
            };

            InvokeReportReadOnlyAlarm(assetId, first);
            ConcurrentDictionary<string, AlarmConditionState> conditions = GetAlarmConditions();
            string mapKey = assetId + ":" + conditionKey;
            Assert.True(conditions.TryGetValue(mapKey, out AlarmConditionState initial));

            InvokeReportReadOnlyAlarm(assetId, second);
            Assert.True(conditions.TryGetValue(mapKey, out AlarmConditionState afterSecond));

            Assert.Single(conditions, kvp => kvp.Key == mapKey);
            Assert.Same(initial, afterSecond);
        }

        [Fact]
        public void ReportReadOnlyAlarm_maps_enabled_active_acked_states_and_boolean_ids()
        {
            string assetId = "asset-state-" + Guid.NewGuid().ToString("N");
            EnsureEventSource(assetId);

            AlarmEvent alarm = new AlarmEvent
            {
                ConditionKey = "state-map",
                Source = "Compressor1",
                ConditionName = "HighPressure",
                Severity = 500,
                Message = "Pressure threshold exceeded",
                Enabled = false,
                Active = true,
                Acknowledged = false,
                Time = DateTime.UtcNow
            };

            InvokeReportReadOnlyAlarm(assetId, alarm);
            AlarmConditionState condition = GetCondition(assetId + ":state-map");

            Assert.Equal("Disabled", condition.EnabledState?.Value?.Text);
            Assert.False(condition.EnabledState?.Id?.Value ?? true);

            Assert.Equal("Active", condition.ActiveState?.Value?.Text);
            Assert.True(condition.ActiveState?.Id?.Value ?? false);

            Assert.Equal("Unacknowledged", condition.AckedState?.Value?.Text);
            Assert.False(condition.AckedState?.Id?.Value ?? true);
        }

        [Fact]
        public void ReportReadOnlyAlarm_maps_retain_time_and_receivetime_as_expected()
        {
            string assetId = "asset-time-" + Guid.NewGuid().ToString("N");
            EnsureEventSource(assetId);

            DateTime fixedTime = new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
            DateTime beforeFirst = DateTime.UtcNow;
            InvokeReportReadOnlyAlarm(assetId, new AlarmEvent
            {
                ConditionKey = "retain-false",
                Source = "LineA",
                ConditionName = "Warning",
                Severity = 10,
                Message = "warn",
                Enabled = true,
                Active = false,
                Acknowledged = true,
                Time = fixedTime
            });
            DateTime afterFirst = DateTime.UtcNow;

            AlarmConditionState first = GetCondition(assetId + ":retain-false");
            Assert.False(first.Retain?.Value ?? true);
            Assert.Equal(fixedTime, first.Time?.Value);
            Assert.InRange(first.ReceiveTime?.Value ?? DateTime.MinValue, beforeFirst.AddSeconds(-1), afterFirst.AddSeconds(1));

            DateTime beforeSecond = DateTime.UtcNow;
            InvokeReportReadOnlyAlarm(assetId, new AlarmEvent
            {
                ConditionKey = "retain-true",
                Source = "LineB",
                ConditionName = "Alarm",
                Severity = 900,
                Message = "alarm",
                Enabled = true,
                Active = true,
                Acknowledged = true,
                Time = default
            });
            DateTime afterSecond = DateTime.UtcNow;

            AlarmConditionState second = GetCondition(assetId + ":retain-true");
            Assert.True(second.Retain?.Value ?? false);
            Assert.InRange(second.Time?.Value ?? DateTime.MinValue, beforeSecond.AddSeconds(-1), afterSecond.AddSeconds(1));
            Assert.InRange(second.ReceiveTime?.Value ?? DateTime.MinValue, beforeSecond.AddSeconds(-1), afterSecond.AddSeconds(1));
        }

        private void EnsureEventSource(string assetId)
        {
            var eventSources = (ConcurrentDictionary<string, BaseObjectState>)GetPrivateField("_eventSources");
            eventSources[assetId] = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("event-source-" + assetId, 2),
                BrowseName = new QualifiedName("event-source-" + assetId, 2),
                DisplayName = new LocalizedText("event-source-" + assetId)
            };
        }

        private void InvokeReportReadOnlyAlarm(string assetId, AlarmEvent alarm)
        {
            MethodInfo report = _sut.GetMethod("ReportReadOnlyAlarm", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(report);
            report.Invoke(_nodeManager, new object[] { assetId, alarm });
        }

        private ConcurrentDictionary<string, AlarmConditionState> GetAlarmConditions()
            => (ConcurrentDictionary<string, AlarmConditionState>)GetPrivateField("_alarmConditions");

        private AlarmConditionState GetCondition(string key)
        {
            ConcurrentDictionary<string, AlarmConditionState> map = GetAlarmConditions();
            Assert.True(map.TryGetValue(key, out AlarmConditionState condition), $"Condition key '{key}' not found.");
            return condition;
        }

        private object GetPrivateField(string name)
        {
            FieldInfo field = _sut.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            object value = field.GetValue(_nodeManager);
            Assert.NotNull(value);
            return value;
        }
    }
}
