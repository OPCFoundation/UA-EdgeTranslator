// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LoRaWan;

namespace Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class LocalLnsDiscovery
    {
        private readonly Uri lnsUri;

        public const string EndpointName = "/router-info";

        public LocalLnsDiscovery(Uri lnsUri) => this.lnsUri = lnsUri;

        public Task<Uri> ResolveLnsAsync(StationEui stationEui, CancellationToken cancellationToken) =>
            Task.FromResult(lnsUri);
    }
}
