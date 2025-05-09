// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.



namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    using global::LoRaWan;
    using Newtonsoft.Json;

    public class DeviceInfo(DevEui devEui)
    {
        [JsonIgnore]
        public DevEui DevEUI { get; set; } = devEui;
    }
}
