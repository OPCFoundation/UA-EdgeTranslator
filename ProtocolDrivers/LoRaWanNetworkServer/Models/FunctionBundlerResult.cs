// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    public class FunctionBundlerResult
    {
        public DeduplicationResult DeduplicationResult { get; set; }

        public LoRaADRResult AdrResult { get; set; }

        public uint? NextFCntDown { get; set; }

        public PreferredGatewayResult PreferredGatewayResult { get; set; }
    }
}
