// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer;

    public class RegionEU868 : Region
    {
        private static readonly ImmutableDictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)> DrToConfigurationByDrIndex =
            new Dictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)>
            {
                [DataRateIndex.DR0] = (LoRaDataRate.SF12BW125, MaxPayloadSize: 59),
                [DataRateIndex.DR1] = (LoRaDataRate.SF11BW125, MaxPayloadSize: 59),
                [DataRateIndex.DR2] = (LoRaDataRate.SF10BW125, MaxPayloadSize: 59),
                [DataRateIndex.DR3] = (LoRaDataRate.SF9BW125, MaxPayloadSize: 123),
                [DataRateIndex.DR4] = (LoRaDataRate.SF8BW125, MaxPayloadSize: 230),
                [DataRateIndex.DR5] = (LoRaDataRate.SF7BW125, MaxPayloadSize: 230),
                [DataRateIndex.DR6] = (LoRaDataRate.SF7BW250, MaxPayloadSize: 230),
                [DataRateIndex.DR7] = (FskDataRate.Fsk50000, MaxPayloadSize: 230),
            }.ToImmutableDictionary();

        public override IReadOnlyDictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)> DRtoConfiguration => DrToConfigurationByDrIndex;

        private static readonly ImmutableDictionary<uint, double> MaxEirpByTxPower =
            new Dictionary<uint, double>
            {
                [0] = 16,
                [1] = 14,
                [2] = 12,
                [3] = 10,
                [4] = 8,
                [5] = 6,
                [6] = 4,
                [7] = 2,
            }.ToImmutableDictionary();

        public override IReadOnlyDictionary<uint, double> TXPowertoMaxEIRP => MaxEirpByTxPower;

        private static readonly ImmutableArray<IReadOnlyList<DataRateIndex>> RX1DROffsetTableInternal =
            new IReadOnlyList<DataRateIndex>[]
            {
                new[] { DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] { DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1 }.ToImmutableArray(),
                new[] { DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] { DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] { DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] { DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] { DataRateIndex.DR6, DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1 }.ToImmutableArray(),
                new[] {DataRateIndex.DR7, DataRateIndex.DR6, DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2 }.ToImmutableArray(),
            }.ToImmutableArray();

        public override IReadOnlyList<IReadOnlyList<DataRateIndex>> RX1DROffsetTable => RX1DROffsetTableInternal;

        public RegionEU868()
            : base(LoRaRegionType.EU868)
        {
            var validDataRangeUpAndDownstream = new HashSet<DataRate>
            {
                LoRaDataRate.SF12BW125, // 0
                LoRaDataRate.SF11BW125, // 1
                LoRaDataRate.SF10BW125, // 2
                LoRaDataRate.SF9BW125,  // 3
                LoRaDataRate.SF8BW125,  // 4
                LoRaDataRate.SF7BW125,  // 5
                LoRaDataRate.SF7BW250,  // 6
                FskDataRate.Fsk50000    // 7
            };

            MaxADRDataRate = DataRateIndex.DR5;
            RegionLimits = new RegionLimits((Min: Hertz.Mega(863), Max: Hertz.Mega(870)), validDataRangeUpAndDownstream, validDataRangeUpAndDownstream, 0, 0);
        }

        /// <summary>
        /// Logic to get the correct downstream transmission frequency for region EU868.
        /// </summary>
        /// <param name="upstreamFrequency">Frequency on which the message was transmitted.</param>
        /// <param name="upstreamDataRate">Data rate at which the message was transmitted.</param>
        /// <param name="deviceJoinInfo">Join info for the device, if applicable.</param>
        public override bool TryGetDownstreamChannelFrequency(Hertz upstreamFrequency, DataRateIndex upstreamDataRate, DeviceJoinInfo deviceJoinInfo, out Hertz downstreamFrequency)
        {
            if (!IsValidUpstreamFrequency(upstreamFrequency))
                throw new LoRaProcessingException($"Invalid upstream frequency {upstreamFrequency}", LoRaProcessingErrorCode.InvalidFrequency);

            // in case of EU, you respond on same frequency as you sent data.
            downstreamFrequency = upstreamFrequency;
            return true;
        }

        /// <summary>
        /// Returns the default RX2 receive window parameters - frequency and data rate.
        /// </summary>
        /// <param name="deviceJoinInfo">Join info for the device, if applicable.</param>
        public override ReceiveWindow GetDefaultRX2ReceiveWindow(DeviceJoinInfo deviceJoinInfo = null) => new ReceiveWindow(DataRateIndex.DR0, Hertz.Mega(869.525));
    }
}
