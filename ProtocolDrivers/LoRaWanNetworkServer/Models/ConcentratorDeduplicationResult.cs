// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models
{
    public enum ConcentratorDeduplicationResult
    {
        /// <summary>
        /// First message on this LNS
        /// </summary>
        NotDuplicate,
        /// <summary>
        /// Duplicate message due to resubmit of a confirmed
        /// message of the same station (concentrator).
        /// </summary>
        DuplicateDueToResubmission,
        /// <summary>
        /// Detected as a duplicate but due to the DeduplicationStrategy,
        /// marked only as a "soft" duplicate - allow upstream (Mark and None)
        /// </summary>
        SoftDuplicateDueToDeduplicationStrategy,
        /// <summary>
        /// Message is a duplicate and does not need to be
        /// sent upstream.
        /// </summary>
        Duplicate
    }
}
