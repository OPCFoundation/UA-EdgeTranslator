// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;
    using System;

    /// <summary>
    /// Message dispatcher.
    /// </summary>
    public sealed class MessageDispatcher(
        NetworkServerConfiguration configuration,
        JoinRequestMessageHandler joinRequestHandler,
        DataMessageHandler dataMessageHandler,
        ILogger<MessageDispatcher> logger)
    {
        private readonly NetworkServerConfiguration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        /// <summary>
        /// Dispatches a request.
        /// </summary>
        public void DispatchRequest(LoRaRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Payload is null)
            {
                throw new LoRaProcessingException(nameof(request.Payload), LoRaProcessingErrorCode.PayloadNotSet);
            }

            if (request.Region is null)
            {
                throw new LoRaProcessingException(nameof(request.Region), LoRaProcessingErrorCode.RegionNotSet);
            }

            if (request.Payload.MessageType == MacMessageType.JoinRequest)
            {
                joinRequestHandler.DispatchRequest(request);
            }
            else if (request.Payload.MessageType is MacMessageType.UnconfirmedDataUp or MacMessageType.ConfirmedDataUp)
            {
                using var scope = logger.BeginDeviceAddressScope(request.Payload.DevAddr);
                if (!IsValidNetId(request.Payload.DevAddr))
                {
                    logger.LogDebug($"device is using another network id, ignoring this message (network: {this.configuration.NetId}, devAddr: {request.Payload.DevAddr.NetworkId})");
                    request.NotifyFailed(LoRaDeviceRequestFailedReason.InvalidNetId);
                    return;
                }

                dataMessageHandler.ProcessData(request);
            }
            else
            {
                logger.LogError("Unknwon message type in rxpk, message ignored");
            }
        }

        private bool IsValidNetId(DevAddr devAddr)
        {
            // Check if the current dev addr is in our network id
            var devAddrNwkid = devAddr.NetworkId;
            if (devAddrNwkid == this.configuration.NetId.NetworkId)
            {
                return true;
            }

            // If not, check if the devaddr is part of the allowed dev address list
            if (this.configuration.AllowedDevAddresses != null && this.configuration.AllowedDevAddresses.Contains(devAddr))
            {
                return true;
            }

            return false;
        }
    }
}
