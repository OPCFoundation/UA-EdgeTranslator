namespace Opc.Ua.Edge.Translator.WotCon
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Serilog;
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Reconnects the assets behind WoT-Con binding channels, the way the v1.02 node
    /// manager reconnects its polled assets: exponential backoff from 1 s, doubling up
    /// to 60 s, and no asset I/O inside a backoff window. An asset connection is
    /// shared by all its channels, so the state is kept per asset.
    /// </summary>
    public static class WotAssetReconnector
    {
        private const int InitialBackoffMs = 1000;

        private const int MaxBackoffMs = 60_000;

        private sealed class State
        {
            public readonly SemaphoreSlim Gate = new(1, 1);

            public long NextAttemptTimestamp;

            public int CurrentBackoffMs = InitialBackoffMs;

            public int ConsecutiveFailures;
        }

        private static readonly ConcurrentDictionary<IAsset, State> s_states = new(System.Collections.Generic.ReferenceEqualityComparer.Instance);

        /// <summary>True when the asset is not inside a reconnect backoff window.</summary>
        public static bool IsAttemptDue(IAsset asset)
        {
            return asset == null
                || !s_states.TryGetValue(asset, out State state)
                || Stopwatch.GetTimestamp() >= Volatile.Read(ref state.NextAttemptTimestamp);
        }

        /// <summary>Clears the backoff after a successful operation.</summary>
        public static void Reset(IAsset asset)
        {
            if (asset != null)
            {
                s_states.TryRemove(asset, out _);
            }
        }

        /// <summary>Forgets an asset whose connection was released.</summary>
        public static void Forget(IAsset asset) => Reset(asset);

        /// <summary>
        /// Reconnects <paramref name="asset"/> unless another channel is already doing it
        /// or the backoff window has not elapsed; schedules the next attempt on failure.
        /// </summary>
        public static async Task TryReconnectAsync(IAsset asset, string assetName, CancellationToken cancellationToken = default)
        {
            if (asset == null)
            {
                return;
            }

            State state = s_states.GetOrAdd(asset, _ => new State());
            if (Stopwatch.GetTimestamp() < Volatile.Read(ref state.NextAttemptTimestamp)
                || !await state.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                Program.Telemetry?.AssetReconnects.Add(1);
                Log.Logger.Information("Trying to reconnect to asset {AssetId} (attempt {Attempt})", assetName, state.ConsecutiveFailures + 1);

                if (!TryParseEndpoint(asset.GetRemoteEndpoint(), out string host, out int port))
                {
                    Log.Logger.Warning("Asset {AssetId} has no usable remote endpoint [{Endpoint}]; skipping reconnect.", assetName, asset.GetRemoteEndpoint());
                    ScheduleNext(state);
                    return;
                }

                await asset.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                await asset.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

                if (asset.IsConnected)
                {
                    if (asset is IEventingAsset eventingAsset)
                    {
                        eventingAsset.StartEventSubscription();
                    }

                    Log.Logger.Information("Reconnected to asset {AssetId}.", assetName);
                    s_states.TryRemove(asset, out _);
                    return;
                }

                Program.Telemetry?.AssetReconnectFailures.Add(1);
                ScheduleNext(state);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Program.Telemetry?.AssetReconnectFailures.Add(1);
                Log.Logger.Warning(ex, "Reconnect attempt for asset {AssetId} failed.", assetName);
                ScheduleNext(state);
            }
            finally
            {
                state.Gate.Release();
            }
        }

        /// <summary>
        /// Reads host and port from a URI such as <c>eip://10.0.0.5:44818/1,0/</c> or a
        /// legacy <c>host[:port]</c> endpoint; port 0 means the driver's default.
        /// </summary>
        public static bool TryParseEndpoint(string endpoint, out string host, out int port)
        {
            host = null;
            port = 0;
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return false;
            }

            if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                host = uri.Host;
                port = uri.IsDefaultPort ? 0 : uri.Port;
                return true;
            }

            if (endpoint.Contains("://", StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = endpoint.Split(':');
            host = parts[0];
            if (parts.Length > 1 && !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
            {
                port = 0;
            }

            return !string.IsNullOrWhiteSpace(host);
        }

        private static void ScheduleNext(State state)
        {
            state.ConsecutiveFailures++;
            int backoffMs = Math.Min(MaxBackoffMs, state.CurrentBackoffMs);
            Volatile.Write(ref state.NextAttemptTimestamp, Stopwatch.GetTimestamp() + (long)(backoffMs * (Stopwatch.Frequency / 1000.0)));
            state.CurrentBackoffMs = Math.Min(MaxBackoffMs, state.CurrentBackoffMs * 2);
        }
    }
}
