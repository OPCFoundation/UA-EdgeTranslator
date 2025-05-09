// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using global::LoRaWan;

    public static class LnsStationConfiguration
    {
        public enum Radio { Zero, One }

        public static T Map<T>(this Radio radio, T zero, T one) => radio switch
        {
            Radio.Zero => zero,
            Radio.One => one,
            _ => throw new ArgumentException(null, nameof(radio))
        };

        [DataContract]
        public class ChannelConfig(bool enable, Radio radio, int @if)
        {
            [DataMember(Name = "enable")]
            public bool Enable { get; } = enable;

            [DataMember(Name = "radio")]
            public Radio Radio { get; } = radio;

            [DataMember(Name = "if")]
            public int If { get; } = @if;
        }

        [DataContract]
        public class StandardConfig(bool enable, Radio radio, int @if, Bandwidth bandwidth, SpreadingFactor spreadingFactor)
        {
            [DataMember(Name = "enable")]
            public bool Enable { get; } = enable;

            [DataMember(Name = "radio")]
            public Radio Radio { get; } = radio;

            [DataMember(Name = "if")]
            public int If { get; } = @if;

            [DataMember(Name = "bandwidth")]
            public Bandwidth Bandwidth { get; } = bandwidth;

            [DataMember(Name = "spread_factor")]
            public SpreadingFactor SpreadingFactor { get; } = spreadingFactor;
        }

        [DataContract]
        public class RadioConfig(bool enable, Hertz freq)
        {
            [DataMember(Name = "enable")]
            public bool Enable { get; } = enable;

            [DataMember(Name = "freq")]
            public Hertz Freq { get; } = freq;
        }

        [DataContract]
        public class Sx1301Config(RadioConfig radio0,
                                  RadioConfig radio1,
                                  StandardConfig channelLoraStd,
                                  ChannelConfig channelFsk,
                                  ChannelConfig channelMultiSf0,
                                  ChannelConfig channelMultiSf1,
                                  ChannelConfig channelMultiSf2,
                                  ChannelConfig channelMultiSf3,
                                  ChannelConfig channelMultiSf4,
                                  ChannelConfig channelMultiSf5,
                                  ChannelConfig channelMultiSf6,
                                  ChannelConfig channelMultiSf7)
        {
            [DataMember(Name = "radio_0")]
            public RadioConfig Radio0 { get; } = radio0;

            [DataMember(Name = "radio_1")]
            public RadioConfig Radio1 { get; } = radio1;

            [DataMember(Name = "chan_Lora_std")]
            public StandardConfig ChannelLoraStd { get; } = channelLoraStd;

            [DataMember(Name = "chan_FSK")]
            public ChannelConfig ChannelFsk { get; } = channelFsk;

            [DataMember(Name = "chan_multiSF_0")]
            public ChannelConfig ChannelMultiSf0 { get; } = channelMultiSf0;

            [DataMember(Name = "chan_multiSF_1")]
            public ChannelConfig ChannelMultiSf1 { get; } = channelMultiSf1;

            [DataMember(Name = "chan_multiSF_2")]
            public ChannelConfig ChannelMultiSf2 { get; } = channelMultiSf2;

            [DataMember(Name = "chan_multiSF_3")]
            public ChannelConfig ChannelMultiSf3 { get; } = channelMultiSf3;

            [DataMember(Name = "chan_multiSF_4")]
            public ChannelConfig ChannelMultiSf4 { get; } = channelMultiSf4;

            [DataMember(Name = "chan_multiSF_5")]
            public ChannelConfig ChannelMultiSf5 { get; } = channelMultiSf5;

            [DataMember(Name = "chan_multiSF_6")]
            public ChannelConfig ChannelMultiSf6 { get; } = channelMultiSf6;

            [DataMember(Name = "chan_multiSF_7")]
            public ChannelConfig ChannelMultiSf7 { get; } = channelMultiSf7;
        }

        [DataContract]
        public class RouterConfig
        {
            [DataMember(Name = "msgtype")]
            public string MsgType { get; set; } = "router_config";

            [DataMember(Name = "NetID")]
            public List<int> NetID { get; set; } = [];

            [DataMember(Name = "JoinEui")]
            public List<(ulong Begin, ulong End)> JoinEui { get; set; } = []; // ranges: beg, end inclusive

            [DataMember(Name = "region")]
            public string Region { get; set; } = string.Empty; // e.g., "EU863", "US902", etc.

            [DataMember(Name = "hwspec")]
            public string HwSpec { get; set; } = string.Empty;

            [DataMember(Name = "freq_range")]
            public (ulong Min, ulong Max) FreqRange { get; set; } // min, max (hz)

            [DataMember(Name = "DRs")]
            public List<(int SpreadingFactor, int Bandwidth, bool DownloadOnly)> DRs { get; set; } = []; // sf, bw, dnonly

            [DataMember(Name = "sx1301_conf")]
            public List<Sx1301Config> Sx1301Conf { get; set; } = [];

            [DataMember(Name = "nocca")]
            public bool NoCca { get; set; }

            [DataMember(Name = "nodc")]
            public bool NoDc { get; set; }

            [DataMember(Name = "nodwell")]
            public bool NoDwell { get; set; }

            [DataMember(Name = "bcning")]
            public Beaconing Bcning { get; set; }
        }

        [DataContract]
        public class Beaconing
        {
            [DataMember(Name = "DR")]
            public uint DR { get; set; }

            [DataMember(Name = "layout")]
            public uint[] Layout { get; set; }

            [DataMember(Name = "freqs")]
            public uint[] Freqs { get; set; }
        }
    }
}
