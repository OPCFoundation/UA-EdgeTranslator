// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LoRaWan.NetworkServer;

namespace Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models
{
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;

    public class LoRaDeviceRequestProcessResult
    {
        public LoRaDeviceRequestProcessResult(LoRaDevice loRaDevice, LoRaRequest request, DownlinkMessage downlinkMessage = null)
        {
            LoRaDevice = loRaDevice;
            Request = request;
            DownlinkMessage = downlinkMessage;
        }

        public LoRaDeviceRequestProcessResult(LoRaDevice loRaDevice, LoRaRequest request, LoRaDeviceRequestFailedReason failedReason)
        {
            LoRaDevice = loRaDevice;
            Request = request;
            FailedReason = failedReason;
        }

        public DownlinkMessage DownlinkMessage { get; }

        public LoRaRequest Request { get; }

        public LoRaDevice LoRaDevice { get; }

        public LoRaDeviceRequestFailedReason? FailedReason { get; }
    }
}
