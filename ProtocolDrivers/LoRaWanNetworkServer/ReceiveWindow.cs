// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer
{
    using global::LoRaWan;

    public record struct ReceiveWindow(DataRateIndex DataRate, Hertz Frequency);
}
