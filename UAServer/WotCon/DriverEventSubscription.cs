namespace Opc.Ua.Edge.Translator.WotCon
{
    using global::Opc.Ua.WotCon.Bindings;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Forwards the events an <see cref="IEventingAsset"/> pushes to a WoT
    /// <c>subscribeevent</c> listener for the lifetime of the subscription.
    /// </summary>
    /// <remarks>
    /// Each <see cref="AlarmEvent"/> becomes one notification whose event data
    /// members are named after the alarm's fields, so a TD event <c>data</c> schema
    /// can select them by member path (for example <c>Severity</c> or <c>Message</c>).
    /// The driver's event subscription is shared per asset, so it is started on the
    /// first listener and stopped when the last one goes away.
    /// </remarks>
    public sealed class DriverEventSubscription : IWotSubscription
    {
        private static readonly Dictionary<IEventingAsset, int> s_listenerCounts = new(ReferenceEqualityComparer.Instance);

        private readonly IEventingAsset _asset;

        private readonly Action<WotNotification> _onEvent;

        private readonly string _assetName;

        private int _disposed;

        public DriverEventSubscription(WotCompiledForm form, IEventingAsset asset, Action<WotNotification> onEvent, string assetName)
        {
            Form = form;
            _asset = asset;
            _onEvent = onEvent;
            _assetName = assetName;

            _asset.AlarmReceived += OnAlarm;

            bool first;
            lock (s_listenerCounts)
            {
                s_listenerCounts.TryGetValue(_asset, out int count);
                s_listenerCounts[_asset] = count + 1;
                first = count == 0;
            }

            if (first)
            {
                try
                {
                    _asset.StartEventSubscription();
                }
                catch
                {
                    Release();
                    throw;
                }
            }
        }

        public WotCompiledForm Form { get; }

        public ValueTask DisposeAsync()
        {
            Release();
            return default;
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _asset.AlarmReceived -= OnAlarm;

            bool last;
            lock (s_listenerCounts)
            {
                s_listenerCounts.TryGetValue(_asset, out int count);
                last = count <= 1;
                if (last)
                {
                    s_listenerCounts.Remove(_asset);
                }
                else
                {
                    s_listenerCounts[_asset] = count - 1;
                }
            }

            if (last)
            {
                try
                {
                    _asset.StopEventSubscription();
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning(ex, "Failed to stop the event subscription of asset {AssetId}.", _assetName);
                }
            }
        }

        private void OnAlarm(object sender, AlarmEvent alarm)
        {
            if (alarm == null || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                _onEvent(ToNotification(alarm));
            }
            catch (Exception ex)
            {
                // A faulty listener must not break the driver's event pump.
                Log.Logger.Warning(ex, "Failed to forward an event from asset {AssetId}.", _assetName);
            }
        }

        public static WotNotification ToNotification(AlarmEvent alarm)
        {
            DateTime time = alarm.Time == default ? DateTime.UtcNow : alarm.Time;

            Dictionary<string, WotEventData> members = new(StringComparer.Ordinal)
            {
                ["ConditionKey"] = Member(alarm.ConditionKey, time),
                ["Source"] = Member(alarm.Source, time),
                ["ConditionName"] = Member(alarm.ConditionName, time),
                ["SubConditionName"] = Member(alarm.SubConditionName, time),
                ["Category"] = Member(alarm.Category, time),
                ["Severity"] = Member(alarm.Severity, time),
                ["Message"] = Member(alarm.Message, time),
                ["Time"] = Member(time, time),
                ["Enabled"] = Member(alarm.Enabled, time),
                ["Active"] = Member(alarm.Active, time),
                ["Acknowledged"] = Member(alarm.Acknowledged, time),
                ["ActorId"] = Member(alarm.ActorId, time),
                ["Comment"] = Member(alarm.Comment, time)
            };

            DataValue message = new(new Variant(alarm.Message ?? string.Empty), StatusCodes.Good, time);
            return new WotNotification(message, data: new WotEventData(members));
        }

        private static WotEventData Member(string value, DateTime time) =>
            new(new DataValue(value == null ? Variant.Null : new Variant(value), StatusCodes.Good, time));

        private static WotEventData Member(int value, DateTime time) =>
            new(new DataValue(new Variant(value), StatusCodes.Good, time));

        private static WotEventData Member(bool value, DateTime time) =>
            new(new DataValue(new Variant(value), StatusCodes.Good, time));

        private static WotEventData Member(DateTime value, DateTime time) =>
            new(new DataValue(new Variant((DateTimeUtc)value), StatusCodes.Good, time));
    }
}
