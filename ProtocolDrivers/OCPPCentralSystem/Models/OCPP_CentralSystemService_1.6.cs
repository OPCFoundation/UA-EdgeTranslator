
namespace OCPPCentralSystem.Models
{
    using Newtonsoft.Json;
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public class IdTagInfo
    {
        [DataMember(Name = "status")]
        public AuthorizationStatus Status { get; set; }
        
        [DataMember(Name = "expiryDate")]
        public DateTime ExpiryDate { get; set; }
        
        [DataMember(Name = "expiryDateSpecified")]
        public bool ExpiryDateSpecified { get; set; }
        
        [DataMember(Name = "parentIdTag")]
        public string ParentIdTag { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum AuthorizationStatus
    {
        Accepted,
        Blocked,
        Expired,
        Invalid,
        ConcurrentTx
    }

    [DataContract]
    public class SampledValue
    {
        [DataMember(Name = "value")]
        public string Value { get; set; }

        [DataMember(Name = "context")]
        public ReadingContext Context { get; set; }

        [DataMember(Name = "contextSpecified")]
        public bool ContextSpecified { get; set; }

        [DataMember(Name = "format")]
        public ValueFormat Format { get; set; }

        [DataMember(Name = "formatSpecified")]
        public bool FormatSpecified { get; set; }

        [DataMember(Name = "measurand")]
        public Measurand Measurand { get; set; }

        [DataMember(Name = "measurandSpecified")]
        public bool MeasurandSpecified { get; set; }

        [DataMember(Name = "phase")]
        public Phase Phase { get; set; }

        [DataMember(Name = "phaseSpecified")]
        public bool PhaseSpecified { get; set; }

        [DataMember(Name = "location")]
        public Location Location { get; set; }

        [DataMember(Name = "locationSpecified")]
        public bool LocationSpecified { get; set; }

        [DataMember(Name = "unit")]
        public UnitOfMeasure Unit { get; set; }

        [DataMember(Name = "unitSpecified")]
        public bool UnitSpecified { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
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

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum ValueFormat
    {
        Raw,
        SignedData
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum Measurand
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

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum Phase
    {
        L1,
        L2,
        L3,
        N,
        L1N,
        L2N,
        L3N,
        L1L2,
        L2L3,
        L3L1
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum Location
    {
        Body,
        Cable,
        EV,
        Inlet,
        Outlet
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
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

    [DataContract]
    public class MeterValue
    {
        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }

        [DataMember(Name = "sampledValue")]
        public SampledValue[] SampledValue { get; set; }
    }

    [DataContract]
    public class AuthorizeRequest
    {
        [DataMember(Name = "idTag")]
        public string IdTag { get; set; }
    }

    [DataContract]
    public class AuthorizeResponse
    {
        [DataMember(Name = "idTagInfo")]
        public IdTagInfo IdTagInfo { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum RegistrationStatus
    {
        Accepted,
        Pending,
        Rejected
    }

    [DataContract]
    public class BootNotificationRequest
    {
        [DataMember(Name = "chargePointVendor")]
        public string ChargePointVendor { get; set; }

        [DataMember(Name = "chargePointModel")]
        public string ChargePointModel { get; set; }

        [DataMember(Name = "chargePointSerialNumber")]
        public string ChargePointSerialNumber { get; set; }

        [DataMember(Name = "chargeBoxSerialNumber")]
        public string ChargeBoxSerialNumber { get; set; }
                
        [DataMember(Name = "firmwareVersion")]
        public string FirmwareVersion { get; set; }

        [DataMember(Name = "iccid")]
        public string Iccid { get; set; }

        [DataMember(Name = "imsi")]
        public string Imsi { get; set; }

        [DataMember(Name = "meterType")]
        public string MeterType { get; set; }

        [DataMember(Name = "meterSerialNumber")]
        public string MeterSerialNumber { get; set; }
    }

    [DataContract]
    public class BootNotificationResponse
    {
        [DataMember(Name = "status")]
        public RegistrationStatus Status { get; set; }

        [DataMember(Name = "currentTime")]
        public DateTime CurrentTime { get; set; }

        [DataMember(Name = "interval")]
        public int Interval { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum DataTransferStatus
    {
        Accepted,
        Rejected,
        UnknownMessageId,
        UnknownVendorId
    }

    [DataContract]
    public class DataTransferRequest
    {
        [DataMember(Name = "vendorId")]
        public string VendorId { get; set; }

        [DataMember(Name = "messageId")]
        public string MessageId { get; set; }

        [DataMember(Name = "data")]
        public string Data { get; set; }
    }

    [DataContract]
    public class DataTransferResponse
    {
        [DataMember(Name = "status")]
        public DataTransferStatus Status { get; set; }

        [DataMember(Name = "data")]
        public string Data { get; set; } = string.Empty;
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum DiagnosticsStatus
    {
        Idle,
        Uploaded,
        UploadFailed,
        Uploading
    }

    [DataContract]
    public class DiagnosticsStatusNotificationRequest
    {
        [DataMember(Name = "status")]
        public DiagnosticsStatus Status { get; set; }
    }

    [DataContract]
    public class DiagnosticsStatusNotificationResponse
    {
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
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

    [DataContract]
    public class FirmwareStatusNotificationRequest
    {
        [DataMember(Name = "status")]
        public FirmwareStatus Status { get; set; }
    }

    [DataContract]
    public class FirmwareStatusNotificationResponse
    {
    }

    [DataContract]
    public class HeartbeatRequest
    {
    }

    [DataContract]
    public class HeartbeatResponse
    {
        [DataMember(Name = "currentTime")]
        public DateTime CurrentTime { get; set; }
    }

    [DataContract]
    public class MeterValuesRequest
    {
        [DataMember(Name = "connectorId")]
        public int ConnectorId { get; set; }

        [DataMember(Name = "transactionId")]
        public int TransactionId { get; set; }

        [DataMember(Name = "meterValue")]
        public MeterValue[] MeterValue { get; set; }
    }

    [DataContract]
    public class MeterValuesResponse
    {
    }

    [DataContract]
    public class StartTransactionRequest
    {
        [DataMember(Name = "connectorId")]
        public int ConnectorId { get; set; }

        [DataMember(Name = "idTag")]
        public string IdTag { get; set; }

        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }

        public int MeterStart { get; set; }

        [DataMember(Name = "reservationId")]
        public int ReservationId { get; set; }
    }

    [DataContract]
    public class StartTransactionResponse
    {
        [DataMember(Name = "transactionId")]
        public int TransactionId { get; set; }

        [DataMember(Name = "idTagInfo")]
        public IdTagInfo IdTagInfo { get; set; }
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
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

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
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

    [DataContract]
    public class StatusNotificationRequest
    {
        [DataMember(Name = "connectorId")]
        public int ConnectorId { get; set; }

        [DataMember(Name = "status")]
        public ChargePointStatus Status { get; set; }

        [DataMember(Name = "errorCode")]
        public ChargePointErrorCode ErrorCode { get; set; }

        [DataMember(Name = "info")]
        public string Info { get; set; }

        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }

        [DataMember(Name = "vendorId")]
        public string VendorId { get; set; }

        [DataMember(Name = "vendorErrorCode")]
        public string VendorErrorCode { get; set; }
    }

    [DataContract]
    public class StatusNotificationResponse
    {
    }

    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public enum Reason
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

    [DataContract]
    public class StopTransactionRequest
    {
        [DataMember(Name = "transactionId")]
        public int TransactionId { get; set; }

        [DataMember(Name = "idTag")]
        public string IdTag { get; set; }
                
        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }

        [DataMember(Name = "meterStop")]
        public int MeterStop { get; set; }

        [DataMember(Name = "reason")]
        public Reason Reason { get; set; }

        [DataMember(Name = "transactionData")]
        public MeterValue[] TransactionData { get; set; }
    }

    [DataContract]
    public class StopTransactionResponse
    {
        [DataMember(Name = "idTagInfo")]
        public IdTagInfo IdTagInfo { get; set; }
    }
}
