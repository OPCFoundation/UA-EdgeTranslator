// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
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
                if (request.Payload.DevAddr.NetworkId != 1)
                {
                    logger.LogDebug($"device is using another network id, ignoring this message (network: {request.Payload.DevAddr.NetworkId}, devAddr: {request.Payload.DevAddr.NetworkId})");
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
    }
}
