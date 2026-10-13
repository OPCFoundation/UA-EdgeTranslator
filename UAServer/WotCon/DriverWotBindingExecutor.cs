namespace Opc.Ua.Edge.Translator.WotCon
{
    using global::Opc.Ua.WotCon.Bindings;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Serilog;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Executes forms planned by <see cref="DriverWotBinder"/> on the protocol
    /// drivers loaded by UA Edge Translator.
    /// </summary>
    /// <remarks>
    /// Drivers connect per asset, not per form, so one connected
    /// <see cref="IAsset"/> is shared by every channel that targets the same TD
    /// base. Drivers parse their own form vocabulary and some need the whole Thing
    /// Description to do so; <see cref="ThingDescriptionCache"/> supplies it.
    /// </remarks>
    public sealed class DriverWotBindingExecutor : IWotBindingExecutor, IAsyncDisposable
    {
        private readonly ProtocolDriverRegistry _drivers;

        private readonly ThingDescriptionCache _thingDescriptions;

        // One shared connection per TD base, reference-counted by the channels using it.
        // Guarded by _gate; the connect itself runs outside the lock.
        private readonly Dictionary<string, SharedConnection> _connections = new(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new();

        private readonly Func<bool> _isAssetAccessBlocked;

        public DriverWotBindingExecutor(
            ProtocolDriverRegistry drivers,
            ThingDescriptionCache thingDescriptions,
            Func<bool> isAssetAccessBlocked = null)
        {
            _drivers = drivers ?? throw new ArgumentNullException(nameof(drivers));
            _thingDescriptions = thingDescriptions ?? throw new ArgumentNullException(nameof(thingDescriptions));
            _isAssetAccessBlocked = isAssetAccessBlocked ?? ProvisioningGate.IsAssetAccessBlocked;
        }

        public WotBindingIdentity Identity => DriverWotBinder.BinderIdentity;

        public bool CanExecute(WotCompiledForm form)
        {
            return form is not null && string.Equals(form.Binding.Id, Identity.Id, StringComparison.Ordinal);
        }

        public async ValueTask<IWotBindingChannel> ActivateAsync(
            WotCompiledForm form, WotExecutorContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(form);

            string baseUri = form.Endpoint.BaseUri;

            // The registry validated the endpoint, but only by comparing host names
            // literally; reject equivalent spellings of a blocked address too.
            AssetEndpointPolicy.ThrowIfBlocked(baseUri);
            AssetEndpointPolicy.ThrowIfBlocked(form.Addressing.Target);

            if (!_drivers.TryGetByUri(baseUri, out IProtocolDriver driver))
            {
                throw new InvalidOperationException($"No protocol driver is loaded for '{baseUri}'.");
            }

            // Look the TD up by the registry resource the form was planned for: several
            // resources may share one base. The connection itself is still per base.
            ThingDescription td = _thingDescriptions.GetOrCreate(_thingDescriptions.ResourceOf(form), baseUri);
            SharedConnection shared = Acquire(driver, td, baseUri);
            AssetConnection connection;
            try
            {
                // The shared connect is not tied to this activation; only this waiter stops
                // waiting when canceled. Its reference is released in the background, so a
                // hung connect is disposed once it finishes without blocking the caller.
                connection = await shared.Connect.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _ = ReleaseAsync(baseUri, shared).AsTask();
                throw;
            }
            catch
            {
                await ReleaseAsync(baseUri, shared).ConfigureAwait(false);
                throw;
            }

            try
            {
                object rawForm = Newtonsoft.Json.Linq.JToken.Parse(form.Addressing.Metadata[DriverWotBinder.FormMetadataKey]);
                // Only property channels read and write a tag. Actions and events need none,
                // and event-only drivers (such as OPC A&E) refuse to create one.
                AssetTag tag = form.AffordanceKind == WotAffordanceKind.Property
                    ? driver.CreateTag(td, rawForm, td.Name, connection.UnitId, form.AffordanceName, null, null)
                    : null;

                return new DriverWotBindingChannel(form, connection.Asset, tag, td.Name, _isAssetAccessBlocked, () => ReleaseAsync(baseUri, shared));
            }
            catch
            {
                await ReleaseAsync(baseUri, shared).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Returns the shared connection for <paramref name="td"/>, holding a reference to it.
        /// A connection whose connect failed is replaced, so a later activation retries
        /// instead of getting the cached failure.
        /// </summary>
        private SharedConnection Acquire(IProtocolDriver driver, ThingDescription td, string key)
        {
            lock (_gate)
            {
                if (!_connections.TryGetValue(key, out SharedConnection shared)
                    || shared.Connect.IsFaulted || shared.Connect.IsCanceled)
                {
                    // Connect without the activation's cancellation token: the connection
                    // is shared, so one caller cancelling must not fail the others.
                    shared = new SharedConnection(driver.CreateAndConnectAssetAsync(td, CancellationToken.None));
                    _connections[key] = shared;
                }

                shared.References++;
                return shared;
            }
        }

        /// <summary>
        /// Drops a reference to <paramref name="shared"/>. The last reference disconnects
        /// the asset and forgets the connection.
        /// </summary>
        private async ValueTask ReleaseAsync(string key, SharedConnection shared)
        {
            lock (_gate)
            {
                shared.References--;
                if (shared.References > 0)
                {
                    return;
                }

                if (_connections.TryGetValue(key, out SharedConnection current) && ReferenceEquals(current, shared))
                {
                    _connections.Remove(key);
                }
            }

            await DisposeConnectionAsync(shared).ConfigureAwait(false);
        }

        private static async ValueTask DisposeConnectionAsync(SharedConnection shared)
        {
            AssetConnection connection;
            try
            {
                connection = await shared.Connect.ConfigureAwait(false);
            }
            catch
            {
                // A failed connect has nothing to release.
                return;
            }

            try
            {
                await connection.Asset.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to dispose asset connection.");
            }
        }

        /// <summary>A connection to one asset and the number of channels using it.</summary>
        private sealed class SharedConnection
        {
            public SharedConnection(Task<AssetConnection> connect)
            {
                Connect = connect;
            }

            public Task<AssetConnection> Connect { get; }

            public int References;
        }

        public async ValueTask DisposeAsync()
        {
            List<SharedConnection> connections;
            lock (_gate)
            {
                connections = new List<SharedConnection>(_connections.Values);
                _connections.Clear();
            }

            foreach (SharedConnection shared in connections)
            {
                await DisposeConnectionAsync(shared).ConfigureAwait(false);
            }
        }
    }
}
