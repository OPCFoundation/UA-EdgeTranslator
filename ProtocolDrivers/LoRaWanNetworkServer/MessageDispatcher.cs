// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using System.Diagnostics.Metrics;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Message dispatcher.
    /// </summary>
    public sealed class MessageDispatcher(
        NetworkServerConfiguration configuration,
        IJoinRequestMessageHandler joinRequestHandler,
        ILoggerFactory loggerFactory,
        ILogger<MessageDispatcher> logger,
        Meter meter) : IMessageDispatcher
    {
        private readonly NetworkServerConfiguration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        private readonly Histogram<double> d2cMessageDeliveryLatencyHistogram = meter?.CreateHistogram<double>(MetricRegistry.D2CMessageDeliveryLatency);

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

            var loggingRequest = new LoggingLoRaRequest(request, loggerFactory.CreateLogger<LoggingLoRaRequest>(), this.d2cMessageDeliveryLatencyHistogram);

            if (request.Payload.MessageType == MacMessageType.JoinRequest)
            {
                DispatchLoRaJoinRequest(loggingRequest);
            }
            else if (request.Payload.MessageType is MacMessageType.UnconfirmedDataUp or MacMessageType.ConfirmedDataUp)
            {
                DispatchLoRaDataMessage(loggingRequest);
            }
            else
            {
                logger.LogError("Unknwon message type in rxpk, message ignored");
            }
        }

        private void DispatchLoRaJoinRequest(LoggingLoRaRequest request) => joinRequestHandler.DispatchRequest(request);

        private void DispatchLoRaDataMessage(LoggingLoRaRequest request)
        {
            var loRaPayload = (LoRaPayloadData)request.Payload;
            using var scope = logger.BeginDeviceAddressScope(loRaPayload.DevAddr);
            if (!IsValidNetId(loRaPayload.DevAddr))
            {
                logger.LogDebug($"device is using another network id, ignoring this message (network: {this.configuration.NetId}, devAddr: {loRaPayload.DevAddr.NetworkId})");
                request.NotifyFailed(LoRaDeviceRequestFailedReason.InvalidNetId);
                return;
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
