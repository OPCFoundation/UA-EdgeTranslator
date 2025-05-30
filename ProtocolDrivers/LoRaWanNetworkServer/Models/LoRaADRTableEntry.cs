// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using global::LoRaWan;
    using Newtonsoft.Json;

    public class LoRaADRTableEntry
    {
        [JsonProperty(nameof(DevEui))]
        public string DevEuiString
        {
            get => DevEUI.ToString();
            set => DevEUI = DevEui.Parse(value);
        }

        [JsonIgnore]
        public DevEui DevEUI { get; set; }

        public uint FCnt { get; set; }

        public uint GatewayCount { get; set; }

        public string GatewayId { get; set; }

        public float Snr { get; set; }
    }
}
