// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;
    using System;

    public class DeduplicationStrategyFactory(ILoggerFactory loggerFactory, ILogger<DeduplicationStrategyFactory> logger) : IDeduplicationStrategyFactory
    {
        public ILoRaDeviceMessageDeduplicationStrategy Create(LoRaDevice loRaDevice)
        {
            ArgumentNullException.ThrowIfNull(loRaDevice);

            if (!string.IsNullOrEmpty(loRaDevice.GatewayID))
            {
                logger.LogDebug("LoRa device has a specific gateway assigned. Ignoring deduplication as it is not applicable.");
                return null;
            }

            switch (loRaDevice.Deduplication)
            {
                case DeduplicationMode.Drop: return new DeduplicationStrategyDrop(loggerFactory.CreateLogger<DeduplicationStrategyDrop>());
                case DeduplicationMode.Mark: return new DeduplicationStrategyMark(loggerFactory.CreateLogger<DeduplicationStrategyMark>());
                case DeduplicationMode.None:
                default:
                {
                    logger.LogDebug("no Deduplication Strategy selected");
                    return null;
                }
            }
        }
    }
}
