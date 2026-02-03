namespace OCPPCentralSystem.Models
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using System.Runtime.Serialization;

    [JsonConverter(typeof(StringEnumConverter))]
    public enum AuthorizationStatus
    {
        Accepted,
        Blocked,
        Expired,
        Invalid,
        ConcurrentTx
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ReadingContext
    {
        [EnumMember(Value = @"Interruption.Begin")]
        InterruptionBegin,

        [EnumMember(Value = @"Interruption.End")]
        InterruptionEnd,

        Other,

        [EnumMember(Value = @"Sample.Clock")]
        SampleClock,

        [EnumMember(Value = @"Sample.Periodic")]
        SamplePeriodic,

        [EnumMember(Value = @"Transaction.Begin")]
        TransactionBegin,

        [EnumMember(Value = @"Transaction.End")]
        TransactionEnd,

        Trigger
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ValueFormat
    {
        Raw,
        SignedData
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum SampledValueMeasurand
    {
        [EnumMember(Value = @"Current.Export")]
        CurrentExport,

        [EnumMember(Value = @"Current.Import")]
        CurrentImport,

        [EnumMember(Value = @"Current.Offered")]
        CurrentOffered,

        [EnumMember(Value = @"Energy.Active.Export.Register")]
        EnergyActiveExportRegister,

        [EnumMember(Value = @"Energy.Active.Import.Register")]
        EnergyActiveImportRegister,

        [EnumMember(Value = @"Energy.Reactive.Export.Register")]
        EnergyReactiveExportRegister,

        [EnumMember(Value = @"Energy.Reactive.Import.Register")]
        EnergyReactiveImportRegister,

        [EnumMember(Value = @"Energy.Active.Export.Interval")]
        EnergyActiveExportInterval,

        [EnumMember(Value = @"Energy.Active.Import.Interval")]
        EnergyActiveImportInterval,

        [EnumMember(Value = @"Energy.Reactive.Export.Interval")]
        EnergyReactiveExportInterval,

        [EnumMember(Value = @"Energy.Reactive.Import.Interval")]
        EnergyReactiveImportInterval,

        Frequency,

        [EnumMember(Value = @"Power.Active.Export")]
        PowerActiveExport,

        [EnumMember(Value = @"Power.Active.Import")]
        PowerActiveImport,

        [EnumMember(Value = @"Power.Factor")]
        PowerFactor,

        [EnumMember(Value = @"Power.Offered")]
        PowerOffered,

        [EnumMember(Value = @"Power.Reactive.Export")]
        PowerReactiveExport,

        [EnumMember(Value = @"Power.Reactive.Import")]
        PowerReactiveImport,

        RPM,

        SoC,

        Temperature,

        Voltage
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum Measurand
    {
        Missing,

        [EnumMember(Value = @"Current.Export")]
        CurrentExport,

        [EnumMember(Value = @"Current.Import")]
        CurrentImport,

        [EnumMember(Value = @"Current.Offered")]
        CurrentOffered,

        [EnumMember(Value = @"Energy.Active.Export.Register")]
        EnergyActiveExportRegister,

        [EnumMember(Value = @"Energy.Active.Import.Register")]
        EnergyActiveImportRegister,

        [EnumMember(Value = @"Energy.Reactive.Export.Register")]
        EnergyReactiveExportRegister,

        [EnumMember(Value = @"Energy.Reactive.Import.Register")]
        EnergyReactiveImportRegister,

        [EnumMember(Value = @"Energy.Active.Export.Interval")]
        EnergyActiveExportInterval,

        [EnumMember(Value = @"Energy.Active.Import.Interval")]
        EnergyActiveImportInterval,

        [EnumMember(Value = @"Energy.Active.Net")]
        EnergyActiveNet,

        [EnumMember(Value = @"Energy.Reactive.Export.Interval")]
        Energy_Reactive_Export_Interval,

        [EnumMember(Value = @"Energy.Reactive.Import.Interval")]
        EnergyReactiveImportInterval,

        [EnumMember(Value = @"Energy.Reactive.Net")]
        EnergyReactiveNet,

        [EnumMember(Value = @"Energy.Apparent.Net")]
        EnergyApparentNet,

        [EnumMember(Value = @"Energy.Apparent.Import")]
        EnergyApparentImport,

        [EnumMember(Value = @"Energy.Apparent.Export")]
        EnergyApparentExport,

        Frequency,

        [EnumMember(Value = @"Power.Active.Export")]
        PowerActiveExport,

        [EnumMember(Value = @"Power.Active.Import")]
        PowerActiveImport,

        [EnumMember(Value = @"Power.Factor")]
        PowerFactor,

        [EnumMember(Value = @"Power.Offered")]
        PowerOffered,

        [EnumMember(Value = @"Power.Reactive.Export")]
        PowerReactiveExport,

        [EnumMember(Value = @"Power.Reactive.Import")]
        PowerReactiveImport,

        SoC,

        Voltage
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum Phase
    {
        L1,
        L2,
        L3,
        N,

        [EnumMember(Value = @"L1-N")]
        L1N,

        [EnumMember(Value = @"L2-N")]
        L2N,

        [EnumMember(Value = @"L3-N")]
        L3N,

        [EnumMember(Value = @"L1-L2")]
        L1L2,

        [EnumMember(Value = @"L2-L3")]
        L2L3,

        [EnumMember(Value = @"L3-L1")]
        L3L1
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum Location
    {
        Body,
        Cable,
        EV,
        Inlet,
        Outlet
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum UnitOfMeasure
    {
        Celsius,
        Fahrenheit,
        Wh,
        kWh,
        varh,
        kvarh,
        W,
        kW,
        VA,
        kVA,
        var,
        kvar,
        A,
        V,
        K,
        Percent
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum RegistrationStatus
    {
        Accepted,
        Pending,
        Rejected
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum DataTransferStatus
    {
        Accepted,
        Rejected,
        UnknownMessageId,
        UnknownVendorId
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum DiagnosticsStatus
    {
        Idle,
        Uploaded,
        UploadFailed,
        Uploading
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum FirmwareStatus
    {
        Downloaded,
        DownloadFailed,
        Downloading,
        Idle,
        InstallationFailed,
        Installed,
        Installing
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ChargePointStatus
    {
        Available,
        Preparing,
        Charging,
        SuspendedEV,
        SuspendedEVSE,
        Finishing,
        Reserved,
        Faulted,
        Unavailable
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ChargePointErrorCode
    {
        ConnectorLockFailure,
        EVCommunicationError,
        GroundFailure,
        HighTemperature,
        InternalError,
        LocalListConflict,
        NoError,
        OtherError,
        OverCurrentFailure,
        OverVoltage,
        PowerMeterFailure,
        PowerSwitchFailure,
        ReaderFailure,
        ResetFailure,
        UnderVoltage,
        WeakSignal
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum StopReason
    {
        EmergencyStop,
        EVDisconnected,
        HardReset,
        Local,
        Other,
        PowerLoss,
        Reboot,
        Remote,
        SoftReset,
        UnlockCommand,
        DeAuthorized
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum HashAlgorithm
    {
        SHA256,
        SHA384,
        SHA512
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum AuthorizationStatusV21
    {
        Accepted,
        Blocked,
        ConcurrentTx,
        Expired,
        Invalid,
        NoCredit,
        NotAllowedTypeEVSE,
        NotAtThisLocation,
        NotAtThisTime,
        Unknown
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum AuthorizeCertificateStatus
    {
        Accepted,
        SignatureError,
        CertificateExpired,
        CertificateRevoked,
        NoCertificateAvailable,
        CertChainError,
        ContractCancelled
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MessageFormat
    {
        ASCII,
        HTML,
        URI,
        UTF8
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum IdToken
    {
        Central,
        eMAID,
        ISO14443,
        ISO15693,
        KeyCode,
        Local,
        MacAddress,
        NoAuthorization,
        Other
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum BootReason
    {
        ApplicationReset,
        FirmwareUpdate,
        LocalReset,
        PowerUp,
        RemoteReset,
        ScheduledReset,
        Triggered,
        Unknown,
        Watchdog
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum EventTrigger
    {
        Alerting,
        Delta,
        Periodic
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ChargingState
    {
        Charging,
        EVConnected,
        SuspendedEV,
        SuspendedEVSE,
        Idle
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum StoppedReason
    {
        Missing,
        DeAuthorized,
        EmergencyStop,
        EnergyLimitReached,
        EVDisconnected,
        GroundFault,
        ImmediateReset,
        Local,
        LocalOutOfCredit,
        MasterPass,
        Other,
        OvercurrentFault,
        PowerLoss,
        PowerQuality,
        Reboot,
        Remote,
        SOCLimitReached,
        StoppedByEV,
        TimeLimitReached,
        Timeout
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum TransactionEvent
    {
        Ended,
        Started,
        Updated
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum TriggerReason
    {
        Authorized,
        CablePluggedIn,
        ChargingRateChanged,
        ChargingStateChanged,
        Deauthorized,
        EnergyLimitReached,
        EVCommunicationLost,
        EVConnectTimeout,
        MeterValueClock,
        MeterValuePeriodic,
        TimeLimitReached,
        Trigger,
        UnlockCommand,
        StopAuthorized,
        EVDeparted,
        EVDetected,
        RemoteStop,
        RemoteStart,
        AbnormalCondition,
        SignedDataReceived,
        ResetCommand
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ConnectorStatus
    {
        Available,
        Occupied,
        Reserved,
        Unavailable,
        Faulted
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum Availability
    {
        Operative,
        Inoperative
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum AvailabilityStatus
    {
        Accepted,
        Rejected,
        Scheduled
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ResetType
    {
        Hard,
        Soft
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ChargingProfilePurpose
    {
        ChargePointMaxProfile,
        TxDefaultProfile,
        TxProfile
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ChargingProfileKind
    {
        Absolute,
        Recurring,
        Relative
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum RecurrencyKind
    {
        Daily,
        Weekly
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ChargingRateUnit
    {
        W,
        A
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MonitorType
    {
        UpperThreshold,
        LowerThreshold,
        Delta,
        Periodic,
        PeriodicClockAligned
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum AttributeType
    {
        Actual,
        Target,
        MinSet,
        MaxSet
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum Mutability
    {
        ReadOnly,
        WriteOnly,
        ReadWrite
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum DataEnum
    {
        [EnumMember(Value = @"string")]
        String,

        [EnumMember(Value = @"decimal")]
        Decimal,

        integer,
        dateTime,
        boolean,
        OptionList,
        SequenceList,
        MemberList
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum EnergyTransferMode
    {
        AC_single_phase,
        AC_two_phase,
        AC_three_phase,
        DC,
        AC_BPT,
        AC_BPT_DER,
        AC_BER,
        DC_BPT,
        DC_ACDP,
        DC_ACDP_BPT,
        WPT
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MobilityNeedsMode
    {
        EVCC,
        EVCC_SECC
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ControlMode
    {
        ScheduledControl,
        DynamicControl
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum NotifyEVChargingNeedsStatus
    {
        Accepted,
        Rejected,
        Processing
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum DERControl
    {
        EnterService,
        FreqDrop,
        FreqWatt,
        FixedPFAbsorb,
        FixedPFInject,
        FixedVar,
        Gradients,
        HFMustTrip,
        HFMayTrip,
        HVMustTrip,
        HVMomCess,
        HVMayTrip,
        LimitMaxDischarge,
        LFMustTrip,
        LVMustTrip,
        LVMomCess,
        LVMayTrip,
        PowerMonitoringMustTrip,
        VoltVar,
        VoltWatt,
        WattPF,
        WattVar
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum IslandingDetection
    {
        NoAntiIslandingSupport,
        RoCoF,
        UVP_OVP,
        UFP_OFP,
        VoltageVectorShift,
        ZeroCrossingDetection,
        OtherPassive,
        ImpedanceMeasurement,
        ImpedanceAtFrequency,
        SlipModeFrequencyShift,
        SandiaFrequencyShift,
        SandiaVoltageShift,
        FrequencyJump,
        RCLQFactor,
        OtherActive
    }
}
