// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Newtonsoft.Json;

    internal sealed record CupsTwinInfo
    {
        // Credential management does not require anything more than the shared endpoint URIs and CRCs
        // Firmware management features could require to define fields in twin in a different way than the station/model/package ones
        [JsonPropertyName("cupsUri")]
        [JsonProperty("cupsUri")]
        public Uri CupsUri { get; set; }

        [JsonPropertyName("tcUri")]
        [JsonProperty("tcUri")]
        public Uri TcUri { get; set; }

        [JsonPropertyName("cupsCredCrc")]
        [JsonProperty("cupsCredCrc")]
        public uint CupsCredCrc { get; set; }

        [JsonPropertyName("tcCredCrc")]
        [JsonProperty("tcCredCrc")]
        public uint TcCredCrc { get; set; }

        [JsonPropertyName("cupsCredentialUrl")]
        [JsonProperty("cupsCredentialUrl")]
        public string CupsCredentialUrl { get; set; }

        [JsonPropertyName("tcCredentialUrl")]
        [JsonProperty("tcCredentialUrl")]
        public string TcCredentialUrl { get; set; }

        [JsonPropertyName("package")]
        [JsonProperty("package")]
        public string Package { get; set; }

        [JsonPropertyName("fwKeyChecksum")]
        [JsonProperty("fwKeyChecksum")]
        public uint FwKeyChecksum { get; set; }

        [JsonPropertyName("fwSignature")]
        [JsonProperty("fwSignature")]
        public string FwSignatureInBase64 { get; set; }

        [JsonPropertyName("fwUrl")]
        [JsonProperty("fwUrl")]
        public Uri FwUrl { get; set; }
    }
}
