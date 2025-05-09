// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    using System.Threading.Tasks;

    /// <summary>
    /// Defines a lora cloud device message received from the network server allowing it to be abandoned, rejected and completed.
    /// </summary>
    public interface IReceivedLoRaCloudToDeviceMessage : ILoRaCloudToDeviceMessage
    {
        byte[] GetPayload();

        Task<bool> CompleteAsync();

        Task<bool> AbandonAsync();

        Task<bool> RejectAsync();
    }
}
