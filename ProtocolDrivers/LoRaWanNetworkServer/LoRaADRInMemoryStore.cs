// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer
{
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Caching.Memory;
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Stores ADR tables in memory on the gateway.
    /// This is the default implementation if we have a single gateway environment.
    /// </summary>
    public class LoRaADRInMemoryStore : IDisposable
    {
        private readonly MemoryCache cache;

        protected static void AddEntryToTable(LoRaADRTable table, LoRaADRTableEntry entry)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(entry);

            var existing = table.Entries.FirstOrDefault(itm => itm.FCnt == entry.FCnt);

            if (existing == null)
            {
                // first for this framecount, simply add it
                entry.GatewayCount = 1;
                table.Entries.Add(entry);
            }
            else
            {
                if (existing.Snr < entry.Snr)
                {
                    // better SNR. Update
                    existing.Snr = entry.Snr;
                    existing.GatewayId = entry.GatewayId;
                }

                existing.GatewayCount++;
            }

            if (table.Entries.Count > LoRaADRTable.FrameCountCaptureCount)
                table.Entries.RemoveAt(0);
        }

        public LoRaADRInMemoryStore()
        {
            // REVIEW: can we set a size limit?
            this.cache = new MemoryCache(new MemoryCacheOptions());
        }

        public Task<LoRaADRTable> GetADRTable(DevEui devEUI)
        {
            lock (this.cache)
            {
                return Task.FromResult(this.cache.Get<LoRaADRTable>(devEUI));
            }
        }

        public Task UpdateADRTable(DevEui devEUI, LoRaADRTable table)
        {
            // void: the reference is up to date already
            return Task.CompletedTask;
        }

        public Task<LoRaADRTable> AddTableEntry(LoRaADRTableEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            lock (this.cache)
            {
                var table = this.cache.GetOrCreate(entry.DevEUI, (cacheEntry) => new LoRaADRTable());
                AddEntryToTable(table, entry);
                return Task.FromResult(table);
            }
        }

        public Task<bool> Reset(DevEui devEUI)
        {
            lock (this.cache)
            {
                this.cache.Remove(devEUI);
            }

            return Task.FromResult(true);
        }

        public void Dispose() => this.cache.Dispose();
    }
}
