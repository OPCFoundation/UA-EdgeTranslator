// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LoRaWANContainer.LoRaWan.NetworkServer.Models;

namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    public interface IMessageDispatcher
    {
        void DispatchRequest(LoRaRequest request);
    }
}