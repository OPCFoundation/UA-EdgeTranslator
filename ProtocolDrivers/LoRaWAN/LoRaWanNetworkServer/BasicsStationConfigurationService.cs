// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer.BasicsStation
{
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using System;

    public sealed class BasicsStationConfigurationService
    {
        public string GetRouterConfigMessage(string devEui)
        {
            if (SearchDevicesResult.DeviceList.ContainsKey(devEui))
            {
                return SearchDevicesResult.DeviceList[devEui];
            }

            // A gateway can connect before any asset is onboarded, so fall back
            // to the configurations loaded from the settings folder at startup.
            string routerConfig = Opc.Ua.Edge.Translator.ProtocolDrivers.RouterConfigStore.Get(devEui);

            if (routerConfig == null)
            {
                throw new Exception($"No router configuration is available for gateway with DevEui {devEui}.");
            }

            return routerConfig;
        }

        public Region GetRegion(ulong frequencyHz)
        {
            if (frequencyHz >= 863000000 && frequencyHz <= 870000000)
            {
                return new RegionEU868();
            }
            else if (frequencyHz >= 902000000 && frequencyHz <= 928000000)
            {
                return new RegionUS915();
            }
            else if (frequencyHz >= 470000000 && frequencyHz <= 510000000)
            {
                return new RegionCN470RP1();
            }
            else if (frequencyHz >= 915000000 && frequencyHz <= 928000000)
            {
                return new RegionAU915RP1();
            }
            else if (frequencyHz >= 920000000 && frequencyHz <= 923000000)
            {
                return new RegionAS923();
            }
            else
            {
                // default to RegionEU868
                return new RegionEU868();
            }
        }
    }
}
