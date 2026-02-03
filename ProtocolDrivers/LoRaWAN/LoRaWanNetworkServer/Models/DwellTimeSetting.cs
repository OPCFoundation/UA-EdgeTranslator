// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System.Runtime.Serialization;

    [DataContract]
    public class DwellTimeSetting()
    {
        [DataMember(Name = "downlinkDwellLimit")]
        public bool DownlinkDwellTime { get; set; }

        [DataMember(Name = "uplinkDwellLimit")]
        public bool UplinkDwellTime { get; set; }

        [DataMember(Name = "eirp")]
        public uint MaxEirp { get; set; }
    }
}
