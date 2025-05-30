// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using static ReceiveWindowNumber;

    /// <summary>
    /// Timer for LoRa operations.
    /// </summary>
    /// <remarks>
    /// Calculates:
    /// - first and second receive windows
    /// - first and second join receive windows.
    /// </remarks>
    public class LoRaOperationTimeWatcher
    {
        /// <summary>
        /// Gets the expected time required to package and send message back to message sender
        /// 400ms.
        /// </summary>
        public static TimeSpan ExpectedTimeToPackageAndSendMessage { get; } = TimeSpan.FromMilliseconds(400);

        /// <summary>
        /// Gets the minimum available time to check for cloud to device messages
        /// If we have less than this amount of time available no check is done
        /// 100ms.
        /// </summary>
        public static TimeSpan MinimumAvailableTimeToCheckForCloudMessage { get; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Gets the estimated overhead of calling receive message async (context switch, etc)
        /// 100ms.
        /// </summary>
        public static TimeSpan CheckForCloudMessageCallEstimatedOverhead { get; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Gets the expected time required to package and send message back to message sender plus the checking for cloud to device message overhead
        /// 400ms.
        /// </summary>
        public static TimeSpan ExpectedTimeToPackageAndSendMessageAndCheckForCloudMessageOverhead { get; } = ExpectedTimeToPackageAndSendMessage + CheckForCloudMessageCallEstimatedOverhead;

        // Gets start time
        public DateTimeOffset Start { get; }

        private readonly Region loraRegion;

        public LoRaOperationTimeWatcher(Region loraRegion)
            : this(loraRegion, DateTimeOffset.UtcNow)
        {
        }

        public LoRaOperationTimeWatcher(Region loraRegion, DateTimeOffset startTime)
        {
            Start = startTime;
            this.loraRegion = loraRegion;
        }

        /// <summary>
        /// Gets the remaining time to second receive window.
        /// </summary>
        public TimeSpan GetRemainingTimeToReceiveSecondWindow(LoRaDevice loRaDevice)
        {
            return TimeSpan.FromSeconds(GetReceiveWindow2Delay(loRaDevice)).Subtract(GetElapsedTime());
        }

        /// <summary>
        /// Gets the remaining time to first receive window.
        /// </summary>
        public TimeSpan GetRemainingTimeToReceiveFirstWindow(LoRaDevice loRaDevice)
        {
            return TimeSpan.FromSeconds(GetReceiveWindow1Delay(loRaDevice)).Subtract(GetElapsedTime());
        }

        /// <summary>
        /// Gets the receive window 1 (RX1) delay in seconds
        /// It takes into consideration region and device settings.
        /// </summary>
        /// <returns>Integer containing the delay in seconds.</returns>
        public uint GetReceiveWindow1Delay(LoRaDevice loRaDevice)
        {
            ArgumentNullException.ThrowIfNull(loRaDevice);

            return CalculateRXWindowsTime((ushort)this.loraRegion.ReceiveDelay1.ToSeconds(), loRaDevice.ReportedRXDelay);
        }

        private bool InTimeForReceiveFirstWindow(LoRaDevice loRaDevice, TimeSpan elapsed) => elapsed.Add(ExpectedTimeToPackageAndSendMessage).TotalSeconds <= GetReceiveWindow1Delay(loRaDevice);

        /// <summary>
        /// Gets the receive window 2 (RX2) delay in seconds
        /// It takes into consideration region and device settings.
        /// </summary>
        /// <returns>Integer containing the delay in seconds.</returns>
        public uint GetReceiveWindow2Delay(LoRaDevice loRaDevice)
        {
            ArgumentNullException.ThrowIfNull(loRaDevice);

            return CalculateRXWindowsTime((ushort)this.loraRegion.ReceiveDelay2.ToSeconds(), loRaDevice.ReportedRXDelay);
        }

        private bool InTimeForReceiveSecondWindow(LoRaDevice loRaDevice, TimeSpan elapsed) => elapsed.Add(ExpectedTimeToPackageAndSendMessage).TotalSeconds <= GetReceiveWindow2Delay(loRaDevice);

        /// <summary>
        /// Calculate if there is still time to send join accept response.
        /// </summary>
        public bool InTimeForJoinAccept()
        {
            return GetElapsedTime().Add(ExpectedTimeToPackageAndSendMessage) < this.loraRegion.JoinAcceptDelay2.ToTimeSpan();
        }

        /// <summary>
        /// Calculates the time remaining to response in first join accept window.
        /// </summary>
        public TimeSpan GetRemainingTimeToJoinAcceptFirstWindow()
        {
            return this.loraRegion.JoinAcceptDelay1.ToTimeSpan() - GetElapsedTime();
        }

        /// <summary>
        /// Calculates the time remaining to response in second join accept window.
        /// </summary>
        public TimeSpan GetRemainingTimeToJoinAcceptSecondWindow()
        {
            return this.loraRegion.JoinAcceptDelay2.ToTimeSpan() - GetElapsedTime();
        }

        /// <summary>
        /// Gets time passed since start.
        /// </summary>
        protected internal virtual TimeSpan GetElapsedTime() => DateTimeOffset.UtcNow - Start;

        /// <summary>
        /// Resolves the receive window to use.
        /// </summary>
        public ReceiveWindowNumber? ResolveReceiveWindowToUse(LoRaDevice loRaDevice)
        {
            ArgumentNullException.ThrowIfNull(loRaDevice);

            var elapsed = GetElapsedTime();
            if (loRaDevice.PreferredWindow is ReceiveWindow1 && InTimeForReceiveFirstWindow(loRaDevice, elapsed))
            {
                return ReceiveWindow1;
            }
            else if (InTimeForReceiveSecondWindow(loRaDevice, elapsed))
            {
                return ReceiveWindow2;
            }

            return null;
        }

        /// <summary>
        /// Gets the join accept window to be used.
        /// </summary>
        public ReceiveWindowNumber? ResolveJoinAcceptWindowToUse()
        {
            var elapsed = GetElapsedTime();
            if (InTimeForJoinAcceptFirstWindow(elapsed))
            {
                return ReceiveWindow1;
            }
            else if (InTimeForJoinAcceptSecondWindow(elapsed))
            {
                return ReceiveWindow2;
            }

            return null;
        }

        private bool InTimeForJoinAcceptFirstWindow(TimeSpan elapsed)
        {
            return elapsed.Add(ExpectedTimeToPackageAndSendMessage).TotalSeconds <= this.loraRegion.JoinAcceptDelay1.ToSeconds();
        }

        private bool InTimeForJoinAcceptSecondWindow(TimeSpan elapsed)
        {
            return elapsed.Add(ExpectedTimeToPackageAndSendMessage).TotalSeconds <= this.loraRegion.JoinAcceptDelay2.ToSeconds();
        }

        /// <summary>
        /// Gets the available time to check for cloud to device messages
        /// It takes into consideration available time and <see cref="LoRaDevice.PreferredWindow"/>.
        /// </summary>
        /// <returns><see cref="TimeSpan.Zero"/> if there is no enough time or a positive <see cref="TimeSpan"/> value.</returns>
        public TimeSpan GetAvailableTimeToCheckCloudToDeviceMessage(LoRaDevice loRaDevice)
        {
            ArgumentNullException.ThrowIfNull(loRaDevice);

            var elapsed = GetElapsedTime();
            if (loRaDevice.PreferredWindow is ReceiveWindow1)
            {
                var availableTimeForFirstWindow = TimeSpan.FromSeconds(GetReceiveWindow1Delay(loRaDevice)).Subtract(elapsed.Add(ExpectedTimeToPackageAndSendMessageAndCheckForCloudMessageOverhead));
                if (availableTimeForFirstWindow >= LoRaOperationTimeWatcher.MinimumAvailableTimeToCheckForCloudMessage)
                {
                    return availableTimeForFirstWindow;
                }
            }

            // 2nd window
            var availableTimeForSecondWindow = TimeSpan.FromSeconds(GetReceiveWindow2Delay(loRaDevice)).Subtract(elapsed.Add(ExpectedTimeToPackageAndSendMessageAndCheckForCloudMessageOverhead));
            if (availableTimeForSecondWindow >= LoRaOperationTimeWatcher.MinimumAvailableTimeToCheckForCloudMessage)
            {
                return availableTimeForSecondWindow;
            }

            return TimeSpan.Zero;
        }

        private static uint CalculateRXWindowsTime(uint windowTime, RxDelay rxDelay)
        {
            // RxDelay follows specification of RXTimingSetupReq and the delay
            // | rXDelay | Delay |
            // ===================
            // |    0    |   1   |
            // |    1    |   1   |
            // |    2    |   2   |
            // |    3    |   3   |
            return (uint)(Enum.IsDefined(rxDelay) ? windowTime + rxDelay.ToSeconds() - 1 : windowTime);
        }
    }
}
