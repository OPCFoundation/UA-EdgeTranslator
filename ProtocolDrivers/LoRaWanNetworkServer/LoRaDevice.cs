// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Threading;
    using System.Threading.Tasks;
    using LoRaTools;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using static ReceiveWindowNumber;

    public class LoRaDevice : IDisposable
    {
        /// <summary>
        /// Defines the maximum amount of times an ack resubmit will be sent.
        /// </summary>
        internal const int MaxConfirmationResubmitCount = 3;

        /// <summary>
        /// The default values for RX1DROffset, RX2DR.
        /// </summary>
        internal const ushort DefaultJoinValues = 0;

        private const RxDelay DefaultJoinRxDelay = RxDelay.RxDelay0;

        /// <summary>
        /// Last time this device connected to the network server
        /// </summary>
        public DateTimeOffset LastSeen { get; set; }

        /// <summary>
        /// Last time the twins were updated from IoT Hub
        /// </summary>
        public DateTimeOffset LastUpdate { get; set; }

        public DevAddr? DevAddr { get; set; }

        // Gets if a device is activated by personalization
        public bool IsABP => AppKey == null;

        public DevEui DevEUI { get; set; }

        public AppKey? AppKey { get; set; }

        public JoinEui? AppEui { get; set; }

        public NetworkSessionKey? NwkSKey { get; set; }

        public AppSessionKey? AppSKey { get; set; }

        public AppNonce AppNonce { get; set; }

        public DevNonce? DevNonce { get; set; }

        public NetId? NetId { get; set; }

        public bool IsOurDevice { get; set; }

        public string LastConfirmedC2DMessageID { get; set; }

        public uint FCntUp => this.fcntUp;

        /// <summary>
        /// Gets the last saved value for <see cref="FCntUp"/>.
        /// </summary>
        public uint LastSavedFCntUp => this.lastSavedFcntUp;

        public uint FCntDown => this.fcntDown;

        /// <summary>
        /// Gets the last saved value for <see cref="FCntDown"/>.
        /// </summary>
        public uint LastSavedFCntDown => this.lastSavedFcntDown;

        public string GatewayID { get; set; }

        public string SensorDecoder { get; set; }

        public bool IsABPRelaxedFrameCounter { get; set; }

        public bool Supports32BitFCnt { get; set; }

        public bool? IsConnectionOwner { get; set; }

        private readonly ChangeTrackingProperty<DataRateIndex> dataRate = new(TwinProperty.DataRate);

        public DataRateIndex DataRate => this.dataRate.Get();

        private readonly ChangeTrackingProperty<int> txPower = new ChangeTrackingProperty<int>(TwinProperty.TxPower);
        private readonly ILogger<LoRaDevice> logger;
        private readonly Counter<int> unhandledExceptionCount;
        private readonly ChangeTrackingProperty<int> nbRep = new ChangeTrackingProperty<int>(TwinProperty.NbRep);

        public DeduplicationMode Deduplication { get; set; }

        private ReceiveWindowNumber preferredWindow;

        /// <summary>
        /// Gets or sets value indicating the preferred receive window for the device.
        /// </summary>
        public ReceiveWindowNumber PreferredWindow
        {
            get => this.preferredWindow;
            set => this.preferredWindow = Enum.IsDefined(value)
                                        ? value
                                        : throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(PreferredWindow)} must be 1 or 2.");
        }

        /// <summary>
        /// Gets or sets the <see cref="LoRaDeviceClassType"/>.
        /// </summary>
        public LoRaDeviceClassType ClassType { get; set; }

        private readonly ChangeTrackingProperty<LoRaRegionType> region = new ChangeTrackingProperty<LoRaRegionType>(TwinProperty.Region, LoRaRegionType.NotSet);

        /// <summary>
        /// Gets or sets the <see cref="LoRaRegionType"/> of the device
        /// Relevant only for <see cref="LoRaDeviceClassType.C"/>.
        /// </summary>
        public LoRaRegionType LoRaRegion
        {
            get => this.region.Get();
            set => this.region.Set(value);
        }

        /// <summary>
        /// Gets or sets the join channel for the device based on reported properties.
        /// Relevant only for region CN470.
        /// </summary>
        public int? ReportedCN470JoinChannel { get; set; }

        /// <summary>
        /// Gets or sets the join channel for the device based on desired properties.
        /// Relevant only for region CN470.
        /// </summary>
        public int? DesiredCN470JoinChannel { get; set; }

        private readonly ChangeTrackingProperty<string> preferredGatewayID = new ChangeTrackingProperty<string>(TwinProperty.PreferredGatewayID, string.Empty);

        /// <summary>
        /// Gets the device preferred gateway identifier
        /// Relevant only for <see cref="LoRaDeviceClassType.C"/>.
        /// </summary>
        public string PreferredGatewayID => this.preferredGatewayID.Get();

        /// <summary>
        /// Used to synchronize the async save operation to the twins for this particular device.
        /// </summary>
#pragma warning disable CA2213 // Disposable fields should be disposed (disposed in async)
        private readonly SemaphoreSlim syncSave = new SemaphoreSlim(1, 1);
#pragma warning restore CA2213 // Disposable fields should be disposed
        private readonly Lock processingSyncLock = new Lock();
        private readonly Queue<LoRaRequest> queuedRequests = new Queue<LoRaRequest>();

        public DataRateIndex? DesiredRX2DataRate { get; set; }

        public ushort DesiredRX1DROffset { get; set; }

        public DataRateIndex? ReportedRX2DataRate { get; set; }

        public ushort ReportedRX1DROffset { get; set; }

        private readonly ChangeTrackingProperty<DwellTimeSetting> reportedDwellTimeSetting = new ChangeTrackingProperty<DwellTimeSetting>(TwinProperty.TxParam, null);
        public DwellTimeSetting ReportedDwellTimeSetting => this.reportedDwellTimeSetting.Get();

        private volatile bool hasFrameCountChanges;

        private byte confirmationResubmitCount;
        private volatile uint fcntUp;
        private volatile uint fcntDown;
        private volatile uint lastSavedFcntUp;
        private volatile uint lastSavedFcntDown;
 
        public RxDelay ReportedRXDelay { get; set; }

        public RxDelay DesiredRXDelay { get; set; }

        /// <summary>
        ///  Gets or sets a value indicating whether cloud to device messages are enabled for the device
        ///  By default it is enabled. To disable, set the desired property "EnableC2D" to false.
        /// </summary>
        public bool DownlinkEnabled { get; set; }

        /// <summary>
        /// Gets or sets the StationEui for the Basic Station that last processed a message coming from this device.
        /// </summary>
        private readonly ChangeTrackingProperty<StationEui> lastProcessingStationEui = new ChangeTrackingProperty<StationEui>(TwinProperty.LastProcessingStationEui, default);

        public StationEui LastProcessingStationEui => this.lastProcessingStationEui.Get();

        public LoRaDevice(DevAddr? devAddr, DevEui devEui, ILogger<LoRaDevice> logger, Meter meter)
        {
            this.queuedRequests = new Queue<LoRaRequest>();
            this.logger = logger;
            DevAddr = devAddr;
            DevEUI = devEui;
            DownlinkEnabled = true;
            IsABPRelaxedFrameCounter = true;
            PreferredWindow = ReceiveWindow1;
            ClassType = LoRaDeviceClassType.A;
            this.unhandledExceptionCount = meter?.CreateCounter<int>(MetricRegistry.UnhandledExceptions);
        }

        /// <summary>
        /// Use constructor for test code only.
        /// </summary>
        internal LoRaDevice(DevAddr? devAddr, DevEui devEui)
            : this(devAddr, devEui, NullLogger<LoRaDevice>.Instance, null)
        { }

        public void SetLastProcessingStationEui(StationEui s) => this.lastProcessingStationEui.Set(s);

        /// <summary>
        /// Saves device changes in reported twin properties
        /// It will only save if required. Frame counters are only saved if the difference since last value is equal or greater than <see cref="Constants.MaxFcntUnsavedDelta"/>.
        /// </summary>
        /// <param name="reportedProperties">Pre populate reported properties.</param>
        /// <param name="force">Indicates if changes should be saved even if the difference between last saved and current frame counter are less than <see cref="Constants.MaxFcntUnsavedDelta"/>.</param>
        public async Task<bool> SaveChangesAsync(bool force = false)
        {
            try
            {
                // We only ever want a single save operation per device
                // to happen. The save to the twins can be delayed for multiple
                // seconds, subsequent updates should be waiting for this to complete
                // before checking the current state and update again.
                await this.syncSave.WaitAsync().ConfigureAwait(false);

                var savedProperties = new List<IChangeTrackingProperty>();
                foreach (var prop in GetTrackableProperties())
                {
                    if (prop.IsDirty())
                    {
                        savedProperties.Add(prop);
                    }
                }

                var fcntUpDelta = FCntUp >= LastSavedFCntUp ? FCntUp - LastSavedFCntUp : LastSavedFCntUp - FCntUp;
                var fcntDownDelta = FCntDown >= LastSavedFCntDown ? FCntDown - LastSavedFCntDown : LastSavedFCntDown - FCntDown;

                if (fcntDownDelta >= Constants.MaxFcntUnsavedDelta ||
                    fcntUpDelta >= Constants.MaxFcntUnsavedDelta ||
                    (this.hasFrameCountChanges && force))
                {
                    var savedFcntDown = FCntDown;
                    var savedFcntUp = FCntUp;

                    InternalAcceptFrameCountChanges(savedFcntUp, savedFcntDown);

                    for (var i = 0; i < savedProperties.Count; i++)
                        savedProperties[i].AcceptChanges();
                }

                return true;
            }
            finally
            {
                _ = this.syncSave.Release();
            }
        }

        /// <summary>
        /// Accept changes to the frame count
        /// This method is not protected by locks.
        /// </summary>
        private void InternalAcceptFrameCountChanges(uint savedFcntUp, uint savedFcntDown)
        {
            this.lastSavedFcntUp = savedFcntUp;
            this.lastSavedFcntDown = savedFcntDown;

            this.hasFrameCountChanges = this.fcntDown != this.lastSavedFcntDown || this.fcntUp != this.lastSavedFcntUp;
        }

        /// <summary>
        /// Increments <see cref="FCntDown"/>.
        /// </summary>
        public uint IncrementFcntDown(uint value)
        {
            this.syncSave.Wait();
            try
            {
                this.fcntDown += value;
                this.hasFrameCountChanges = true;
                return this.fcntDown;
            }
            finally
            {
                _ = this.syncSave.Release();
            }
        }

        public void SetFcntUp(uint newValue)
        {
            this.syncSave.Wait();
            try
            {
                if (this.fcntUp != newValue)
                {
                    this.fcntUp = newValue;
                    this.confirmationResubmitCount = 0;
                    this.hasFrameCountChanges = true;
                }
            }
            finally
            {
                _ = this.syncSave.Release();
            }
        }

        /// <summary>
        /// Optimized way to reset fcntUp and fcntDown to zero with a single lock.
        /// </summary>
        internal void ResetFcnt()
        {
            this.syncSave.Wait();
            try
            {
                if (this.hasFrameCountChanges)
                {
                    // if there are changes, reset them if the last saved was 0, 0
                    this.hasFrameCountChanges = this.lastSavedFcntDown != 0 || this.lastSavedFcntUp != 0;
                }
                else
                {
                    // if there aren't changes, reset if fcnt was not 0, 0
                    this.hasFrameCountChanges = this.fcntDown != 0 || this.fcntUp != 0;
                }

                this.fcntDown = 0;
                this.fcntUp = 0;
                this.confirmationResubmitCount = 0;
            }
            finally
            {
                _ = this.syncSave.Release();
            }
        }

        /// <summary>
        /// Indicates whether or not we can resubmit an ack for the confirmation up message.
        /// </summary>
        /// <returns><c>true</c>, if resubmit is allowed, <c>false</c> otherwise.</returns>
        /// <param name="payloadFcnt">Payload frame count.</param>
        public bool ValidateConfirmResubmit(uint payloadFcnt)
        {
            this.syncSave.Wait();
            try
            {
                if (FCntUp == payloadFcnt)
                {
                    if (this.confirmationResubmitCount < MaxConfirmationResubmitCount)
                    {
                        this.confirmationResubmitCount++;
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                _ = this.syncSave.Release();
            }
        }

        /// <summary>
        /// Updates device on the server after a join succeeded.
        /// </summary>
        internal virtual Task<bool> UpdateAfterJoinAsync(LoRaDeviceJoinUpdateProperties updateProperties, CancellationToken cancellationToken)
        {
            var devAddrBeforeSave = DevAddr;

            _ = RegionManager.TryTranslateToRegion(updateProperties.Region, out var currentRegion);

            // Only save if the devAddr remains the same, otherwise ignore the save
            if (devAddrBeforeSave == DevAddr)
            {
                DevAddr = updateProperties.DevAddr;
                NwkSKey = updateProperties.NwkSKey;
                AppSKey = updateProperties.AppSKey;
                AppNonce = updateProperties.AppNonce;
                DevNonce = updateProperties.DevNonce;
                NetId = updateProperties.NetId;
                ReportedCN470JoinChannel = updateProperties.CN470JoinChannel;

                if (currentRegion.IsValidRX1DROffset(DesiredRX1DROffset))
                {
                    ReportedRX1DROffset = DesiredRX1DROffset;
                }
                else
                {
                    this.logger.LogError("the provided RX1DROffset is not valid");
                }

                if (DesiredRX2DataRate.HasValue && currentRegion.RegionLimits.IsCurrentDownstreamDRIndexWithinAcceptableValue(DesiredRX2DataRate.Value))
                {
                    ReportedRX2DataRate = DesiredRX2DataRate;
                }
                else
                {
                    this.logger.LogError("the provided RX2DataRate is not valid");
                }

                if (Enum.IsDefined(DesiredRXDelay))
                {
                    ReportedRXDelay = DesiredRXDelay;
                }
                else
                {
                    this.logger.LogError("the provided RXDelay is not valid");
                }

                this.region.AcceptChanges();
                this.preferredGatewayID.AcceptChanges();
                this.lastProcessingStationEui.AcceptChanges();

                ResetFcnt();
                InternalAcceptFrameCountChanges(this.fcntUp, this.fcntDown);
            }
            else
            {
                this.region.Rollback();
                this.preferredGatewayID.Rollback();
            }

            return Task.FromResult(true);
        }

     
        public void Dispose()
        {
            this.syncSave.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Updates the ADR properties of device.
        /// </summary>
        public void UpdatedADRProperties(DataRateIndex dataRate, int txPower, int nbRep)
        {
            this.dataRate.Set(dataRate);
            this.txPower.Set(txPower);
            this.nbRep.Set(nbRep);
        }

        /// <summary>
        /// Gets the properties that are trackable.
        /// </summary>
        private IEnumerable<IChangeTrackingProperty> GetTrackableProperties()
        {
            yield return this.preferredGatewayID;
            yield return this.region;
            yield return this.dataRate;
            yield return this.txPower;
            yield return this.nbRep;
            yield return this.lastProcessingStationEui;
            yield return this.reportedDwellTimeSetting;
        }

        internal void UpdatePreferredGatewayID(string value, bool acceptChanges) =>
            UpdateChangeTrackingProperty(value, acceptChanges, this.preferredGatewayID);

        internal void UpdateRegion(LoRaRegionType value, bool acceptChanges) =>
            UpdateChangeTrackingProperty(value, acceptChanges, this.region);

        internal void UpdateDwellTimeSetting(DwellTimeSetting dwellTimeSetting, bool acceptChanges) =>
            UpdateChangeTrackingProperty(dwellTimeSetting, acceptChanges, this.reportedDwellTimeSetting);

        private static void UpdateChangeTrackingProperty<T>(T value, bool acceptChanges, ChangeTrackingProperty<T> changeTrackingProperty)
        {
            changeTrackingProperty.Set(value);
            if (acceptChanges)
                changeTrackingProperty.AcceptChanges();
        }
    }
}
