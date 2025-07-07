// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models
{
    using System.Runtime.Serialization;

    [DataContract]
    public class DiscoveryMessage
    {
        [DataMember(Name = "router")]
        public string Router { get; set; }
    }

    [DataContract]
    public class DiscoveryResponseMessage
    {
        [DataMember(Name = "router")]
        public string Router { get; set; }

        [DataMember(Name = "muxs")]
        public string Muxs { get; set; }

        [DataMember(Name = "uri")]
        public string Uri { get; set; }
    }

    [DataContract]
    public class DiscoveryErrorMessage
    {
        [DataMember(Name = "router")]
        public string Router { get; set; }

        [DataMember(Name = "error")]
        public string Error { get; set; }
    }
}
