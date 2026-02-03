// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using System.Threading.Tasks;

    public sealed class FrameCounterUpdateStrategy
    {
        public Task<bool> ResetAsync(LoRaDevice loRaDevice)
        {
            ArgumentNullException.ThrowIfNull(loRaDevice);

            loRaDevice.ResetFcnt();
            return Task.FromResult(true); // always able to reset locally
        }

        public ValueTask<uint> NextFcntDown(LoRaDevice loRaDevice, uint messageFcnt)
        {
            ArgumentNullException.ThrowIfNull(loRaDevice);

            return new ValueTask<uint>(loRaDevice.IncrementFcntDown(1));
        }

        public Task<bool> SaveChangesAsync(LoRaDevice loRaDevice)
        {
            ArgumentNullException.ThrowIfNull(loRaDevice);

            return InternalSaveChangesAsync(loRaDevice, force: false);
        }

        private static async Task<bool> InternalSaveChangesAsync(LoRaDevice loRaDevice, bool force)
        {
            return await loRaDevice.SaveChangesAsync(force: force).ConfigureAwait(false);
        }
    }
}
