// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;
    using System.Diagnostics.Metrics;
    using global::LoRaWan;
    using global::LoRaWan.NetworkServer;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using Microsoft.Extensions.Logging;
    using static LoRaWANContainer.LoRaWan.NetworkServer.Models.LnsData;

    /// <summary>
    /// Composition of a <see cref="LoRaRequest"/> that logs at the end of the process.
    /// </summary>
    public class LoggingLoRaRequest : LoRaRequest
    {
        private readonly LoRaRequest wrappedRequest;
        private readonly ILogger<LoggingLoRaRequest> logger;
        private readonly Histogram<double> d2cMessageDeliveryLatencyHistogram;

        public override IDownstreamMessageSender DownstreamMessageSender => this.wrappedRequest.DownstreamMessageSender;

        public override Region Region => this.wrappedRequest.Region;

        public override LoRaPayload Payload => this.wrappedRequest.Payload;

        public override RadioMetadata RadioMetadata => this.wrappedRequest.RadioMetadata;

        public override DateTime StartTime => this.wrappedRequest.StartTime;

        public override StationEui StationEui => this.wrappedRequest.StationEui;

        public LoggingLoRaRequest(LoRaRequest wrappedRequest, ILogger<LoggingLoRaRequest> logger, Histogram<double> d2cMessageDeliveryLatencyHistogram)
        {
            this.wrappedRequest = wrappedRequest;
            this.logger = logger;
            this.d2cMessageDeliveryLatencyHistogram = d2cMessageDeliveryLatencyHistogram;
        }

        public override void NotifyFailed(string deviceId, LoRaDeviceRequestFailedReason reason, Exception exception = null)
        {
            this.wrappedRequest.NotifyFailed(deviceId, reason, exception);
            TrackProcessingTime();
        }

        public override void NotifySucceeded(LoRaDevice loRaDevice, DownlinkMessage downlink)
        {
            this.wrappedRequest.NotifySucceeded(loRaDevice, downlink);
            TrackProcessingTime();
        }

        private void TrackProcessingTime()
        {
            var elapsedTime = DateTime.UtcNow.Subtract(this.wrappedRequest.StartTime);
            this.d2cMessageDeliveryLatencyHistogram?.Record(elapsedTime.TotalMilliseconds);

            if (!this.logger.IsEnabled(LogLevel.Debug))
                return;

            this.logger.LogDebug($"processing time: {elapsedTime}");
        }

        public override LoRaOperationTimeWatcher GetTimeWatcher() => this.wrappedRequest.GetTimeWatcher();
    }
}
