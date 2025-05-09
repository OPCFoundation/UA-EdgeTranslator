// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer;

    // Frequency plan for region CN470-510 using version 1 of LoRaWAN 1.0.3 Regional Parameters specification
    public class RegionCN470RP1 : Region
    {
        private static readonly Hertz StartingUpstreamFrequency = Hertz.Mega(470.3);
        private static readonly Hertz StartingDownstreamFrequency = Hertz.Mega(500.3);
        private static readonly Mega FrequencyIncrement = new(0.2);

        private const int DownstreamChannelCount = 48;

        private static readonly ImmutableDictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)> DrToConfigurationByDrIndex =
            new Dictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)>
            {
                [DataRateIndex.DR0] = (LoRaDataRate.SF12BW125, MaxPayloadSize: 59),
                [DataRateIndex.DR1] = (LoRaDataRate.SF11BW125, MaxPayloadSize: 59),
                [DataRateIndex.DR2] = (LoRaDataRate.SF10BW125, MaxPayloadSize: 59),
                [DataRateIndex.DR3] = (LoRaDataRate.SF9BW125, MaxPayloadSize: 123),
                [DataRateIndex.DR4] = (LoRaDataRate.SF8BW125, MaxPayloadSize: 230),
                [DataRateIndex.DR5] = (LoRaDataRate.SF7BW125, MaxPayloadSize: 230),
            }.ToImmutableDictionary();

        public override IReadOnlyDictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)> DRtoConfiguration => DrToConfigurationByDrIndex;

        private static readonly ImmutableDictionary<uint, double> MaxEirpByTxPower =
            new Dictionary<uint, double>
            {
                [0] = 19.15,
                [1] = 17.15,
                [2] = 15.15,
                [3] = 13.15,
                [4] = 11.15,
                [5] = 9.15,
                [6] = 7.15,
                [7] = 5.15,
            }.ToImmutableDictionary();

        public override IReadOnlyDictionary<uint, double> TXPowertoMaxEIRP => MaxEirpByTxPower;

        private static readonly ImmutableArray<IReadOnlyList<DataRateIndex>> RX1DROffsetTableInternal =
            new IReadOnlyList<DataRateIndex>[]
            {
                new[] {DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] {DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] {DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] {DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] {DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] {DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0 }.ToImmutableArray(),
            }.ToImmutableArray();

        public override IReadOnlyList<IReadOnlyList<DataRateIndex>> RX1DROffsetTable => RX1DROffsetTableInternal;

        public RegionCN470RP1()
            : base(LoRaRegionType.CN470RP1)
        {
            var validDatarates = new HashSet<DataRate>
            {
                LoRaDataRate.SF12BW125, // 0
                LoRaDataRate.SF11BW125, // 1
                LoRaDataRate.SF10BW125, // 2
                LoRaDataRate.SF9BW125,  // 3
                LoRaDataRate.SF8BW125,  // 4
                LoRaDataRate.SF7BW125   // 5
            };

            MaxADRDataRate = DataRateIndex.DR5;
            RegionLimits = new RegionLimits((Min: Hertz.Mega(470), Max: Hertz.Mega(510)), validDatarates, validDatarates, 0, 0);
        }

        /// <summary>
        /// Logic to get the correct downstream transmission frequency for region CN470.
        /// <param name="upstreamFrequency">The frequency at which the message was transmitted.</param>
        /// <param name="upstreamDataRate">The upstream data rate.</param>
        /// <param name="deviceJoinInfo">Join info for the device, if applicable.</param>
        /// </summary>
        public override bool TryGetDownstreamChannelFrequency(Hertz upstreamFrequency, DataRateIndex upstreamDataRate, DeviceJoinInfo deviceJoinInfo, out Hertz downstreamFrequency)
        {
            if (!IsValidUpstreamFrequency(upstreamFrequency))
                throw new LoRaProcessingException($"Invalid upstream frequency {upstreamFrequency}", LoRaProcessingErrorCode.InvalidFrequency);

            var upstreamChannelNumber = (int)Math.Round(
                (upstreamFrequency - StartingUpstreamFrequency) / FrequencyIncrement.Units,
                0,
                MidpointRounding.AwayFromZero);

            downstreamFrequency = StartingDownstreamFrequency + checked((long)(upstreamChannelNumber % DownstreamChannelCount * FrequencyIncrement.Units));

            return true;
        }

        /// <summary>
        /// Returns the default RX2 receive window parameters - frequency and data rate.
        /// </summary>
        /// <param name="deviceJoinInfo">Join info for the device.</param>
        public override ReceiveWindow GetDefaultRX2ReceiveWindow(DeviceJoinInfo deviceJoinInfo = null) => new ReceiveWindow(DataRateIndex.DR0, Hertz.Mega(505.3));
    }
}
