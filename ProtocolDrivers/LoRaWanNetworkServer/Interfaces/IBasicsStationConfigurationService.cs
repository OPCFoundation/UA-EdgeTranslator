// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.



namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using System.Threading;
    using System.Threading.Tasks;

    internal interface IBasicsStationConfigurationService
    {
        Task<string> GetRouterConfigMessageAsync(StationEui stationEui, CancellationToken cancellationToken);

        Task<Region> GetRegionAsync(StationEui stationEui, CancellationToken cancellationToken);
    }
}
