// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;

    public class LoRaDeviceFrameCounterUpdateStrategyProvider : ILoRaDeviceFrameCounterUpdateStrategyProvider
    {
        private readonly string gatewayID;
        private readonly MultiGatewayFrameCounterUpdateStrategy multiGateway;
        private readonly SingleGatewayFrameCounterUpdateStrategy singleGateway;

        public LoRaDeviceFrameCounterUpdateStrategyProvider(NetworkServerConfiguration networkServerConfiguration)
        {
            ArgumentNullException.ThrowIfNull(networkServerConfiguration);

            this.gatewayID = networkServerConfiguration.GatewayID;
            this.multiGateway = new MultiGatewayFrameCounterUpdateStrategy(gatewayID);
            this.singleGateway = new SingleGatewayFrameCounterUpdateStrategy();
        }

        public ILoRaDeviceFrameCounterUpdateStrategy GetStrategy(string deviceGatewayID)
        {
            if (string.IsNullOrEmpty(deviceGatewayID))
                return this.multiGateway;

            if (string.Equals(this.gatewayID, deviceGatewayID, StringComparison.OrdinalIgnoreCase))
                return this.singleGateway;

            return null;
        }
    }
}
