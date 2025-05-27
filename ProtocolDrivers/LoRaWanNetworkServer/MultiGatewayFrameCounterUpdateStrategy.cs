// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System.Threading.Tasks;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;

    // Frame counter strategy for multi gateway scenarios
    // Frame Down counters is resolved by calling the LoRa device API. Only a single caller will received a valid frame counter (> 0)
    public class MultiGatewayFrameCounterUpdateStrategy(string gatewayID) : ILoRaDeviceFrameCounterUpdateStrategy
    {
        public Task<bool> ResetAsync(LoRaDevice loRaDevice, uint fcntUp, string gatewayId)
        {
            System.ArgumentNullException.ThrowIfNull(loRaDevice);

            loRaDevice.ResetFcnt();

            return Task.FromResult(true);
        }

        public ValueTask<uint> NextFcntDown(LoRaDevice loRaDevice, uint messageFcnt)
        {
            System.ArgumentNullException.ThrowIfNull(loRaDevice);
            System.ArgumentNullException.ThrowIfNull(gatewayID);

            //var result = await loRaDeviceAPIService.NextFCntDownAsync(
            //    devEUI: loRaDevice.DevEUI,
            //    fcntDown: loRaDevice.FCntDown,
            //    fcntUp: messageFcnt,
            //    gatewayId: gatewayID).ConfigureAwait(false);

            //if (result > 0)
            //{
            //    loRaDevice.SetFcntDown(result);
            //}

            //return result;

            return ValueTask.FromResult(loRaDevice.FCntDown + 1);
        }

        public Task<bool> SaveChangesAsync(LoRaDevice loRaDevice)
        {
            System.ArgumentNullException.ThrowIfNull(loRaDevice);

            return InternalSaveChangesAsync(loRaDevice, force: false);
        }

        private static async Task<bool> InternalSaveChangesAsync(LoRaDevice loRaDevice, bool force)
        {
            return await loRaDevice.SaveChangesAsync(force: force).ConfigureAwait(false);
        }
    }
}
