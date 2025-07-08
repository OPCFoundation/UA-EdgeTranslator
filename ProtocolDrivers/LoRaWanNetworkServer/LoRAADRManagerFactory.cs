// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LoRaWan.NetworkServer;

namespace LoRaWANContainer.LoRaWan.NetworkServer
{
    using System;
    using System.Threading;

    public class LoRAADRManagerFactory()
    {
        private static readonly Lock InMemoryStoreLock = new Lock();
        private static volatile LoRaADRInMemoryStore inMemoryStore;

        public LoRaADRManagerBase Create(FrameCounterUpdateStrategy frameCounterStrategy, LoRaDevice loRaDevice)
        {
            ArgumentNullException.ThrowIfNull(loRaDevice);

            return new LoRaADRDefaultManager(CurrentInMemoryStore, frameCounterStrategy, loRaDevice);
        }

        private static LoRaADRInMemoryStore CurrentInMemoryStore
        {
            get
            {
                if (inMemoryStore != null)
                {
                    return inMemoryStore;
                }
                else
                {
                    lock (InMemoryStoreLock)
                    {
                        if (inMemoryStore == null)
                        {
                            inMemoryStore = new LoRaADRInMemoryStore();
                        }
                    }
                }

                return inMemoryStore;
            }
        }
    }
}
