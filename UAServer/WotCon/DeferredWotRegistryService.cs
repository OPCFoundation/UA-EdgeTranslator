namespace Opc.Ua.Edge.Translator.WotCon
{
    using global::Opc.Ua.WotCon;
    using global::Opc.Ua.WotCon.Server.Registry;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Wraps the registry service so that its first load is done by
    /// <see cref="WotRegistryHost"/>, not by the registry node manager.
    /// </summary>
    /// <remarks>
    /// <c>WotRegistryNodeManager.CreateAddressSpaceAsync</c> calls
    /// <see cref="IWotRegistryService.InitializeAsync"/> and then materializes every
    /// persisted Thing while <c>NodeManagerLifecycle</c> still holds its lifecycle
    /// semaphore for the registry node manager's own registration. Materializing adds
    /// projection node managers through that same lifecycle, so with persisted Things
    /// present the server never finishes starting. While deferred, the
    /// node manager's initialize call is a no-op and it starts against the empty
    /// registry; <see cref="WotRegistryHost"/> loads and materializes the persisted
    /// content after the registration returned.
    /// </remarks>
    internal sealed class DeferredWotRegistryService : IWotRegistryService
    {
        private readonly IWotRegistryService _inner;

        private volatile bool _initializeDeferred = true;

        public DeferredWotRegistryService(IWotRegistryService inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public WotRegistrySnapshot Current => _inner.Current;

        public WotRegistryPersistenceBounds Bounds => _inner.Bounds;

        public event EventHandler<WotRegistryChangedEventArgs> Changed
        {
            add => _inner.Changed += value;
            remove => _inner.Changed -= value;
        }

        /// <summary>Loads the persisted registry and ends the deferral.</summary>
        public ValueTask LoadAsync(CancellationToken cancellationToken = default)
        {
            _initializeDeferred = false;
            return _inner.InitializeAsync(cancellationToken);
        }

        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
            _initializeDeferred ? ValueTask.CompletedTask : _inner.InitializeAsync(cancellationToken);

        public ValueTask<WotResourceGroup> GetOrCreateGroupAsync(string groupId, WoTDocumentKindEnum kind, string name = null, CancellationToken cancellationToken = default) =>
            _inner.GetOrCreateGroupAsync(groupId, kind, name, cancellationToken);

        public ValueTask<WotResourceGroup> TryCreateGroupAsync(string groupId, WoTDocumentKindEnum kind, string name = null, CancellationToken cancellationToken = default) =>
            _inner.TryCreateGroupAsync(groupId, kind, name, cancellationToken);

        public ValueTask<WotRegistryMutationResult> DeleteGroupAsync(string groupId, long? expectedEpoch = null, CancellationToken cancellationToken = default) =>
            _inner.DeleteGroupAsync(groupId, expectedEpoch, cancellationToken);

        public ValueTask<(WotResource Resource, bool Created)> GetOrCreateResourceAsync(string groupId, string resourceId, WoTDocumentKindEnum kind, CancellationToken cancellationToken = default) =>
            _inner.GetOrCreateResourceAsync(groupId, resourceId, kind, cancellationToken);

        public ValueTask<WotResource> TryCreateResourceAsync(string groupId, string resourceId, WoTDocumentKindEnum kind, CancellationToken cancellationToken = default) =>
            _inner.TryCreateResourceAsync(groupId, resourceId, kind, cancellationToken);

        public ValueTask<WoTValidationOutcomeDataType> ValidateResourceAsync(string groupId, string resourceId, CancellationToken cancellationToken = default) =>
            _inner.ValidateResourceAsync(groupId, resourceId, cancellationToken);

        public ValueTask<WotRegistryMutationResult> UpsertResourceAsync(WotUpsertResourceRequest request, CancellationToken cancellationToken = default) =>
            _inner.UpsertResourceAsync(request, cancellationToken);

        public ValueTask<ByteString> ReadContentAsync(WotResourceVersion version, CancellationToken cancellationToken = default) =>
            _inner.ReadContentAsync(version, cancellationToken);

        public ValueTask<ByteString> ReadContentChunkAsync(string digestHex, long offset, int count, CancellationToken cancellationToken = default) =>
            _inner.ReadContentChunkAsync(digestHex, offset, count, cancellationToken);

        public ValueTask<WotRegistryMutationResult> DeleteResourceAsync(string groupId, string resourceId, long? expectedEpoch = null, CancellationToken cancellationToken = default) =>
            _inner.DeleteResourceAsync(groupId, resourceId, expectedEpoch, cancellationToken);

        public ValueTask<WotRegistryMutationResult> SetDefaultVersionAsync(string groupId, string resourceId, string versionId, long? expectedEpoch = null, CancellationToken cancellationToken = default) =>
            _inner.SetDefaultVersionAsync(groupId, resourceId, versionId, expectedEpoch, cancellationToken);

        public ValueTask<WotRegistryMutationResult> SetEnabledAsync(string groupId, string resourceId, bool enabled, long? expectedEpoch = null, CancellationToken cancellationToken = default) =>
            _inner.SetEnabledAsync(groupId, resourceId, enabled, expectedEpoch, cancellationToken);

        public ValueTask<WotRegistryMutationResult> AddRegistryLabelAsync(string key, string value, long? expectedEpoch = null, CancellationToken cancellationToken = default) =>
            _inner.AddRegistryLabelAsync(key, value, expectedEpoch, cancellationToken);

        public ValueTask<WotRegistryMutationResult> RemoveRegistryLabelAsync(string key, long? expectedEpoch = null, CancellationToken cancellationToken = default) =>
            _inner.RemoveRegistryLabelAsync(key, expectedEpoch, cancellationToken);

        public ValueTask<WotRegistryMutationResult> AddGroupLabelAsync(string groupId, string key, string value, long? expectedEpoch = null, CancellationToken cancellationToken = default) =>
            _inner.AddGroupLabelAsync(groupId, key, value, expectedEpoch, cancellationToken);

        public ValueTask<WotRegistryMutationResult> RemoveGroupLabelAsync(string groupId, string key, long? expectedEpoch = null, CancellationToken cancellationToken = default) =>
            _inner.RemoveGroupLabelAsync(groupId, key, expectedEpoch, cancellationToken);

        public ValueTask<WotRegistryMutationResult> AddResourceLabelAsync(string groupId, string resourceId, string key, string value, long? expectedEpoch = null, CancellationToken cancellationToken = default) =>
            _inner.AddResourceLabelAsync(groupId, resourceId, key, value, expectedEpoch, cancellationToken);

        public ValueTask<WotRegistryMutationResult> RemoveResourceLabelAsync(string groupId, string resourceId, string key, long? expectedEpoch = null, CancellationToken cancellationToken = default) =>
            _inner.RemoveResourceLabelAsync(groupId, resourceId, key, expectedEpoch, cancellationToken);

        public ValueTask ApplyProjectionResultsAsync(IReadOnlyList<WotResourceProjection> projections, CancellationToken cancellationToken = default) =>
            _inner.ApplyProjectionResultsAsync(projections, cancellationToken);
    }
}
