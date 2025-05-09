// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer
{
    using System.Threading.Tasks;
    using global::LoRaWan;
    using global::LoRaWan.NetworkServer;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;

    public class LoRaADRMultiGatewayManager(LoRaDevice loRaDevice, ILogger<LoRaADRMultiGatewayManager> logger) : LoRaADRDefaultManager(null, null, null, loRaDevice, logger)
    {
        public override Task<bool> ResetAsync(DevEui devEUI)
        {
            // needs to be called on the function bundler
            return Task.FromResult(false);
        }

        public override Task StoreADREntryAsync(LoRaADRTableEntry newEntry)
        {
            // function bundler is executing this request
            return Task.CompletedTask;
        }

        public override Task<LoRaADRResult> CalculateADRResultAndAddEntryAsync(DevEui devEUI, string gatewayId, uint fCntUp, uint fCntDown, float requiredSnr, DataRateIndex dataRate, int minTxPower, DataRateIndex maxDr, LoRaADRTableEntry newEntry = null)
        {
            return Task.FromResult<LoRaADRResult>(null);
        }
    }
}
