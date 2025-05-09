// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    using System.Threading.Tasks;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;

    // Interface for sending downstream messages
    public interface IDownstreamMessageSender
    {
        /// <summary>
        /// Send downstream message to LoRa device.
        /// </summary>
        Task SendDownstreamAsync(DownlinkMessage message);
    }
}
