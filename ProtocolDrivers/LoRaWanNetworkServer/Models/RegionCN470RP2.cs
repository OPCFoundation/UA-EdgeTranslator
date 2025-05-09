// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer;

    // Frequency plan for region CN470-510 using version RP002-1.0.3 of LoRaWAN Regional Parameters specification
    public class RegionCN470RP2 : Region
    {
        private static readonly Mega FrequencyIncrement = new(0.2);

        private readonly List<Hertz> rx2OTAADefaultFrequencies;

        private readonly List<List<Hertz>> downstreamFrequenciesByPlanType;

        // Dictionary mapping upstream join frequencies to a tuple containing
        // the corresponding downstream join frequency and the channel index
        public Dictionary<Hertz, (Hertz downstreamFreq, int joinChannelIndex)> UpstreamJoinFrequenciesToDownstreamAndChannelIndex { get; }

        private static readonly ImmutableDictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)> DrToConfigurationByDrIndex =
            new Dictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)>
            {
                // Values assuming FOpts param is not used
                [DataRateIndex.DR0] = (LoRaDataRate.SF12BW125, MaxPayloadSize: 59),
                [DataRateIndex.DR1] = (LoRaDataRate.SF11BW125, MaxPayloadSize: 31),
                [DataRateIndex.DR2] = (LoRaDataRate.SF10BW125, MaxPayloadSize: 94),
                [DataRateIndex.DR3] = (LoRaDataRate.SF9BW125, MaxPayloadSize: 192),
                [DataRateIndex.DR4] = (LoRaDataRate.SF8BW125, MaxPayloadSize: 250),
                [DataRateIndex.DR5] = (LoRaDataRate.SF7BW125, MaxPayloadSize: 250),
                [DataRateIndex.DR6] = (LoRaDataRate.SF7BW500, MaxPayloadSize: 250),
                [DataRateIndex.DR7] = (FskDataRate.Fsk50000, MaxPayloadSize: 250),
            }.ToImmutableDictionary();

        public override IReadOnlyDictionary<DataRateIndex, (DataRate DataRate, uint MaxPayloadSize)> DRtoConfiguration => DrToConfigurationByDrIndex;

        private static readonly ImmutableDictionary<uint, double> MaxEirpByTxPower =
            new Dictionary<uint, double>
            {
                [0] = 19,
                [1] = 17,
                [2] = 15,
                [3] = 13,
                [4] = 11,
                [5] = 9,
                [6] = 7,
                [7] = 5,
            }.ToImmutableDictionary();

        public override IReadOnlyDictionary<uint, double> TXPowertoMaxEIRP => MaxEirpByTxPower;

        private static readonly ImmutableArray<IReadOnlyList<DataRateIndex>> RX1DROffsetTableInternal =
            new IReadOnlyList<DataRateIndex>[]
            {
                new[] {DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0, DataRateIndex.DR0 }.ToImmutableArray(),
                new[] {DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1 }.ToImmutableArray(),
                new[] {DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1 }.ToImmutableArray(),
                new[] {DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1 }.ToImmutableArray(),
                new[] {DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR1, DataRateIndex.DR1 }.ToImmutableArray(),
                new[] {DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1, DataRateIndex.DR1 }.ToImmutableArray(),
                new[] {DataRateIndex.DR6, DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2, DataRateIndex.DR1 }.ToImmutableArray(),
                new[] {DataRateIndex.DR7, DataRateIndex.DR6, DataRateIndex.DR5, DataRateIndex.DR4, DataRateIndex.DR3, DataRateIndex.DR2 }.ToImmutableArray(),
            }.ToImmutableArray();

        public override IReadOnlyList<IReadOnlyList<DataRateIndex>> RX1DROffsetTable => RX1DROffsetTableInternal;

        public RegionCN470RP2()
            : base(LoRaRegionType.CN470RP2)
        {
            var validDatarates = new HashSet<DataRate>
            {
                LoRaDataRate.SF12BW125, // 0
                LoRaDataRate.SF11BW125, // 1
                LoRaDataRate.SF10BW125, // 2
                LoRaDataRate.SF9BW125,  // 3
                LoRaDataRate.SF8BW125,  // 4
                LoRaDataRate.SF7BW125,  // 5
                LoRaDataRate.SF7BW500,  // 6
                FskDataRate.Fsk50000    // 7
            };

            MaxADRDataRate = DataRateIndex.DR7;
            RegionLimits = new RegionLimits((Min: Hertz.Mega(470.3), Max: Hertz.Mega(509.7)), validDatarates, validDatarates, 0, 0);

            UpstreamJoinFrequenciesToDownstreamAndChannelIndex = new Dictionary<Hertz, (Hertz, int)>
            {
                [Hertz.Mega(470.9)] = (Hertz.Mega(484.5), 0),
                [Hertz.Mega(472.5)] = (Hertz.Mega(486.1), 1),
                [Hertz.Mega(474.1)] = (Hertz.Mega(487.7), 2),
                [Hertz.Mega(475.7)] = (Hertz.Mega(489.3), 3),
                [Hertz.Mega(504.1)] = (Hertz.Mega(490.9), 4),
                [Hertz.Mega(505.7)] = (Hertz.Mega(492.5), 5),
                [Hertz.Mega(507.3)] = (Hertz.Mega(494.1), 6),
                [Hertz.Mega(508.9)] = (Hertz.Mega(495.7), 7),
                [Hertz.Mega(479.9)] = (Hertz.Mega(479.9), 8),
                [Hertz.Mega(499.9)] = (Hertz.Mega(499.9), 9),
                [Hertz.Mega(470.3)] = (Hertz.Mega(492.5), 10),
                [Hertz.Mega(472.3)] = (Hertz.Mega(492.5), 11),
                [Hertz.Mega(474.3)] = (Hertz.Mega(492.5), 12),
                [Hertz.Mega(476.3)] = (Hertz.Mega(492.5), 13),
                [Hertz.Mega(478.3)] = (Hertz.Mega(492.5), 14),
                [Hertz.Mega(480.3)] = (Hertz.Mega(502.5), 15),
                [Hertz.Mega(482.3)] = (Hertz.Mega(502.5), 16),
                [Hertz.Mega(484.3)] = (Hertz.Mega(502.5), 17),
                [Hertz.Mega(486.3)] = (Hertz.Mega(502.5), 18),
                [Hertz.Mega(488.3)] = (Hertz.Mega(502.5), 19)
            };

            this.downstreamFrequenciesByPlanType = new List<List<Hertz>>
            {
                ListFrequencyPlan(Hertz.Mega(483.9), 0, 31).Concat(ListFrequencyPlan(Hertz.Mega(490.3), 32, 63)).ToList(),
                ListFrequencyPlan(Hertz.Mega(476.9), 0, 31).Concat(ListFrequencyPlan(Hertz.Mega(496.9), 32, 63)).ToList(),
                ListFrequencyPlan(Hertz.Mega(490.1), 0, 23).ToList(),
                ListFrequencyPlan(Hertz.Mega(500.1), 0, 23).ToList()
            };

            static IEnumerable<Hertz> ListFrequencyPlan(Hertz startFrequency, int startChannel, int endChannel)
            {
                var currentFreq = startFrequency;

                for (var channel = startChannel; channel <= endChannel; ++channel)
                {
                    yield return currentFreq;
                    currentFreq += FrequencyIncrement;
                }
            }

            this.rx2OTAADefaultFrequencies = new List<Hertz>
            {
                // 20 MHz plan A devices
                Hertz.Mega(485.3), Hertz.Mega(486.9), Hertz.Mega(488.5), Hertz.Mega(490.1),
                Hertz.Mega(491.7), Hertz.Mega(493.3), Hertz.Mega(494.9), Hertz.Mega(496.5),
                // 20 MHz plan B devices
                Hertz.Mega(478.3), Hertz.Mega(498.3),
            };
        }

        /// <summary>
        /// Returns join channel index for region CN470 matching the frequency of the join request.
        /// </summary>
        public override bool TryGetJoinChannelIndex(Hertz frequency, out int channelIndex)
        {
            channelIndex = -1;

            if (UpstreamJoinFrequenciesToDownstreamAndChannelIndex.TryGetValue(frequency, out var elem))
                channelIndex = elem.joinChannelIndex;

            return channelIndex != -1;
        }

        /// <summary>
        /// Logic to get the correct downstream transmission frequency for region CN470.
        /// <param name="upstreamFrequency">The frequency at which the message was transmitted.</param>
        /// <param name="upstreamDataRate">The upstream data rate.</param>
        /// <param name="deviceJoinInfo">Join info for the device, if applicable.</param>
        /// </summary>
        public override bool TryGetDownstreamChannelFrequency(Hertz upstreamFrequency, DataRateIndex upstreamDataRate, DeviceJoinInfo deviceJoinInfo, out Hertz downstreamFrequency)
        {
            ArgumentNullException.ThrowIfNull(deviceJoinInfo);

            if (!IsValidUpstreamFrequency(upstreamFrequency))
                throw new LoRaProcessingException($"Invalid upstream frequency {upstreamFrequency}", LoRaProcessingErrorCode.InvalidFrequency);

            // We prioritize the selection of join channel index from reported twin properties (set for OTAA devices)
            // over desired twin properties (set for APB devices).
            switch (deviceJoinInfo.ReportedCN470JoinChannel ?? deviceJoinInfo.DesiredCN470JoinChannel)
            {
                case <= 7: // 20 MHz plan A
                {
                    var channelNumber = upstreamFrequency < Hertz.Mega(500) ? GetChannelNumber(upstreamFrequency, Hertz.Mega(470.3)) : 32 + GetChannelNumber(upstreamFrequency, Hertz.Mega(503.5));
                    downstreamFrequency = this.downstreamFrequenciesByPlanType[0][channelNumber];
                    return true;
                }
                case <= 9: // 20 MHz plan B
                {
                    var channelNumber = upstreamFrequency < Hertz.Mega(490) ? GetChannelNumber(upstreamFrequency, Hertz.Mega(476.9)) : 32 + GetChannelNumber(upstreamFrequency, Hertz.Mega(496.9));
                    downstreamFrequency = this.downstreamFrequenciesByPlanType[1][channelNumber];
                    return true;
                }
                case <= 14: // 26 MHz plan A
                {
                    var channelNumber = GetChannelNumber(upstreamFrequency, Hertz.Mega(470.3));
                    downstreamFrequency = this.downstreamFrequenciesByPlanType[2][channelNumber % 24];
                    return true;
                }
                case <= 19: // 26 MHz plan B
                {
                    var channelNumber = GetChannelNumber(upstreamFrequency, Hertz.Mega(480.3));
                    downstreamFrequency = this.downstreamFrequenciesByPlanType[3][channelNumber % 24];
                    return true;
                }
                default:
                    downstreamFrequency = default;
                    return false;
            }

            static int GetChannelNumber(Hertz upstreamChannelFrequency, Hertz startUpstreamFreq) =>
                (int)Math.Round((upstreamChannelFrequency - startUpstreamFreq) / FrequencyIncrement.Units, 0, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Returns the default RX2 receive window parameters - frequency and data rate.
        /// </summary>
        /// <param name="deviceJoinInfo">Join info for the device.</param>
        public override ReceiveWindow GetDefaultRX2ReceiveWindow(DeviceJoinInfo deviceJoinInfo)
        {
            ArgumentNullException.ThrowIfNull(deviceJoinInfo);

            // Default data rate is always 1 for CN470
            const DataRateIndex dataRate = DataRateIndex.DR1;

            var rx2Window = new ReceiveWindow(dataRate, default);

            // OTAA device
            if (deviceJoinInfo.ReportedCN470JoinChannel != null)
            {
                // 20 MHz plan A or B
                if (deviceJoinInfo.ReportedCN470JoinChannel < this.rx2OTAADefaultFrequencies.Count)
                    return new ReceiveWindow(dataRate, this.rx2OTAADefaultFrequencies[(int)deviceJoinInfo.ReportedCN470JoinChannel]);
                // 26 MHz plan A
                else if (deviceJoinInfo.ReportedCN470JoinChannel <= 14)
                {
                    return new ReceiveWindow(dataRate, Hertz.Mega(492.5));
                }
                // 26 MHz plan B
                else if (deviceJoinInfo.ReportedCN470JoinChannel <= 19)
                {
                    return new ReceiveWindow(dataRate, Hertz.Mega(502.5));
                }
            }

            // ABP device
            else if (deviceJoinInfo.DesiredCN470JoinChannel != null)
            {
                // 20 MHz plan A
                if (deviceJoinInfo.DesiredCN470JoinChannel <= 7)
                    return new ReceiveWindow(dataRate, Hertz.Mega(486.9));
                // 20 MHz plan B
                else if (deviceJoinInfo.DesiredCN470JoinChannel <= 9)
                {
                    return new ReceiveWindow(dataRate, Hertz.Mega(498.3));
                }
                // 26 MHz plan A
                else if (deviceJoinInfo.DesiredCN470JoinChannel <= 14)
                {
                    return new ReceiveWindow(dataRate, Hertz.Mega(492.5));
                }
                // 26 MHz plan B
                else if (deviceJoinInfo.DesiredCN470JoinChannel <= 19)
                {
                    return new ReceiveWindow(dataRate, Hertz.Mega(502.5));
                }
            }

            return rx2Window;
        }
    }
}
