// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;

    public class DeduplicationStrategyDrop : ILoRaDeviceMessageDeduplicationStrategy
    {
        private readonly ILogger<DeduplicationStrategyDrop> logger;

        public DeduplicationStrategyDrop(ILogger<DeduplicationStrategyDrop> logger)
        {
            this.logger = logger;
            this.logger.LogDebug("deduplication Strategy: Drop");
        }

        public DeduplicationResult Process(DeduplicationResult result, uint fCntUp)
        {
            ArgumentNullException.ThrowIfNull(result);

            result.CanProcess = !result.IsDuplicate;

            if (result.IsDuplicate)
            {
                this.logger.LogDebug($"duplicate message '{fCntUp}' is dropped.");
            }

            return result;
        }
    }
}
