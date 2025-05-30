// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer
{
    using System;
    using System.Linq;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;

    public abstract class LoRaADRStoreBase
    {
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
    }
}
