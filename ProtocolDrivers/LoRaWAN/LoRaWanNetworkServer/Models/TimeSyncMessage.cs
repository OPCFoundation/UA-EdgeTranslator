// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System.Runtime.Serialization;

    [DataContract]
    public class TimeSyncMessage
    {
        [DataMember(Name = "txtime")]
        public ulong TxTime { get; set; }

        [DataMember(Name = "gpstime")]
        public ulong GpsTime { get; set; }

        [DataMember(Name = "msgtype")]
        public string MsgType { get; set; }
    }
}
