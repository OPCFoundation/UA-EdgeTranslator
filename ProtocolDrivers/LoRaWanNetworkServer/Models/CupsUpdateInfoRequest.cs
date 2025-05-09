// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using global::LoRaWan;

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;
    using System.Collections.Immutable;
    using System.Runtime.Serialization;

    [DataContract]
    public class CupsUpdateInfoRequest
    {
        [DataMember(Name = "router")]
        public StationEui StationEui { get; }

        [DataMember(Name = "cupsUri")]
        public Uri? CupsUri { get; }

        [DataMember(Name = "tcUri")]
        public Uri? TcUri { get; }

        [DataMember(Name = "cupsCredCrc")]
        public uint CupsCredentialsChecksum { get; }

        [DataMember(Name = "tcCredCrc")]
        public uint TcCredentialsChecksum { get; }

        [DataMember(Name = "package")]
        public string? Package { get; }

        [DataMember(Name = "keys")]
        public ImmutableArray<uint> KeyChecksums { get; }
    }
}
