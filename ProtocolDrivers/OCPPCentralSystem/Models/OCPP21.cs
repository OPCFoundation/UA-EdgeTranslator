namespace OCPPCentralSystem.Models
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    [DataContract]
    public class OCSPRequest
    {
        [DataMember (Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "hashAlgorithm")]
        public HashAlgorithm HashAlgorithm { get; set; }

        [DataMember(Name = "issuerNameHash")]
        public string IssuerNameHash { get; set; }

        [DataMember(Name = "issuerKeyHash")]
        public string IssuerKeyHash { get; set; }

        [DataMember(Name = "serialNumber")]
        public string SerialNumber { get; set; }

        [DataMember(Name = "responderURL")]
        public string ResponderURL { get; set; }
    }

    [DataContract]
    public class AuthorizeRequestV21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "idToken")]
        public IdTokenType IdToken { get; set; }

        [DataMember(Name = "certificate")]
        public string Certificate { get; set; }

        [DataMember(Name = "iso15118CertificateHashData")]
        public ICollection<OCSPRequest> Iso15118CertificateHashData { get; set; }
    }

    [DataContract]
    public class AdditionalInfo
    {
        [DataMember (Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "additionalIdToken")]
        public string AdditionalIdToken { get; set; }

        [DataMember(Name = "type")]
        public string Type { get; set; }
    }

    [DataContract]
    public class IdTokenInfo
    {
        [DataMember (Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "status")]
        public AuthorizationStatus Status { get; set; }

        [DataMember(Name = "cacheExpiryDateTime")]
        public DateTime CacheExpiryDateTime { get; set; }

        [DataMember(Name = "chargingPriority")]
        public int ChargingPriority { get; set; }

        [DataMember(Name = "language1")]
        public string Language1 { get; set; }

        [DataMember(Name = "evseId")]
        public ICollection<int> EvseId { get; set; }

        [DataMember(Name = "groupIdToken")]
        public IdTokenType GroupIdToken { get; set; }

        [DataMember(Name = "language2")]
        public string Language2 { get; set; }

        [DataMember(Name = "personalMessage")]
        public MessageContent PersonalMessage { get; set; }
    }

    [DataContract]
    public class IdTokenType
    {
        [DataMember (Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "additionalInfo")]
        public ICollection<AdditionalInfo> AdditionalInfo { get; set; }

        [DataMember(Name = "idToken")]
        public string IdToken { get; set; }

        [DataMember(Name = "type")]
        public IdToken Type { get; set; }
    }

    [DataContract]
    public class MessageContent
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "format")]
        public MessageFormat Format { get; set; }

        [DataMember(Name = "language")]
        public string Language { get; set; }

        [DataMember(Name = "content")]
        public string Content { get; set; }
    }

    [DataContract]
    public class AuthorizeResponse21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "idTokenInfo")]
        public IdTokenInfo IdTokenInfo { get; set; }

        [DataMember(Name = "certificateStatus")]
        public AuthorizeCertificateStatus CertificateStatus { get; set; }
    }

    [DataContract]
    public class TokenInfo
    {
        [DataMember(Name = "status")]
        public AuthorizationStatus Status { get; set; }

        [DataMember(Name = "cacheExpiryDateTime")]
        public DateTime? CacheExpiryDateTime { get; set; }

        [DataMember(Name = "language1")]
        public string Language1 { get; set; }

        [DataMember(Name = "language2")]
        public string Language2 { get; set; }

        [DataMember(Name = "personalMessage")]
        public string PersonalMessage { get; set; }
    }

    [DataContract]
    public class ChargingStation
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "serialNumber")]
        public string SerialNumber { get; set; }

        [DataMember(Name = "model")]
        public string Model { get; set; }

        [DataMember(Name = "modem")]
        public ModemType Modem { get; set; }

        [DataMember(Name = "vendorName")]
        public string VendorName { get; set; }

        [DataMember(Name = "firmwareVersion")]
        public string FirmwareVersion { get; set; }
    }

    [DataContract]
    public class AuthorizeRequest21
    {
        [DataMember(Name = "idToken")]
        public IdTokenType IdToken { get; set; }

        [DataMember(Name = "certificate")]
        public string Certificate { get; set; }
    }

    [DataContract]
    public class CustomData
    {
        [DataMember(Name = "vendorId")]
        public string VendorId { get; set; }

        [DataMember(Name = "additionalProperties")]
        public IDictionary<string, object> AdditionalProperties { get; set; }
    }

    [DataContract]
    public class ModemType
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "iccid")]
        public string Iccid { get; set; }

        [DataMember(Name = "imsi")]
        public string Imsi { get; set; }
    }

    [DataContract]
    public class BootNotificationRequest21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "chargingStation")]
        public ChargingStation ChargingStation { get; set; }

        [DataMember(Name = "reason")]
        public BootReason Reason { get; set; }
    }

    [DataContract]
    public class StatusInfo
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "reasonCode")]
        public string ReasonCode { get; set; }

        [DataMember(Name = "additionalInfo")]
        public string AdditionalInfo { get; set; }
    }

    [DataContract]
    public class BootNotificationResponse21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "currentTime")]
        public DateTime CurrentTime { get; set; }

        [DataMember(Name = "interval")]
        public int Interval { get; set; }

        [DataMember(Name = "status")]
        public RegistrationStatus Status { get; set; }

        [DataMember(Name = "statusInfo")]
        public StatusInfo StatusInfo { get; set; }
    }

    [DataContract]
    public class HeartbeatRequest21
    {
        [DataMember (Name = "customData")]
        public CustomData CustomData { get; set; }
    }

    [DataContract]
    public class HeartbeatResponse21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "currentTime")]
        public DateTime CurrentTime { get; set; }
    }

    [DataContract]
    public class DataTransferRequest21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "messageId")]
        public string MessageId { get; set; }

        [DataMember(Name = "data")]
        public object Data { get; set; }

        [DataMember(Name = "vendorId")]
        public string VendorId { get; set; }
    }

    [DataContract]
    public class DataTransferResponse21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "status")]
        public DataTransferStatus Status { get; set; }

        [DataMember(Name = "statusInfo")]
        public StatusInfo StatusInfo { get; set; }

        [DataMember(Name = "data")]
        public object Data { get; set; }
    }

    [DataContract]
    public class MeterValue21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "sampledValue")]
        public ICollection<SampledValue21> SampledValue { get; set; }

        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }
    }

    [DataContract]
    public class SampledValue21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "value")]
        public double Value { get; set; }

        [DataMember(Name = "context")]
        public ReadingContext Context { get; set; }

        [DataMember(Name = "measurand")]
        public Measurand Measurand { get; set; }

        [DataMember(Name = "phase")]
        public Phase Phase { get; set; }

        [DataMember(Name = "location")]
        public Location Location { get; set; }

        [DataMember(Name = "signedMeterValue")]
        public SignedMeterValue SignedMeterValue { get; set; }

        [DataMember(Name = "unitOfMeasure")]
        public UnitOfMeasure21 UnitOfMeasure { get; set; }
    }

    [DataContract]
    public class SignedMeterValue
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "signedMeterData")]
        public string SignedMeterData { get; set; }

        [DataMember(Name = "signingMethod")]
        public string SigningMethod { get; set; }

        [DataMember(Name = "encodingMethod")]
        public string EncodingMethod { get; set; }

        [DataMember(Name = "publicKey")]
        public string PublicKey { get; set; }
    }

    [DataContract]
    public class UnitOfMeasure21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "unit")]
        public string Unit { get; set; }

        [DataMember(Name = "multiplier")]
        public int Multiplier { get; set; } = 0;
    }

    [DataContract]
    public class MeterValuesRequest21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "evseId")]
        public int EvseId { get; set; }

        [DataMember(Name = "meterValue")]
        public ICollection<MeterValue21> MeterValue { get; set; }
    }

    [DataContract]
    public class MeterValuesResponse21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }
    }

    [DataContract]
    public class NotifyEventRequest
    {
        [DataMember(Name = "generatedAt")]
        public DateTime GeneratedAt { get; set; }

        [DataMember(Name = "seqNo")]
        public int SeqNo { get; set; }

        [DataMember(Name = "eventData")]
        public EventData[] EventData { get; set; }
    }

    [DataContract]
    public class EventData
    {
        [DataMember(Name = "eventId")]
        public int EventId { get; set; }

        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }

        [DataMember(Name = "trigger")]
        public EventTrigger Trigger { get; set; }

        [DataMember(Name = "actualValue")]
        public string ActualValue { get; set; }
    }

    [DataContract]
    public class EVSE
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "connectorId")]
        public int ConnectorId { get; set; }
    }

    [DataContract]
    public class Transaction
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "transactionId")]
        public string TransactionId { get; set; }

        [DataMember(Name = "chargingState")]
        public ChargingState ChargingState { get; set; }

        [DataMember(Name = "timeSpentCharging")]
        public int TimeSpentCharging { get; set; }

        [DataMember(Name = "stoppedReason")]
        public StoppedReason StoppedReason { get; set; }

        [DataMember(Name = "remoteStartId")]
        public int RemoteStartId { get; set; }
    }

    [DataContract]
    public class TransactionEventRequest
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "eventType")]
        public TransactionEvent EventType { get; set; }

        [DataMember(Name = "meterValue")]
        public ICollection<MeterValue21> MeterValue { get; set; }

        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }

        [DataMember(Name = "triggerReason")]
        public TriggerReason TriggerReason { get; set; }

        [DataMember(Name = "seqNo")]
        public int SeqNo { get; set; }

        [DataMember(Name = "offline")]
        public bool Offline { get; set; }

        [DataMember(Name = "numberOfPhasesUsed")]
        public int NumberOfPhasesUsed { get; set; }

        [DataMember(Name = "cableMaxCurrent")]
        public int CableMaxCurrent { get; set; }

        [DataMember(Name = "reservationId")]
        public int ReservationId { get; set; }

        [DataMember(Name = "transactionInfo")]
        public Transaction TransactionInfo { get; set; }

        [DataMember(Name = "evse")]
        public EVSE Evse { get; set; }

        [DataMember(Name = "idToken")]
        public IdTokenType IdToken { get; set; }
    }

    [DataContract]
    public class TransactionEventResponse
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "totalCost")]
        public double TotalCost { get; set; }

        [DataMember(Name = "chargingPriority")]
        public int ChargingPriority { get; set; }

        [DataMember(Name = "idTokenInfo")]
        public IdTokenInfo IdTokenInfo { get; set; }

        [DataMember(Name = "updatedPersonalMessage")]
        public MessageContent UpdatedPersonalMessage { get; set; }
    }

    [DataContract]
    public class StatusNotificationRequest21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }

        [DataMember(Name = "timestamp")]
        public System.DateTimeOffset Timestamp { get; set; }

        [DataMember(Name = "connectorStatus")]
        public ConnectorStatus ConnectorStatus { get; set; }

        [DataMember(Name = "evseId")]
        public int EvseId { get; set; }

        [DataMember(Name = "connectorId")]
        public int ConnectorId { get; set; }
    }

    [DataContract]
    public class StatusNotificationResponse21
    {
        [DataMember(Name = "customData")]
        public CustomData CustomData { get; set; }
    }
}

