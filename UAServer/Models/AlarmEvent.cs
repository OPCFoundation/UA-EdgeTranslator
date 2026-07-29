namespace Opc.Ua.Edge.Translator.Models
{
    using System;

    /// <summary>
    /// Protocol-neutral alarm or condition transition supplied by an eventing asset.
    /// The host projects these values to read-only OPC UA Alarms & Conditions.
    /// </summary>
    public sealed class AlarmEvent
    {
        public string ConditionKey { get; init; }

        public string Source { get; init; }

        public string ConditionName { get; init; }

        public string SubConditionName { get; init; }

        public int Category { get; init; }

        public int Severity { get; init; }

        public string Message { get; init; }

        public DateTime Time { get; init; }

        public bool Enabled { get; init; } = true;

        public bool Active { get; init; }

        public bool Acknowledged { get; init; }

        public string ActorId { get; init; }

        public string Comment { get; init; }
    }
}