// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer;

    public class RegionAU915RP1 : Region
    {
        private static readonly Hertz[] DownstreamChannelFrequencies =
        {
            Hertz.Mega(923.3),
            Hertz.Mega(923.9),
            Hertz.Mega(924.5),
            Hertz.Mega(925.1),
            Hertz.Mega(925.7),
            Hertz.Mega(926.3),
            Hertz.Mega(926.9),
            Hertz.Mega(927.5)
        };

        private static readonly ImmutableDictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)> DrToConfigurationByDrIndex =
            new Dictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)>
            {
                [DataRateIndex.DR0] = (LoRaDataRate.SF12BW125, 59),
                [DataRateIndex.DR1] = (LoRaDataRate.SF11BW125, 59),
                [DataRateIndex.DR2] = (LoRaDataRate.SF10BW125, 59),
                [DataRateIndex.DR3] = (LoRaDataRate.SF9BW125, 123),
                [DataRateIndex.DR4] = (LoRaDataRate.SF8BW125, 230),
                [DataRateIndex.DR5] = (LoRaDataRate.SF7BW125, 230),
                [DataRateIndex.DR6] = (LoRaDataRate.SF8BW500, 230),
                [DataRateIndex.DR8] = (LoRaDataRate.SF12BW500, 41),
                [DataRateIndex.DR9] = (LoRaDataRate.SF11BW500, 117),
                [DataRateIndex.DR10] = (LoRaDataRate.SF10BW500, 230),
                [DataRateIndex.DR11] = (LoRaDataRate.SF9BW500, 230),
                [DataRateIndex.DR12] = (LoRaDataRate.SF8BW500, 230),
                [DataRateIndex.DR13] = (LoRaDataRate.SF7BW500, 230),
            }.ToImmutableDictionary();

        public override IReadOnlyDictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)> DRtoConfiguration => DrToConfigurationByDrIndex;

        private static readonly ImmutableDictionary<uint, double> MaxEirpByTxPower =
            new Dictionary<uint, double>
            {
                [0] = 30,
                [1] = 29,
                [2] = 28,
                [3] = 27,
                [4] = 26,
                [5] = 25,
                [6] = 24,
                [7] = 23,
                [8] = 22,
                [9] = 21,
                [10] = 20,
            }.ToImmutableDictionary();

        public override IReadOnlyDictionary<uint, double> TXPowertoMaxEIRP => MaxEirpByTxPower;

        private static readonly ImmutableArray<IReadOnlyList<DataRateIndex>> RX1DROffsetTableInternal =
            new IReadOnlyList<DataRateIndex>[]
            {
                new[] { DataRateIndex.DR8, DataRateIndex.DR8, DataRateIndex.DR8, DataRateIndex.DR8, DataRateIndex.DR8, DataRateIndex.DR8  }.ToImmutableArray(),
                new[] { DataRateIndex.DR9, DataRateIndex.DR8, DataRateIndex.DR8, DataRateIndex.DR8, DataRateIndex.DR8, DataRateIndex.DR8  }.ToImmutableArray(),
                new[] { DataRateIndex.DR10, DataRateIndex.DR9, DataRateIndex.DR8, DataRateIndex.DR8, DataRateIndex.DR8, DataRateIndex.DR8  }.ToImmutableArray(),
                new[] { DataRateIndex.DR11, DataRateIndex.DR10, DataRateIndex.DR9, DataRateIndex.DR8, DataRateIndex.DR8, DataRateIndex.DR8  }.ToImmutableArray(),
                new[] { DataRateIndex.DR12, DataRateIndex.DR11, DataRateIndex.DR10, DataRateIndex.DR9, DataRateIndex.DR8, DataRateIndex.DR8  }.ToImmutableArray(),
                new[] { DataRateIndex.DR13, DataRateIndex.DR12, DataRateIndex.DR11, DataRateIndex.DR10, DataRateIndex.DR9, DataRateIndex.DR8  }.ToImmutableArray(),
                new[] { DataRateIndex.DR13, DataRateIndex.DR13, DataRateIndex.DR12, DataRateIndex.DR11, DataRateIndex.DR10, DataRateIndex.DR9  }.ToImmutableArray(),
            }.ToImmutableArray();

        public override IReadOnlyList<IReadOnlyList<DataRateIndex>> RX1DROffsetTable => RX1DROffsetTableInternal;

        public RegionAU915RP1()
            : base(LoRaRegionType.US915)
        {
            var upstreamValidDataranges = new HashSet<DataRate>
            {
                LoRaDataRate.SF12BW125, // 0
                LoRaDataRate.SF11BW125,  // 1
                LoRaDataRate.SF10BW125,  // 2
                LoRaDataRate.SF9BW125,  // 3
                LoRaDataRate.SF8BW125,  // 4
                LoRaDataRate.SF7BW125,  // 5
                LoRaDataRate.SF8BW500,  // 6
            };

            var downstreamValidDataranges = new HashSet<DataRate>
            {
                LoRaDataRate.SF12BW500, // 8
                LoRaDataRate.SF11BW500, // 9
                LoRaDataRate.SF10BW500, // 10
                LoRaDataRate.SF9BW500,  // 11
                LoRaDataRate.SF8BW500,  // 12
                LoRaDataRate.SF7BW500   // 13
            };

            MaxADRDataRate = DataRateIndex.DR3;
            RegionLimits = new RegionLimits((Min: Hertz.Mega(915.2), Max: Hertz.Mega(927.8)), upstreamValidDataranges, downstreamValidDataranges, DataRateIndex.DR0, DataRateIndex.DR8);
        }

        /// <summary>
        /// Logic to get the correct downstream transmission frequency for region AU915.
        /// </summary>
        /// <param name="upstreamFrequency">Frequency on which the message was transmitted.</param>
        /// <param name="upstreamDataRate">Data rate at which the message was transmitted.</param>
        /// <param name="deviceJoinInfo">Join info for the device, if applicable.</param>
        public override bool TryGetDownstreamChannelFrequency(Hertz upstreamFrequency, DataRateIndex upstreamDataRate, DeviceJoinInfo deviceJoinInfo, out Hertz downstreamFrequency)
        {
            if (!IsValidUpstreamFrequency(upstreamFrequency))
                throw new LoRaProcessingException($"Invalid upstream frequency {upstreamFrequency}", LoRaProcessingErrorCode.InvalidFrequency);

            if (!IsValidUpstreamDataRate(upstreamDataRate))
                throw new LoRaProcessingException($"Invalid upstream data rate {upstreamDataRate}", LoRaProcessingErrorCode.InvalidDataRate);

            int upstreamChannelNumber;
            upstreamChannelNumber = upstreamDataRate == DataRateIndex.DR6 ? 64 + (int)Math.Round((upstreamFrequency.InMega - 915.9) / 1.6, 0, MidpointRounding.AwayFromZero)
                                                            : (int)Math.Round((upstreamFrequency.InMega - 915.2) / 0.2, 0, MidpointRounding.AwayFromZero);
            downstreamFrequency = DownstreamChannelFrequencies[upstreamChannelNumber % 8];
            return true;
        }

        /// <summary>
        /// Returns the default RX2 receive window parameters - frequency and data rate.
        /// </summary>
        /// <param name="deviceJoinInfo">Join info for the device, if applicable.</param>
        public override ReceiveWindow GetDefaultRX2ReceiveWindow(DeviceJoinInfo deviceJoinInfo = null) => new ReceiveWindow(DataRateIndex.DR8, Hertz.Mega(923.3));
    }
}
