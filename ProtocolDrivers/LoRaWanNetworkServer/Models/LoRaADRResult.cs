// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using global::LoRaWan;

    public class LoRaADRResult
    {
        public int? NbRepetition { get; set; }

        public int? TxPower { get; set; }

        public DataRateIndex DataRate { get; set; }

        public bool CanConfirmToDevice { get; set; }

        public uint? FCntDown { get; set; }

        public int NumberOfFrames { get; set; }
    }
}
