// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System.Runtime.Serialization;
    using global::LoRaWan;

    public static class LnsData
    {
        [DataContract]
        public class BasicMessage
        {
            [DataMember(Name = "msgtype")]
            public LnsMessageType MessageType { get; set; }
        }

        [DataContract]
        public class VersionMessage
        {
            [DataMember(Name = "msgtype")]
            public LnsMessageType MessageType { get; set; }

            [DataMember(Name = "station")]
            public string Station { get; set; }

            [DataMember(Name = "firmware")]
            public string Firmware { get; set; }

            [DataMember(Name = "package")]
            public string Package { get; set; }

            [DataMember(Name = "model")]
            public string Model { get; set; }

            [DataMember(Name = "protocol")]
            public int Protocol { get; set; }

            [DataMember(Name = "features")]
            public string Features { get; set; }
        }

        [DataContract]
        public class UpstreamDataFrame
        {
            [DataMember(Name = "msgtype")]
            public string MessageType { get; set; }

            [DataMember(Name = "MHdr")]
            public uint MHdr { get; set; }

            [DataMember(Name = "DevAddr")]
            public int DevAddr { get; set; }

            [DataMember(Name = "FCtrl")]
            public uint FCtrl { get; set; }

            [DataMember(Name = "FCnt")]
            public uint FCnt { get; set; }

            [DataMember(Name = "FOpts")]
            public string FOpts { get; set; }

            [DataMember(Name = "FPort")]
            public int FPort { get; set; }

            [DataMember(Name = "FRMPayload")]
            public string FRMPayload { get; set; }

            [DataMember(Name = "MIC")]
            public int MIC { get; set; }

            [DataMember(Name = "RADIOMETADATA")]
            public RadioMetadata RadioMetadata { get; set; }
        }

        [DataContract]
        public class RadioMetadata
        {
            [DataMember(Name = "DR")]
            public DataRateIndex DataRate { get; set; }

            [DataMember(Name = "Freq")]
            public Hertz Frequency { get; set; }

            [DataMember(Name = "upinfo")]
            public RadioMetadataUpInfo UpInfo { get; set; }
        }

        [DataContract]
        public class RadioMetadataUpInfo
        {
            [DataMember(Name = "rctx")]
            public uint AntennaPreference { get; set; }

            [DataMember(Name = "xtime")]
            public uint Xtime { get; set; }

            [DataMember(Name = "gpstime")]
            public uint GpsTime { get; set; }

            [DataMember(Name = "rssi")]
            public float ReceivedSignalStrengthIndication { get; set; }

            [DataMember(Name = "snr")]
            public float SignalNoiseRatio { get; set; }
        }

        [DataContract]
        public class UpstreamDataMessage
        {
            [DataMember(Name = "msgtype")]
            public LnsMessageType MessageType { get; set; }

            [DataMember(Name = "MHdr")]
            public MacHeader MacHeader { get; }

            [DataMember(Name = "DevAddr")]
            public DevAddr DevAddr { get; }

            [DataMember(Name = "FCtrl")]
            public FrameControlFlags FrameControlFlags { get; }

            [DataMember(Name = "FCnt")]
            public ushort Counter { get; }

            [DataMember(Name = "FOpts")]
            public string Options { get; }

            [DataMember(Name = "FPort")]
            public FramePort? Port { get; }

            [DataMember(Name = "FRMPayload")]
            public string Payload { get; }

            [DataMember(Name = "MIC")]
            public MessageIntegrityCode Mic { get; }

            [DataMember(Name = "RADIOMETADATA")]
            public RadioMetadata RadioMetadata { get; }
        }

        [DataContract]
        public class JoinRequestMessage
        {
            [DataMember(Name = "msgtype")]
            public LnsMessageType MessageType { get; set; }

            [DataMember(Name = "MHdr")]
            public MacHeader MacHeader { get; }

            [DataMember(Name = "JoinEUI")]
            public JoinEui JoinEui { get; }

            [DataMember(Name = "DevEUI")]
            public DevEui DevEui { get; }

            [DataMember(Name = "DevNonce")]
            public DevNonce DevNonce { get; }

            [DataMember(Name = "MIC")]
            public MessageIntegrityCode Mic { get; }

            [DataMember(Name = "RADIOMETADATA")]
            public RadioMetadata RadioMetadata { get; }
        }

    }
}
