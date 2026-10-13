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
        // One gate per asset, kept for as long as the asset has listeners. The gate
        // serializes the whole start/stop transition - the count change and the
        // driver call - so a stop for the last old listener cannot run after a new
        // listener has already started the subscription again.
        private static readonly Dictionary<IEventingAsset, Gate> s_gates = new(ReferenceEqualityComparer.Instance);

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

            Gate gate = Acquire(_asset);
            lock (gate)
            {
                if (gate.Listeners == 0)
                {
                    try
                    {
                        _asset.StartEventSubscription();
                    }
                    catch
                    {
                        ReleaseGate(_asset, gate);
                        throw;
                    }
                }

                gate.Listeners++;
            }

            _asset.AlarmReceived += OnAlarm;
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

            Gate gate;
            lock (s_gates)
            {
                if (!s_gates.TryGetValue(_asset, out gate))
                {
                    return;
                }
            }

            lock (gate)
            {
                gate.Listeners--;
                if (gate.Listeners == 0)
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

            ReleaseGate(_asset, gate);
        }

        /// <summary>Returns the gate of <paramref name="asset"/>, holding a reference to it.</summary>
        private static Gate Acquire(IEventingAsset asset)
        {
            lock (s_gates)
            {
                if (!s_gates.TryGetValue(asset, out Gate gate))
                {
                    gate = new Gate();
                    s_gates[asset] = gate;
                }

                gate.References++;
                return gate;
            }
        }

        /// <summary>Drops a reference to <paramref name="gate"/>, removing it when unused.</summary>
        private static void ReleaseGate(IEventingAsset asset, Gate gate)
        {
            lock (s_gates)
            {
                gate.References--;
                if (gate.References == 0 && s_gates.TryGetValue(asset, out Gate current) && ReferenceEquals(current, gate))
                {
                    s_gates.Remove(asset);
                }
            }
        }

        /// <summary>
        /// Serializes the event subscription of one asset. <see cref="Listeners"/> is
        /// guarded by the gate itself; <see cref="References"/> by <c>s_gates</c>.
        /// </summary>
        private sealed class Gate
        {
            public int Listeners;

            public int References;
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
