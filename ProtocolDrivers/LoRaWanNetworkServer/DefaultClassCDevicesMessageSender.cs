// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using System.Diagnostics.Metrics;
    using System.Threading;
    using System.Threading.Tasks;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;

    public class DefaultClassCDevicesMessageSender(
        NetworkServerConfiguration configuration,
        IDownstreamMessageSender downstreamMessageSender,
        ILoRaDeviceFrameCounterUpdateStrategyProvider frameCounterUpdateStrategyProvider,
        ILogger<DefaultClassCDevicesMessageSender> logger,
        Meter meter) : IClassCDeviceMessageSender
    {
        private readonly Counter<int> c2dMessageTooLong = meter?.CreateCounter<int>(MetricRegistry.C2DMessageTooLong);

        public async Task<bool> SendAsync(IReceivedLoRaCloudToDeviceMessage message, CancellationToken cts = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            var devEui = message.DevEUI.GetValueOrDefault();
            if (!devEui.IsValid)
            {
                logger.LogError($"[class-c] devEUI missing/invalid in payload");
                return false;
            }

            if (!message.IsValid(out var validationErrorMessage))
            {
                logger.LogError($"[class-c] {validationErrorMessage}");
                return false;
            }

            var searchResult = SearchDevicesResult.SearchForDevice(null, devEui);
            if (searchResult == null || searchResult.Devices.Count == 0)
            {
                logger.LogError($"[class-c] device {message.DevEUI} not found or not joined");
                return false;
            }

            var loRaDevice = new LoRaDevice(null, searchResult.Devices[0].DevEUI);

            if (!RegionManager.TryTranslateToRegion(loRaDevice.LoRaRegion, out var region))
            {
                logger.LogError("[class-c] device does not have a region assigned. Ensure the device has connected at least once with the network");
                return false;
            }

            if (cts.IsCancellationRequested)
            {
                logger.LogError($"[class-c] device {message.DevEUI} timed out, stopping");
                return false;
            }

            if (loRaDevice.DevAddr is null)
            {
                logger.LogError("[class-c] devAddr is empty, cannot send cloud to device message. Ensure the device has connected at least once with the network");
                return false;
            }

            if (loRaDevice.ClassType != LoRaDeviceClassType.C)
            {
                logger.LogError($"[class-c] sending cloud to device messages expects a class C device. Class type is {loRaDevice.ClassType}");
                return false;
            }

            if (loRaDevice.LastProcessingStationEui == default)
            {
                logger.LogError("[class-c] sending cloud to device messages expects a class C device already connected to one station and reported its StationEui. No StationEui was saved for this device.");
                return false;
            }

            var frameCounterStrategy = frameCounterUpdateStrategyProvider.GetStrategy(loRaDevice.GatewayID);
            if (frameCounterStrategy == null)
            {
                logger.LogError($"[class-c] could not resolve frame count update strategy for device, gateway id: {loRaDevice.GatewayID}");
                return false;
            }

            var fcntDown = await frameCounterStrategy.NextFcntDown(loRaDevice, loRaDevice.FCntUp).ConfigureAwait(false);
            if (fcntDown <= 0)
            {
                logger.LogError("[class-c] could not obtain fcnt down for class C device");
                return false;
            }

            logger.LogDebug($"[class-c] down frame counter: {loRaDevice.FCntDown}");

            var downlinkMessageBuilderResp = DownlinkMessageBuilder.CreateDownlinkMessage(
                configuration,
                loRaDevice, // TODO resolve region from device information
                region,
                message,
                fcntDown,
                logger);

            var messageIdLog = message.MessageId ?? "undefined";

            if (downlinkMessageBuilderResp.IsMessageTooLong)
            {
                this.c2dMessageTooLong?.Add(1);
                logger.LogError($"[class-c] cloud to device message too large, rejecting. Id: {messageIdLog}");
                if (!await message.RejectAsync().ConfigureAwait(false))
                {
                    logger.LogError($"[class-c] failed to reject. Id: {messageIdLog}");
                }
                return false;
            }
            else
            {
                try
                {
                    await downstreamMessageSender.SendDownstreamAsync(downlinkMessageBuilderResp.DownlinkMessage).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError($"[class-c] failed to send the message, abandoning. Id: {messageIdLog}, ex: {ex.Message}");
                    if (!await message.AbandonAsync().ConfigureAwait(false))
                    {
                        logger.LogError($"[class-c] failed to abandon the message. Id: {messageIdLog}");
                    }
                    throw;
                }

                if (!await message.CompleteAsync().ConfigureAwait(false))
                {
                    logger.LogError($"[class-c] failed to complete the message. Id: {messageIdLog}");
                }

                if (!await frameCounterStrategy.SaveChangesAsync(loRaDevice).ConfigureAwait(false))
                {
                    logger.LogWarning("[class-c] failed to update framecounter.");
                }
            }

            return true;
        }
    }
}
