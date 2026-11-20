namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Client;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.WotCon;
    using Opc.Ua.Server;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// A Thing Description usually serves <c>readproperty</c> and <c>observeproperty</c>
    /// from one form. The registry treated such a property as read-only, so a client
    /// that subscribed to its Variable got the initial value and never another one.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class WotRegistrySubscriptionTests
    {
        [Theory]
        [InlineData("""[ { "href": "counter", "op": [ "readproperty", "observeproperty" ] } ]""")]
        [InlineData("""[ { "href": "counter", "op": [ "readproperty" ] }, { "href": "counter", "op": [ "observeproperty" ] } ]""")]
        public async Task A_subscribed_property_keeps_publishing_new_values(string forms)
        {
            await using OpcUaServerFixture fixture = new();
            StandardServer server = (StandardServer)fixture.App.Server;
            Program.Drivers.Register(new CountingDriver());

            string thingId = "counter-" + Guid.NewGuid().ToString("N");
            string importFolder = Path.Combine(fixture.WorkingDirectory, "counter-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(importFolder);
            await File.WriteAllTextAsync(Path.Combine(importFolder, thingId + ".jsonld"), $$"""
                {
                  "@context": ["https://www.w3.org/2022/wot/td/v1.1"],
                  "@type": ["Thing"],
                  "id": "urn:{{thingId}}",
                  "title": "{{thingId}}",
                  "base": "countmock://device-1:1/1",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": ["nosec_sc"],
                  "properties": {
                    "counter": {
                      "type": "integer",
                      "readOnly": true,
                      "observable": true,
                      "forms": {{forms}}
                    }
                  }
                }
                """);

            string storage = Path.Combine(fixture.WorkingDirectory, "wot-registry-" + Guid.NewGuid().ToString("N"));
            await using WotRegistryHost host = await WotRegistryHost.StartAsync(server, Program.Drivers, storage);
            await host.ImportSettingsFilesAsync(importFolder, default);

            NodeId counter = await FindPropertyAsync(thingId + " [xRegistry]", "counter");

            Client.ISession session = await CreateClientSessionAsync(fixture);
            try
            {
                ConcurrentQueue<DataValue> received = new();
#pragma warning disable CS0618 // The classic subscription API is enough to observe notifications here.
                Client.Subscription subscription = new(session.DefaultSubscription) { PublishingInterval = 100 };
#pragma warning restore CS0618
                Client.MonitoredItem item = new(subscription.DefaultItem)
                {
                    StartNodeId = counter,
                    AttributeId = Attributes.Value,
                    SamplingInterval = 100,
                    QueueSize = 10
                };
                item.Notification += (monitoredItem, e) =>
                {
                    if (e.NotificationValue is MonitoredItemNotification notification)
                    {
                        received.Enqueue(notification.Value);
                    }
                };

                subscription.AddItem(item);
                Assert.True(session.AddSubscription(subscription));
                await subscription.CreateAsync();

                // The driver returns a new value on every read, so a working
                // subscription delivers several distinct values within a few seconds.
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (DistinctGoodValues(received) < 3 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(100);
                }

                Assert.True(
                    DistinctGoodValues(received) >= 3,
                    "Only received: " + string.Join(", ", received.Select(v => v.StatusCode + "/" + v.WrappedValue)));

                await subscription.DeleteAsync(true);
            }
            finally
            {
                await session.CloseAsync();
                session.Dispose();
            }
        }

        private static int DistinctGoodValues(IEnumerable<DataValue> values)
        {
            return values
                .Where(v => StatusCode.IsGood(v.StatusCode))
                .Select(v => v.WrappedValue.ToString())
                .Distinct(StringComparer.Ordinal)
                .Count();
        }

        private static async Task<NodeId> FindPropertyAsync(string thingName, string propertyName)
        {
            AddressSpaceService service = new();
            for (int attempt = 0; attempt < 100; attempt++)
            {
                AddressSpaceNode thing = (await service.BrowseChildrenAsync(ObjectIds.ObjectsFolder.ToString()))
                    .FirstOrDefault(c => c.Text == thingName);
                AddressSpaceNode child = thing == null
                    ? null
                    : (await service.BrowseChildrenAsync(thing.Id)).FirstOrDefault(c => c.Text == propertyName);
                if (child != null)
                {
                    return NodeId.Parse(child.Id);
                }

                await Task.Delay(50);
            }

            throw new Xunit.Sdk.XunitException($"Property '{propertyName}' of '{thingName}' was never materialized.");
        }

        private static async Task<Client.ISession> CreateClientSessionAsync(OpcUaServerFixture fixture)
        {
            EndpointDescription selectedEndpoint = await CoreClientUtils.SelectEndpointAsync(
                fixture.App.ApplicationConfiguration,
                fixture.EndpointUrl,
                useSecurity: false,
                fixture.Telemetry).ConfigureAwait(false);

            ConfiguredEndpoint configuredEndpoint = new(
                null,
                selectedEndpoint,
                EndpointConfiguration.Create(fixture.App.ApplicationConfiguration));

            return await new DefaultSessionFactory(fixture.Telemetry).CreateAsync(
                fixture.App.ApplicationConfiguration,
                configuredEndpoint,
                updateBeforeConnect: true,
                checkDomain: false,
                "UAEdgeTranslator subscription tests",
                30_000,
                new UserIdentity(),
                default).ConfigureAwait(false);
        }

        /// <summary>A driver whose asset returns a new value on every read.</summary>
        private sealed class CountingDriver : IProtocolDriver
        {
            public string Scheme => "countmock";

            public string WoTBindingUri => "https://opcfoundation.org/UA-EdgeTranslator/countmock";

            public IEnumerable<string> Discover() => [];

            public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint) => throw new NotSupportedException();

            public Task<AssetConnection> CreateAndConnectAssetAsync(ThingDescription td, CancellationToken cancellationToken = default) =>
                Task.FromResult(new AssetConnection(new CountingAsset(), 1));

            public AssetTag CreateTag(ThingDescription td, object form, string assetId, byte unitId, string variableId, string mappedUAExpandedNodeId, string mappedUAFieldPath) =>
                new() { Name = variableId, Address = variableId, UnitID = unitId, Type = "integer", PollingInterval = 200 };
        }

        private sealed class CountingAsset : IAsset
        {
            private int _count;

            public bool IsConnected => true;

            public string GetRemoteEndpoint() => "countmock://device-1:1";

            public Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default) =>
                Task.FromResult<object>(Interlocked.Increment(ref _count));

            public Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<AssetActionResult> ExecuteActionAsync(MethodState method, IList<object> inputArgs, CancellationToken cancellationToken = default) =>
                Task.FromResult(AssetActionResult.FromOutputs("ok", new List<object>()));

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
