// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.



namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;
    using global::LoRaWan;
    using global::LoRaWan.NetworkServer.BasicsStation;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;

    internal interface IBasicsStationConfigurationService
    {
        Task<string> GetRouterConfigMessageAsync(StationEui stationEui, CancellationToken cancellationToken);

        Task<Region> GetRegionAsync(StationEui stationEui, CancellationToken cancellationToken);

        Task<CupsTwinInfo> GetCupsConfigAsync(StationEui? stationEui, CancellationToken cancellationToken);
    }
}
