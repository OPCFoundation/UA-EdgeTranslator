// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;

    /// <summary>
    /// An interface implementing LoRa ADR strategies.
    /// </summary>
    public interface ILoRaADRStrategy
    {
        LoRaADRResult ComputeResult(LoRaADRTable table, float requiredSnr, DataRateIndex upstreamDataRate, int minTxPower, DataRateIndex maxDr);

        int MinimumNumberOfResult { get; }

        int DefaultTxPower { get; }

        int DefaultNbRep { get; }
    }
}
