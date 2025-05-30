// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using global::LoRaWan;

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    public class LoRaADRRequest
    {
        public DataRateIndex DataRate { get; set; }

        public float RequiredSnr { get; set; }

        public bool PerformADRCalculation { get; set; }

        public uint FCntUp { get; set; }

        public uint FCntDown { get; set; }

        public int MinTxPowerIndex { get; set; }

        public string GatewayId { get; set; }

        public bool ClearCache { get; set; }
    }
}
