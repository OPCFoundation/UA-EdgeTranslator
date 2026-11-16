namespace Opc.Ua.Edge.Translator.WotCon
{
    using global::Opc.Ua.WotCon.Server.Registry;
    using global::Opc.Ua.XRegistry.Server;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The WoT registry's document store, with immutable content leases on every platform.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The WoT Connectivity materialization refuses to publish anything unless the registry
    /// store can lease the exact bytes a Thing was prepared from. The package's own file
    /// stores only grant those leases on Windows, where an open file cannot be replaced or
    /// deleted; on Linux, which is what the container runs, every refresh failed with
    /// "The configured owners cannot isolate the requested publication" and no registry
    /// Thing reached the address space.
    /// </para>
    /// <para>
    /// This store keeps the documents in the package's file layout (<c>{sha256}.bin</c>,
    /// through <see cref="WotBlobResourceStore"/>) and grants the leases itself: a lease holds
    /// a copy of the document's bytes, and while a key is leased, deleting or overwriting it
    /// fails just like it does on Windows. That covers every writer, because the registry
    /// folder is owned by one process at a time (<see cref="FileWotRegistryStore"/> holds a
    /// lock file) and all of its writes come through this store.
    /// </para>
    /// <para>
    /// An injected store must not use the registry's own <c>blobs</c> folder: before the
    /// first manifest is written, <see cref="FileWotRegistryStore"/> treats anything there
    /// as an interrupted commit and refuses to start. <see cref="OpenForRegistry"/> therefore
    /// uses a <c>documents</c> folder and copies the documents of an existing registry into
    /// it once.
    /// </para>
    /// </remarks>
    public sealed class LeasingWotBlobResourceStore : IWotRegistryContentLeaseProvider, IDisposable
    {
        private readonly WotBlobResourceStore _documents;

        // Serializes lease acquisition with writes and deletes, so a lease always captures
        // the bytes the key maps to and nothing replaces them before the lease is counted.
        private readonly SemaphoreSlim _mutation = new(1, 1);

        // Leased keys, their captured bytes and how many leases hold them; guarded by itself.
        private readonly Dictionary<string, LeasedDocument> _leases = new(StringComparer.Ordinal);

        private int _disposed;

        /// <summary>
        /// Opens the store over <paramref name="documentsFolder"/>.
        /// </summary>
        public LeasingWotBlobResourceStore(string documentsFolder)
        {
            if (string.IsNullOrEmpty(documentsFolder))
            {
                throw new ArgumentException("A documents folder is required.", nameof(documentsFolder));
            }

            _documents = new WotBlobResourceStore(documentsFolder);
        }

        /// <summary>
        /// The folder below a registry root that holds the documents of this store.
        /// </summary>
        public const string DocumentsFolderName = "documents";

        /// <summary>
        /// Opens the document store of the <see cref="FileWotRegistryStore"/> rooted at
        /// <paramref name="registryFolder"/>. The first time, the documents the package's
        /// default store kept in <c>blobs</c> are copied over, so an existing registry
        /// keeps its content; the <c>blobs</c> folder itself is left to the package.
        /// </summary>
        public static LeasingWotBlobResourceStore OpenForRegistry(string registryFolder)
        {
            if (string.IsNullOrEmpty(registryFolder))
            {
                throw new ArgumentException("A registry folder is required.", nameof(registryFolder));
            }

            string documents = Path.Combine(registryFolder, DocumentsFolderName);
            MigrateLegacyDocuments(Path.Combine(registryFolder, "blobs"), documents);
            return new LeasingWotBlobResourceStore(documents);
        }

        private static void MigrateLegacyDocuments(string legacyFolder, string documentsFolder)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(documentsFolder));
            string pending = DocumentsFolderName + ".migrating-";

            // Remove what an interrupted migration left behind; its source is still intact.
            if (Directory.Exists(parent))
            {
                foreach (string leftover in Directory.GetDirectories(parent, pending + "*"))
                {
                    Directory.Delete(leftover, recursive: true);
                }
            }

            if (Directory.Exists(documentsFolder) || !Directory.Exists(legacyFolder))
            {
                return;
            }

            // Copy into a scratch folder and publish it with one rename, so the documents
            // folder only ever appears complete.
            string scratch = Path.Combine(parent, pending + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            foreach (string file in Directory.GetFiles(legacyFolder, "*.bin"))
            {
                File.Copy(file, Path.Combine(scratch, Path.GetFileName(file)));
            }

            Directory.Move(scratch, documentsFolder);
        }

        /// <inheritdoc/>
        public bool SupportsImmutableContentLeases => Volatile.Read(ref _disposed) == 0;

        /// <inheritdoc/>
        public async ValueTask<IWotRegistryContentLease> AcquireContentLeaseAsync(string resourceKey, CancellationToken cancellationToken = default)
        {
            RequireKey(resourceKey);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            await _mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_leases)
                {
                    if (_leases.TryGetValue(resourceKey, out LeasedDocument leased))
                    {
                        leased.Holders++;
                        return new Lease(this, resourceKey, leased.Content);
                    }
                }

                byte[] content = await ReadDocumentAsync(resourceKey, cancellationToken).ConfigureAwait(false);
                lock (_leases)
                {
                    _leases.Add(resourceKey, new LeasedDocument(content));
                }

                return new Lease(this, resourceKey, content);
            }
            finally
            {
                _mutation.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> ReadAsync(string resourceKey, long offset, int count, CancellationToken ct = default)
        {
            return _documents.ReadAsync(resourceKey, offset, count, ct);
        }

        /// <inheritdoc/>
        public ValueTask<long> GetLengthAsync(string resourceKey, CancellationToken ct = default)
        {
            return _documents.GetLengthAsync(resourceKey, ct);
        }

        /// <inheritdoc/>
        public async ValueTask WriteAsync(string resourceKey, long offset, ByteString data, CancellationToken ct = default)
        {
            RequireKey(resourceKey);
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                RequireNotLeased(resourceKey, "overwritten");
                await _documents.WriteAsync(resourceKey, offset, data, ct).ConfigureAwait(false);
            }
            finally
            {
                _mutation.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default)
        {
            RequireKey(resourceKey);
            await _mutation.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                RequireNotLeased(resourceKey, "deleted");
                return await _documents.DeleteAsync(resourceKey, ct).ConfigureAwait(false);
            }
            finally
            {
                _mutation.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _documents.Dispose();
            _mutation.Dispose();
        }

        private async ValueTask<byte[]> ReadDocumentAsync(string resourceKey, CancellationToken cancellationToken)
        {
            long length = await _documents.GetLengthAsync(resourceKey, cancellationToken).ConfigureAwait(false);
            if (length < 0)
            {
                throw new FileNotFoundException("The WoT registry document '" + resourceKey + "' does not exist.");
            }

            if (length > Array.MaxLength)
            {
                throw new IOException("The WoT registry document '" + resourceKey + "' is too large to lease.");
            }

            byte[] content = new byte[length];
            int read = 0;
            while (read < content.Length)
            {
                ByteString chunk = await _documents.ReadAsync(resourceKey, read, content.Length - read, cancellationToken).ConfigureAwait(false);
                if (chunk.IsNull)
                {
                    throw new FileNotFoundException("The WoT registry document '" + resourceKey + "' was removed while it was being leased.");
                }

                if (chunk.Length == 0)
                {
                    throw new EndOfStreamException("The WoT registry document '" + resourceKey + "' was truncated while it was being leased.");
                }

                chunk.Span.CopyTo(content.AsSpan(read));
                read += chunk.Length;
            }

            return content;
        }

        private void RequireNotLeased(string resourceKey, string operation)
        {
            lock (_leases)
            {
                if (_leases.ContainsKey(resourceKey))
                {
                    throw new IOException("The WoT registry document '" + resourceKey + "' is leased and cannot be " + operation + ".");
                }
            }
        }

        private void Release(string resourceKey)
        {
            lock (_leases)
            {
                if (_leases.TryGetValue(resourceKey, out LeasedDocument leased) && --leased.Holders == 0)
                {
                    _leases.Remove(resourceKey);
                }
            }
        }

        private static void RequireKey(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                throw new ArgumentException("A resource key is required.", nameof(resourceKey));
            }
        }

        private sealed class LeasedDocument
        {
            public LeasedDocument(byte[] content)
            {
                Content = content;
            }

            public byte[] Content { get; }

            public int Holders { get; set; } = 1;
        }

        private sealed class Lease : IWotRegistryContentLease
        {
            private readonly LeasingWotBlobResourceStore _owner;

            private readonly byte[] _content;

            private int _disposed;

            public Lease(LeasingWotBlobResourceStore owner, string resourceKey, byte[] content)
            {
                _owner = owner;
                _content = content;
                ResourceKey = resourceKey;
            }

            public string ResourceKey { get; }

            public long ContentLength => _content.Length;

            public ValueTask<ByteString> ReadAsync(long offset, int count, CancellationToken cancellationToken = default)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(offset);
                ArgumentOutOfRangeException.ThrowIfNegative(count);
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                cancellationToken.ThrowIfCancellationRequested();

                if (offset >= _content.Length || count == 0)
                {
                    return new ValueTask<ByteString>(new ByteString(ReadOnlyMemory<byte>.Empty));
                }

                int take = (int)Math.Min(count, _content.Length - offset);
                return new ValueTask<ByteString>(new ByteString(_content.AsMemory((int)offset, take)));
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.Release(ResourceKey);
                }
            }
        }
    }
}
