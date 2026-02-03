// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models
{
    using LoRaWan;

    public class DeviceInfo(DevEui devEui, AppKey appKey)
    {
        public DevEui DevEUI { get; set; } = devEui;

        public AppKey AppKey { get; set; } = appKey;
    }
}
