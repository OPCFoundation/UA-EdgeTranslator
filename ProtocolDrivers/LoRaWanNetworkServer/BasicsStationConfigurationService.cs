// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer.BasicsStation
{
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class BasicsStationConfigurationService()
    {
        public Task<string> GetRouterConfigMessageAsync(StationEui stationEui, CancellationToken cancellationToken)
        {
            // TODO: Support other routers
            var routerConfig = "{" +
                                 "\"msgtype\": \"router_config\"," +
                                 "\"NetID\": [1]," +
                                 "\"JoinEui\": [[0,-1]]," +
                                 "\"region\":\"EU863\"," +
                                 "\"hwspec\":\"sx1301/1\"," +
                                 "\"freq_range\": [863000000, 870000000]," +
                                 "\"DRs\": [" +
                                 "    [12, 125, 0]," +
                                 "    [11, 125, 0]," +
                                 "    [10, 125, 0]," +
                                 "    [9, 125, 0]," +
                                 "    [8, 125, 0]," +
                                 "    [7, 125, 0]," +
                                 "    [7, 250, 0]" +
                                 "]," +
                                 "\"sx1301_conf\": [" +
                                 "    {" +
                                 "      \"radio_0\": { \"enable\": true, \"freq\": 867500000 }," +
                                 "      \"radio_1\": { \"enable\": true, \"freq\": 868500000 }," +
                                 "      \"chan_FSK\": { \"enable\": true, \"radio\": 1, \"if\": 300000 }," +
                                 "      \"chan_Lora_std\": {" +
                                 "        \"enable\": true," +
                                 "        \"radio\": 1," +
                                 "        \"if\": -200000," +
                                 "        \"bandwidth\": 250000," +
                                 "        \"spread_factor\": 7" +
                                 "    }," +
                                 "    \"chan_multiSF_0\": { \"enable\": true, \"radio\": 1, \"if\": -400000 }," +
                                 "    \"chan_multiSF_1\": { \"enable\": true, \"radio\": 1, \"if\": -200000 }," +
                                 "    \"chan_multiSF_2\": { \"enable\": true, \"radio\": 1, \"if\": 0 }," +
                                 "    \"chan_multiSF_3\": { \"enable\": true, \"radio\": 0, \"if\": -400000 }," +
                                 "    \"chan_multiSF_4\": { \"enable\": true, \"radio\": 0, \"if\": -200000 }," +
                                 "    \"chan_multiSF_5\": { \"enable\": true, \"radio\": 0, \"if\": 0 }," +
                                 "    \"chan_multiSF_6\": { \"enable\": true, \"radio\": 0, \"if\": 200000 }," +
                                 "    \"chan_multiSF_7\": { \"enable\": true, \"radio\": 0, \"if\": 400000 }" +
                                 "   }" +
                                 "]," +
                                 "\"nocca\": true," +
                                 "\"nodc\": true," +
                                 "\"nodwell\": true" +
                               "}";

            return Task.FromResult(routerConfig);
        }

        public Task<Region> GetRegionAsync(StationEui stationEui, CancellationToken cancellationToken)
        {
            // default to EU
            // TODO: Support other regions
            Region region = new RegionEU868();

            return Task.FromResult(region);
        }
    }
}
