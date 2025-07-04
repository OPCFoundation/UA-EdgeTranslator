// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using global::LoRaWan;
    using Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models;

    public class RegionAS923 : DwellTimeLimitedRegion
    {
        private static readonly Hertz Channel0Frequency = Hertz.Mega(923.2);
        private static readonly Hertz Channel1Frequency = Hertz.Mega(923.4);

        private static readonly ImmutableDictionary<DataRateIndex, (DataRate configuration, uint maxPyldSize)> DrToConfigurationByDrIndexNoDwell =
            new Dictionary<DataRateIndex, (DataRate configuration, uint maxPyldSize)>
            {
                [DataRateIndex.DR0] = (LoRaDataRate.SF12BW125, 59),
                [DataRateIndex.DR1] = (LoRaDataRate.SF11BW125, 59),
                [DataRateIndex.DR2] = (LoRaDataRate.SF10BW125, 123),
                [DataRateIndex.DR3] = (LoRaDataRate.SF9BW125, 123),
                [DataRateIndex.DR4] = (LoRaDataRate.SF8BW125, 230),
                [DataRateIndex.DR5] = (LoRaDataRate.SF7BW125, 230),
                [DataRateIndex.DR6] = (LoRaDataRate.SF7BW250, 230),
                [DataRateIndex.DR7] = (FskDataRate.Fsk50000, 230)
            }.ToImmutableDictionary();

        private static readonly ImmutableHashSet<DataRate> ValidDataRatesDr0Dr7 =
            ImmutableHashSet.Create<DataRate>(LoRaDataRate.SF12BW125,
                                              LoRaDataRate.SF11BW125,
                                              LoRaDataRate.SF10BW125,
                                              LoRaDataRate.SF9BW125,
                                              LoRaDataRate.SF8BW125,
                                              LoRaDataRate.SF7BW125,
                                              LoRaDataRate.SF7BW250,
                                              FskDataRate.Fsk50000);

        private static readonly ImmutableArray<IReadOnlyList<DataRateIndex>> RX1DROffsetTableNoDwell =
            new IReadOnlyList<DataRateIndex>[]
            {
                new DataRateIndex[] { DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR1, DataRateIndex.DR2 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR2, DataRateIndex.DR3 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR3, DataRateIndex.DR4 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR4, DataRateIndex.DR5 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR5, DataRateIndex.DR6 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR0, DataRateIndex.DR6, DataRateIndex.DR7 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR6, DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR7, DataRateIndex.DR7 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR7, DataRateIndex.DR6, DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR7, DataRateIndex.DR7 }.ToImmutableArray(),
            }.ToImmutableArray();

        private static readonly RegionLimits RegionLimitsNoDwell =
            new RegionLimits((Min: Hertz.Mega(915), Max: Hertz.Mega(928)), ValidDataRatesDr0Dr7, ValidDataRatesDr0Dr7, DataRateIndex.DR0, DataRateIndex.DR0);

        private static readonly ImmutableDictionary<DataRateIndex, (DataRate configuration, uint maxPyldSize)> DrToConfigurationByDrIndexWithDwell =
            new Dictionary<DataRateIndex, (DataRate configuration, uint maxPyldSize)>
            {
                [DataRateIndex.DR0] = (LoRaDataRate.SF12BW125, 0),
                [DataRateIndex.DR1] = (LoRaDataRate.SF11BW125, 0),
                [DataRateIndex.DR2] = (LoRaDataRate.SF10BW125, 19),
                [DataRateIndex.DR3] = (LoRaDataRate.SF9BW125, 61),
                [DataRateIndex.DR4] = (LoRaDataRate.SF8BW125, 133),
                [DataRateIndex.DR5] = (LoRaDataRate.SF7BW125, 230),
                [DataRateIndex.DR6] = (LoRaDataRate.SF7BW250, 230),
                [DataRateIndex.DR7] = (FskDataRate.Fsk50000, 230)
            }.ToImmutableDictionary();

        private static readonly ImmutableArray<IReadOnlyList<DataRateIndex>> RX1DROffsetTableWithDwell =
            new IReadOnlyList<DataRateIndex>[]
            {
                new DataRateIndex[] { DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR3 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR3, DataRateIndex.DR4 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR4, DataRateIndex.DR5 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR5, DataRateIndex.DR6 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR6, DataRateIndex.DR7 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR6, DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR2, DataRateIndex.DR7, DataRateIndex.DR7 }.ToImmutableArray(),
                new DataRateIndex[] { DataRateIndex.DR7, DataRateIndex.DR6, DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR7, DataRateIndex.DR7 }.ToImmutableArray(),
            }.ToImmutableArray();

        private static readonly ImmutableHashSet<DataRate> ValidDataRatesDr2Dr7 =
            ImmutableHashSet.Create<DataRate>(LoRaDataRate.SF10BW125,
                                              LoRaDataRate.SF9BW125,
                                              LoRaDataRate.SF8BW125,
                                              LoRaDataRate.SF7BW125,
                                              LoRaDataRate.SF7BW250,
                                              FskDataRate.Fsk50000);

        private static readonly RegionLimits RegionLimitsWithDwell =
            new RegionLimits((Min: Hertz.Mega(915), Max: Hertz.Mega(928)), ValidDataRatesDr0Dr7, ValidDataRatesDr2Dr7, DataRateIndex.DR0, DataRateIndex.DR2);

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

        private DwellTimeSetting dwellTimeSetting;

        public override IReadOnlyDictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)> DRtoConfiguration =>
            ApplyDwellTimeLimits ? DrToConfigurationByDrIndexWithDwell : DrToConfigurationByDrIndexNoDwell;

        public override IReadOnlyList<IReadOnlyList<DataRateIndex>> RX1DROffsetTable =>
            ApplyDwellTimeLimits ? RX1DROffsetTableWithDwell : RX1DROffsetTableNoDwell;

        protected override DwellTimeSetting DefaultDwellTimeSetting { get; } = new DwellTimeSetting() { DownlinkDwellTime = true, UplinkDwellTime = true, MaxEirp = 5 };

        private bool ApplyDwellTimeLimits => (this.dwellTimeSetting ?? DefaultDwellTimeSetting).DownlinkDwellTime;

        public long FrequencyOffset { get; private set; }

        public RegionAS923()
            : base(LoRaRegionType.AS923)
        {
            FrequencyOffset = 0;
            MaxADRDataRate = DataRateIndex.DR7;
            RegionLimits = ApplyDwellTimeLimits ? RegionLimitsWithDwell : RegionLimitsNoDwell;
        }

        /// <summary>
        /// Calculates the frequency offset (AS923_FREQ_OFFSET_HZ) value for region AS923.
        /// </summary>
        /// <param name="frequencyChannel0">Configured frequency for radio 0.</param>
        /// <param name="frequencyChannel1">Configured frequency for radio 1.</param>
        public RegionAS923 WithFrequencyOffset(Hertz frequencyChannel0, Hertz frequencyChannel1)
        {
            FrequencyOffset = frequencyChannel0 - Channel0Frequency;

            var channel1Offset = frequencyChannel1 - Channel1Frequency;
            if (channel1Offset != FrequencyOffset)
                throw new ArgumentException($"Provided channel frequencies {frequencyChannel0}, {frequencyChannel1} for Region {LoRaRegion} are inconsistent.");

            return this;
        }

        /// <summary>
        /// Logic to get the correct downstream transmission frequency for region CN470.
        /// </summary>
        /// <param name="upstreamFrequency">The frequency at which the message was transmitted.</param>
        /// <param name="upstreamDataRate">The upstream data rate.</param>
        /// <param name="deviceJoinInfo">Join info for the device, if applicable.</param>
        public override bool TryGetDownstreamChannelFrequency(Hertz upstreamFrequency, DataRateIndex upstreamDataRate, DeviceJoinInfo deviceJoinInfo, out Hertz downstreamFrequency)
        {
            if (!IsValidUpstreamFrequency(upstreamFrequency))
                throw new LoRaProcessingException($"Invalid upstream frequency {upstreamFrequency}", LoRaProcessingErrorCode.InvalidFrequency);

            // Use the same frequency as the upstream.
            downstreamFrequency = upstreamFrequency;
            return true;
        }

        /// <summary>
        /// Returns the default RX2 receive window parameters - frequency and data rate.
        /// </summary>
        /// <param name="deviceJoinInfo">Join info for the device.</param>
        public override ReceiveWindow GetDefaultRX2ReceiveWindow(DeviceJoinInfo deviceJoinInfo = null) =>
            new ReceiveWindow(DataRateIndex.DR2, Hertz.Mega(923.2) + FrequencyOffset);

        /// <inheritdoc/>
        public override void UseDwellTimeSetting(DwellTimeSetting dwellTimeSetting)
        {
            this.dwellTimeSetting = dwellTimeSetting;
            RegionLimits = ApplyDwellTimeLimits ? RegionLimitsWithDwell : RegionLimitsNoDwell;
        }
    }
}
