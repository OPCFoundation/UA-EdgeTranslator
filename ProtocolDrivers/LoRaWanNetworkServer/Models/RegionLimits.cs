// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using LoRaTools;

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System.Collections.Generic;
    using global::LoRaWan;

    public class RegionLimits
    {
        /// <summary>
        /// Gets or sets The maximum and minimum datarate of a given region.
        /// </summary>
        public (Hertz Min, Hertz Max) FrequencyRange { get; set; }

        private readonly ISet<DataRate> downstreamValidDR;

        private readonly ISet<DataRate> upstreamValidDR;

        private readonly DataRateIndex startUpstreamDRIndex;

        private readonly DataRateIndex startDownstreamDRIndex;

        public RegionLimits((Hertz Min, Hertz Max) frequencyRange, ISet<DataRate> upstreamValidDR, ISet<DataRate> downstreamValidDR,
                            DataRateIndex startUpstreamDRIndex, DataRateIndex startDownstreamDRIndex)
        {
            FrequencyRange = frequencyRange;
            this.downstreamValidDR = downstreamValidDR;
            this.upstreamValidDR = upstreamValidDR;
            this.startDownstreamDRIndex = startDownstreamDRIndex;
            this.startUpstreamDRIndex = startUpstreamDRIndex;
        }

        public bool IsCurrentUpstreamDRIndexWithinAcceptableValue(DataRateIndex dr) => dr >= this.startUpstreamDRIndex && dr < this.startUpstreamDRIndex + this.upstreamValidDR.Count;

        public bool IsCurrentDownstreamDRIndexWithinAcceptableValue(DataRateIndex dr) => dr >= this.startDownstreamDRIndex && dr < this.startDownstreamDRIndex + this.downstreamValidDR.Count;
    }
}
