
namespace OCPPCentralSystem.Models
{
    using System;
    using System.Collections.Generic;
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
        public SampledValueMeasurand Measurand { get; set; }

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

    [DataContract]
    public class DataTransferRequest
    {
        [DataMember(Name = "messageId")]
        public string MessageId { get; set; }

        [DataMember(Name = "data")]
        public string Data { get; set; }

        [DataMember(Name = "vendorId")]
        public string VendorId { get; set; }
    }

    [DataContract]
    public class DataTransferResponse
    {
        [DataMember(Name = "status")]
        public DataTransferStatus Status { get; set; }

        [DataMember(Name = "data")]
        public string Data { get; set; } = string.Empty;
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
        public StopReason Reason { get; set; }

        [DataMember(Name = "transactionData")]
        public MeterValue[] TransactionData { get; set; }
    }

    [DataContract]
    public class StopTransactionResponse
    {
        [DataMember(Name = "idTagInfo")]
        public IdTagInfo IdTagInfo { get; set; }
    }

    [DataContract]
    public class ChangeAvailabilityRequest
    {
        [DataMember(Name = "connectorId")]
        public int ConnectorId { get; set; }

        [DataMember(Name = "type")]
        public Availability Type { get; set; }
    }

    [DataContract]
    public class ChangeAvailabilityResponse
    {
        [DataMember(Name = "status")]
        public AvailabilityStatus Status { get; set; }
    }

    [DataContract]
    public class ChangeConfigurationRequest
    {
        [DataMember(Name = "key")]
        public string Key { get; set; }

        [DataMember(Name = "value")]
        public string Value { get; set; }
    }

    [DataContract]
    public class ClearCacheRequest
    {
    }

    [DataContract]
    public class GetConfigurationRequest
    {
        [DataMember(Name = "key")]
        public string[] Key { get; set; }
    }

    [DataContract]
    public class ChargingProfile
    {
        [DataMember(Name = "chargingProfileId")]
        public int ChargingProfileId { get; set; }

        [DataMember(Name = "stackLevel")]
        public int StackLevel { get; set; }

        [DataMember(Name = "chargingProfilePurpose")]
        public ChargingProfilePurpose ChargingProfilePurpose { get; set; }

        [DataMember(Name = "chargingProfileKind")]
        public ChargingProfileKind ChargingProfileKind { get; set; }

        [DataMember(Name = "recurrencyKind")]
        public RecurrencyKind? RecurrencyKind { get; set; }

        [DataMember(Name = "validFrom")]
        public DateTime? ValidFrom { get; set; }

        [DataMember(Name = "validTo")]
        public DateTime? ValidTo { get; set; }

        [DataMember(Name = "chargingSchedule")]
        public ChargingSchedule ChargingSchedule { get; set; }
    }

    [DataContract]
    public class ChargingSchedule
    {
        [DataMember(Name = "duration")]
        public int? Duration { get; set; }

        [DataMember(Name = "startSchedule")]
        public DateTime? StartSchedule { get; set; }

        [DataMember(Name = "chargingRateUnit")]
        public ChargingRateUnit ChargingRateUnit { get; set; }

        [DataMember(Name = "chargingSchedulePeriod")]
        public List<ChargingSchedulePeriod> ChargingSchedulePeriod { get; set; }

        [DataMember(Name = "minChargingRate")]
        public decimal? MinChargingRate { get; set; }
    }

    [DataContract]
    public class ChargingSchedulePeriod
    {
        [DataMember(Name = "startPeriod")]
        public int StartPeriod { get; set; }

        [DataMember(Name = "limit")]
        public decimal Limit { get; set; }

        [DataMember(Name = "numberPhases")]
        public int? NumberPhases { get; set; }
    }

    [DataContract]
    public class RemoteStartTransactionRequest
    {
        [DataMember(Name = "connectorId")]
        public int ConnectorId { get; set; }

        [DataMember(Name = "idTag")]
        public string IdTag { get; set; }

        [DataMember(Name = "chargingProfile")]
        public ChargingProfile ChargingProfile { get; set; }
    }

    [DataContract]
    public class RemoteStopTransactionRequest
    {
        [DataMember(Name = "transactionId")]
        public int TransactionId { get; set; }
    }

    [DataContract]
    public class ResetRequest
    {
        [DataMember(Name = "type")]
        public ResetType Type { get; set; }
    }

    [DataContract]
    public class UnlockConnectorRequest
    {
        [DataMember(Name = "connectorId")]
        public int ConnectorId { get; set; }
    }

    [DataContract]
    public class SetChargingProfileRequest
    {
        [DataMember(Name = "connectorId")]
        public int ConnectorId { get; set; }

        [DataMember(Name = "csChargingProfiles")]
        public ChargingProfile CSChargingProfiles { get; set; }
    }
}
