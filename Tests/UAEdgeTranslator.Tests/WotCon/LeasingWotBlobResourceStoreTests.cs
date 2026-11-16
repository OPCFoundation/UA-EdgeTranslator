namespace Opc.Ua.Edge.Translator.Tests.WotCon
{
    using global::Opc.Ua.WotCon;
    using global::Opc.Ua.WotCon.Server.Registry;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.WotCon;
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// The WoT registry only materializes Things when its document store grants immutable
    /// content leases; the package's own stores grant them on Windows only, so the registry
    /// stayed empty in the Linux container. These tests pin the store that grants them
    /// everywhere.
    /// </summary>
    public sealed class LeasingWotBlobResourceStoreTests : IDisposable
    {
        private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "wot-leasing-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Fact]
        public async Task A_lease_serves_the_exact_document_bytes()
        {
            byte[] document = Encoding.UTF8.GetBytes("{ \"title\": \"pump\" }");
            using LeasingWotBlobResourceStore store = new(_root);
            await store.WriteAsync(Key, 0, new ByteString(document));

            Assert.True(store.SupportsImmutableContentLeases);
            using IWotRegistryContentLease lease = await store.AcquireContentLeaseAsync(Key);

            Assert.Equal(Key, lease.ResourceKey);
            Assert.Equal(document.Length, lease.ContentLength);
            Assert.Equal(document, (await lease.ReadAsync(0, document.Length)).ToArray());
            Assert.Equal(document.AsSpan(3, 4).ToArray(), (await lease.ReadAsync(3, 4)).ToArray());
            Assert.Equal(0, (await lease.ReadAsync(document.Length, 10)).Length);
        }

        [Fact]
        public async Task A_missing_document_cannot_be_leased()
        {
            using LeasingWotBlobResourceStore store = new(_root);

            await Assert.ThrowsAsync<FileNotFoundException>(async () => await store.AcquireContentLeaseAsync(Key));
        }

        [Fact]
        public async Task A_leased_document_cannot_be_replaced_or_deleted_until_every_lease_is_released()
        {
            byte[] original = Encoding.UTF8.GetBytes("original");
            using LeasingWotBlobResourceStore store = new(_root);
            await store.WriteAsync(Key, 0, new ByteString(original));

            IWotRegistryContentLease first = await store.AcquireContentLeaseAsync(Key);
            IWotRegistryContentLease second = await store.AcquireContentLeaseAsync(Key);

            await Assert.ThrowsAsync<IOException>(async () => await store.WriteAsync(Key, 0, new ByteString(Encoding.UTF8.GetBytes("replaced"))));
            await Assert.ThrowsAsync<IOException>(async () => await store.DeleteAsync(Key));

            first.Dispose();
            await Assert.ThrowsAsync<IOException>(async () => await store.DeleteAsync(Key));
            Assert.Equal(original, (await second.ReadAsync(0, original.Length)).ToArray());

            second.Dispose();
            Assert.True(await store.DeleteAsync(Key));
            Assert.Equal(-1, await store.GetLengthAsync(Key));
        }

        [Fact]
        public void A_registry_over_the_store_supports_prepared_publication()
        {
            using LeasingWotBlobResourceStore store = LeasingWotBlobResourceStore.OpenForRegistry(_root);
            using WotRegistryService registry = new(new FileWotRegistryStore(_root, store));

            Assert.True(registry.SupportsPreparedPublication);
        }

        [Fact]
        public async Task A_new_registry_stores_and_reloads_its_documents()
        {
            byte[] document = Pump();

            using (LeasingWotBlobResourceStore store = LeasingWotBlobResourceStore.OpenForRegistry(_root))
            using (WotRegistryService registry = new(new FileWotRegistryStore(_root, store)))
            {
                await registry.InitializeAsync();
                await UpsertPumpAsync(registry, document);
            }

            Assert.NotEmpty(Directory.GetFiles(Path.Combine(_root, LeasingWotBlobResourceStore.DocumentsFolderName), "*.bin"));
            Assert.Equal(document, await ReopenAndReadPumpAsync());
        }

        [Fact]
        public async Task Documents_stored_by_the_default_store_are_migrated_once()
        {
            byte[] document = Pump();

            using (WotRegistryService registry = new(new FileWotRegistryStore(_root)))
            {
                await registry.InitializeAsync();
                await UpsertPumpAsync(registry, document);
            }

            Assert.Equal(document, await ReopenAndReadPumpAsync());

            // A second start must not copy the package's documents again.
            string documents = Path.Combine(_root, LeasingWotBlobResourceStore.DocumentsFolderName);
            foreach (string file in Directory.GetFiles(documents))
            {
                File.Delete(file);
            }

            using LeasingWotBlobResourceStore store = LeasingWotBlobResourceStore.OpenForRegistry(_root);
            Assert.Empty(Directory.GetFiles(documents));
            Assert.Empty(Directory.GetDirectories(_root, LeasingWotBlobResourceStore.DocumentsFolderName + ".migrating-*"));
        }

        private static byte[] Pump() => Encoding.UTF8.GetBytes(
            "{ \"@context\": [\"https://www.w3.org/2022/wot/td/v1.1\"], \"@type\": [\"Thing\"], \"id\": \"urn:pump\", \"title\": \"pump\", \"securityDefinitions\": { \"nosec_sc\": { \"scheme\": \"nosec\" } }, \"security\": [\"nosec_sc\"] }");

        private static async Task UpsertPumpAsync(WotRegistryService registry, byte[] document)
        {
            await registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                Kind = WoTDocumentKindEnum.ThingDescription,
                ResourceId = "pump",
                Name = "pump",
                Content = new ByteString(document),
                SetAsDefault = true
            });
        }

        private async Task<byte[]> ReopenAndReadPumpAsync()
        {
            using LeasingWotBlobResourceStore store = LeasingWotBlobResourceStore.OpenForRegistry(_root);
            using WotRegistryService reopened = new(new FileWotRegistryStore(_root, store));
            await reopened.InitializeAsync();

            WotResourceVersion version = reopened.Current
                .ResourcesOfKind(WoTDocumentKindEnum.ThingDescription)
                .Single(r => r.ResourceId == "pump")
                .DefaultVersion;

            return (await reopened.ReadContentAsync(version)).ToArray();
        }
    }
}
