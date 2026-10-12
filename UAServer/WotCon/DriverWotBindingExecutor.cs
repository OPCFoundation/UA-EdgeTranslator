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

        private readonly ConcurrentDictionary<string, Lazy<Task<AssetConnection>>> _connections =
            new(StringComparer.OrdinalIgnoreCase);

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
            if (!_drivers.TryGetByUri(baseUri, out IProtocolDriver driver))
            {
                throw new InvalidOperationException($"No protocol driver is loaded for '{baseUri}'.");
            }

            ThingDescription td = _thingDescriptions.GetOrCreate(baseUri);
            AssetConnection connection = await GetConnectionAsync(driver, td, cancellationToken).ConfigureAwait(false);

            object rawForm = Newtonsoft.Json.Linq.JToken.Parse(form.Addressing.Metadata[DriverWotBinder.FormMetadataKey]);
            AssetTag tag = driver.CreateTag(td, rawForm, td.Name, connection.UnitId, form.AffordanceName, null, null);

            return new DriverWotBindingChannel(form, connection.Asset, tag, td.Name, _isAssetAccessBlocked);
        }

        private Task<AssetConnection> GetConnectionAsync(IProtocolDriver driver, ThingDescription td, CancellationToken cancellationToken)
        {
            Lazy<Task<AssetConnection>> lazy = _connections.GetOrAdd(
                td.Base,
                _ => new Lazy<Task<AssetConnection>>(() => driver.CreateAndConnectAssetAsync(td, cancellationToken)));

            Task<AssetConnection> task = lazy.Value;
            if (task.IsFaulted || task.IsCanceled)
            {
                // Do not cache a failed connect; the next activation retries.
                _connections.TryRemove(new KeyValuePair<string, Lazy<Task<AssetConnection>>>(td.Base, lazy));
            }

            return task;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (Lazy<Task<AssetConnection>> lazy in _connections.Values)
            {
                if (!lazy.IsValueCreated || !lazy.Value.IsCompletedSuccessfully)
                {
                    continue;
                }

                try
                {
                    await lazy.Value.Result.Asset.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning(ex, "Failed to dispose asset connection.");
                }
            }

            _connections.Clear();
        }
    }
}
