namespace Opc.Ua.Edge.Translator.Interfaces
{
    using Opc.Ua.Edge.Translator.Models;
    using System;

    /// <summary>
    /// Optional contract for assets that push alarms or events. Existing polling
    /// drivers remain unchanged because event delivery is additive to IAsset.
    /// </summary>
    public interface IEventingAsset : IAsset
    {
        event EventHandler<AlarmEvent> AlarmReceived;

        void StartEventSubscription();

        void RefreshEventSubscription();

        void StopEventSubscription();
    }
}