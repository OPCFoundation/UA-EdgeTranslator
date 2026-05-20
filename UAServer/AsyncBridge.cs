namespace Opc.Ua.Edge.Translator
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Centralized, deadlock-safe sync ↔ async bridge for the few places where
    /// the OPC UA SDK forces a synchronous boundary (CustomNodeManager2 overrides,
    /// FileTypeState callbacks, IProtocolDriver.Discover) but the work is async.
    ///
    /// The helper offloads the awaited work onto the thread pool via Task.Run,
    /// which deliberately discards any captured SynchronizationContext from the
    /// caller. That is what prevents the classic "GetAwaiter().GetResult() inside
    /// a captured context deadlocks" failure mode if this code is ever hosted in
    /// an environment that installs one (ASP.NET Framework, WinForms, WPF).
    ///
    /// Use at the OUTERMOST sync↔async boundary only — never inside async code.
    /// </summary>
    public static class AsyncBridge
    {
        public static void RunSync(Func<Task> asyncWork)
        {
            ArgumentNullException.ThrowIfNull(asyncWork);

            Task.Run(asyncWork).GetAwaiter().GetResult();
        }

        public static T RunSync<T>(Func<Task<T>> asyncWork)
        {
            ArgumentNullException.ThrowIfNull(asyncWork);

            return Task.Run(asyncWork).GetAwaiter().GetResult();
        }
    }
}
