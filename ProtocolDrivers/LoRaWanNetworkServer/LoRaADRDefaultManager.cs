// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer
{
    using System.Threading.Tasks;
    using global::LoRaWan;
    using global::LoRaWan.NetworkServer;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;

    public class LoRaADRDefaultManager(ILoRaADRStore store, LoRaADRStrategyProvider strategyProvider, ILoRaDeviceFrameCounterUpdateStrategy frameCounterStrategy, LoRaDevice loRaDevice, ILogger<LoRaADRDefaultManager> logger) : LoRaADRManagerBase(store, strategyProvider, logger)
    {
        protected LoRaDevice LoRaDevice { get; private set; } = loRaDevice;

        protected override void UpdateState(LoRaADRResult loRaADRResult)
        {
            if (loRaADRResult != null)
                LoRaDevice.UpdatedADRProperties(loRaADRResult.DataRate, loRaADRResult.TxPower.GetValueOrDefault(), loRaADRResult.NbRepetition.GetValueOrDefault());
        }

        public override Task<uint> NextFCntDown(DevEui devEUI, string gatewayId, uint clientFCntUp, uint clientFCntDown)
        {
            return frameCounterStrategy.NextFcntDown(LoRaDevice, clientFCntUp).AsTask();
            // update twins
        }
    }
}
