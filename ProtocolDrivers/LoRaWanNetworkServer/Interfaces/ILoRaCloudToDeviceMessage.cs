// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    using System.Collections.Generic;
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;

    /// <summary>
    /// Defines the data contract for cloud to device messages.
    /// </summary>
    public interface ILoRaCloudToDeviceMessage
    {
        DevEui? DevEUI { get; }

        public FramePort Fport { get; }

        bool Confirmed { get; }

        string MessageId { get; }

        /// <summary>
        /// Gets list of mac commands that are part of the message.
        /// </summary>
        IList<MacCommand> MacCommands { get; }

        /// <summary>
        /// Identifies if the message is a valid LoRa downstream message.
        /// </summary>
        /// <param name="errorMessage">Returns the error message in case it fails.</param>
        /// <returns>True if the message is valid, false otherwise.</returns>
        bool IsValid(out string errorMessage);
    }
}
