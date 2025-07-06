// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using Newtonsoft.Json;

    /// <summary>
    /// Defines the result of a decoder.
    /// </summary>
    public class DecodePayloadResult
    {
        /// <summary>
        /// Gets or sets the decoded value
        /// </summary>
        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
        public object Value { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        [JsonProperty("errorDetail", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorDetail { get; set; }
    }
}
