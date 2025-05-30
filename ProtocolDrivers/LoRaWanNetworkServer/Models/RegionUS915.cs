// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer;

    public class RegionUS915 : Region
    {
        // Frequencies calculated according to formula:
        // 923.3 + upstreamChannelNumber % 8 * 0.6,
        // rounded to first decimal point
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
                [DataRateIndex.DR0] = (LoRaDataRate.SF10BW125, MaxPayloadSize: 19),
                [DataRateIndex.DR1] = (LoRaDataRate.SF9BW125, MaxPayloadSize: 61),
                [DataRateIndex.DR2] = (LoRaDataRate.SF8BW125, MaxPayloadSize: 133),
                [DataRateIndex.DR3] = (LoRaDataRate.SF7BW125, MaxPayloadSize: 250),
                [DataRateIndex.DR4] = (LoRaDataRate.SF8BW500, MaxPayloadSize: 250),
                [DataRateIndex.DR8] = (LoRaDataRate.SF12BW500, MaxPayloadSize: 61),
                [DataRateIndex.DR9] = (LoRaDataRate.SF11BW500, MaxPayloadSize: 137),
                [DataRateIndex.DR10] = (LoRaDataRate.SF10BW500, MaxPayloadSize: 250),
                [DataRateIndex.DR11] = (LoRaDataRate.SF9BW500, MaxPayloadSize: 250),
                [DataRateIndex.DR12] = (LoRaDataRate.SF8BW500, MaxPayloadSize: 250),
                [DataRateIndex.DR13] = (LoRaDataRate.SF7BW500, MaxPayloadSize: 250),
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
                [11] = 19,
                [12] = 18,
                [13] = 17,
            }.ToImmutableDictionary();

        public override IReadOnlyDictionary<uint, double> TXPowertoMaxEIRP => MaxEirpByTxPower;

        private static readonly ImmutableArray<IReadOnlyList<DataRateIndex>> RX1DROffsetTableInternal =
            new IReadOnlyList<DataRateIndex>[]
            {
                new[] { DataRateIndex.DR10, DataRateIndex.DR9,  DataRateIndex.DR8,  DataRateIndex.DR8  }.ToImmutableArray(),
                new[] { DataRateIndex.DR11, DataRateIndex.DR10, DataRateIndex.DR9,  DataRateIndex.DR8  }.ToImmutableArray(),
                new[] { DataRateIndex.DR12, DataRateIndex.DR11, DataRateIndex.DR10, DataRateIndex.DR9  }.ToImmutableArray(),
                new[] {DataRateIndex.DR13, DataRateIndex.DR12, DataRateIndex.DR11, DataRateIndex.DR10 }.ToImmutableArray(),
                new[] {DataRateIndex.DR13, DataRateIndex.DR13, DataRateIndex.DR12, DataRateIndex.DR11 }.ToImmutableArray(),
            }.ToImmutableArray();

        public override IReadOnlyList<IReadOnlyList<DataRateIndex>> RX1DROffsetTable => RX1DROffsetTableInternal;

        public RegionUS915()
            : base(LoRaRegionType.US915)
        {
            var upstreamValidDataranges = new HashSet<DataRate>
            {
                LoRaDataRate.SF10BW125, // 0
                LoRaDataRate.SF9BW125,  // 1
                LoRaDataRate.SF8BW125,  // 2
                LoRaDataRate.SF7BW125,  // 3
                LoRaDataRate.SF8BW500,  // 4
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
            RegionLimits = new RegionLimits((Min: Hertz.Mega(902.3), Max: Hertz.Mega(927.5)), upstreamValidDataranges, downstreamValidDataranges, DataRateIndex.DR0, DataRateIndex.DR8);
        }

        /// <summary>
        /// Logic to get the correct downstream transmission frequency for region US915.
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
            upstreamChannelNumber = upstreamDataRate == DataRateIndex.DR4 ? 64 + (int)Math.Round((upstreamFrequency.InMega - 903) / 1.6, 0, MidpointRounding.AwayFromZero)
                                                            : (int)Math.Round((upstreamFrequency.InMega - 902.3) / 0.2, 0, MidpointRounding.AwayFromZero);
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
