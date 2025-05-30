// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IClassCDeviceMessageSender
    {
        Task<bool> SendAsync(IReceivedLoRaCloudToDeviceMessage message, CancellationToken cts = default);
    }
}
