// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LoRaWan.NetworkServer;

namespace LoRaWANContainer.LoRaWan.NetworkServer
{
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading;

    public class LoRAADRManagerFactory(ILoggerFactory loggerFactory) : ILoRAADRManagerFactory
    {
        private static readonly Lock InMemoryStoreLock = new Lock();
        private static volatile LoRaADRInMemoryStore inMemoryStore;

        public ILoRaADRManager Create(ILoRaADRStrategyProvider strategyProvider,
                                      ILoRaDeviceFrameCounterUpdateStrategy frameCounterStrategy,
                                      LoRaDevice loRaDevice)
        {
            ArgumentNullException.ThrowIfNull(loRaDevice);

            return !string.IsNullOrEmpty(loRaDevice.GatewayID)
                    ? new LoRaADRDefaultManager(CurrentInMemoryStore, strategyProvider, frameCounterStrategy, loRaDevice, loggerFactory.CreateLogger<LoRaADRDefaultManager>())
                    : new LoRaADRMultiGatewayManager(loRaDevice, loggerFactory.CreateLogger<LoRaADRMultiGatewayManager>());
        }

        private static LoRaADRInMemoryStore CurrentInMemoryStore
        {
            get
            {
                if (inMemoryStore != null)
                    return inMemoryStore;

                lock (InMemoryStoreLock)
                {
#pragma warning disable IDE0074 // Use compound assignment
#pragma warning disable CA1508 // Avoid dead conditional code
                    // False positive.
                    if (inMemoryStore == null)
                        inMemoryStore = new LoRaADRInMemoryStore();
#pragma warning restore IDE0074 // Use compound assignment
                }

                return inMemoryStore;
            }
        }
    }
}
