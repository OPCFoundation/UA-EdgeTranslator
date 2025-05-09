// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LoRaWan.NetworkServer;

namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    using System.Threading.Tasks;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;

    /// <summary>
    /// Request handler for data requests.
    /// </summary>
    public interface ILoRaDataRequestHandler
    {
        Task<LoRaDeviceRequestProcessResult> ProcessRequestAsync(LoRaRequest request, LoRaDevice loRaDevice);

        void SetClassCMessageSender(IClassCDeviceMessageSender classCMessageSender);
    }
}
