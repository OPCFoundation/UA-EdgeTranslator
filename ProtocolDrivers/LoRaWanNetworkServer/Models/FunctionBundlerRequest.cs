// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    public class FunctionBundlerRequest
    {
        public string GatewayId { get; set; }

        public uint ClientFCntUp { get; set; }

        public uint ClientFCntDown { get; set; }

        public double? Rssi { get; set; }

        public LoRaADRRequest AdrRequest { get; set; }

        public FunctionBundlerItemType FunctionItems { get; set; }
    }
}
