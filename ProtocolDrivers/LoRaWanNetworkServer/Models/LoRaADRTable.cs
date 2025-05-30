// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System.Collections.Generic;

    public class LoRaADRTable
    {
        public const int FrameCountCaptureCount = 20;

        /// <summary>
        /// Gets or sets current TxPower Index of the system.
        /// Max Txpower is set to 0 and decrease for each increment by a #db.
        /// </summary>
        public int? CurrentTxPower { get; set; }

        /// <summary>
        /// Gets or sets the current number of repetion for device transmission.
        /// </summary>
        public int? CurrentNbRep { get; set; }

        public IList<LoRaADRTableEntry> Entries { get; } = new List<LoRaADRTableEntry>();

        public bool IsComplete => Entries.Count >= FrameCountCaptureCount;
    }
}
