
namespace OCPPCentralSystem.Models
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    [DataContract]
    public class NotifyEVChargingNeedsRequest
    {
        [DataMember(Name = "evseId")]
        public string EvseId { get; set; }

        [DataMember(Name = "maxScheduleTuples")]
        public int MaxScheduleTuples { get; set; }

        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }

        [DataMember(Name = "chargingNeeds")]
        public ChargingNeeds ChargingNeeds { get; set; }
    }

    [DataContract]
    public class ChargingNeeds
    {
        [DataMember(Name = "requestedEnergyTransfer")]
        public EnergyTransferMode RequestedEnergyTransfer { get; set; }

        [DataMember(Name = "availableEnergyTransfer")]
        public EnergyTransferMode AvailableEnergyTransfer { get; set; }

        [DataMember(Name = "controlMode")]
        public ControlMode ControlMode { get; set; }

        [DataMember(Name = "mobilityNeedsMode")]
        public MobilityNeedsMode MobilityNeedsMode { get; set; }

        [DataMember(Name = "departureTime")]
        public DateTime DepartureTime { get; set; }

        [DataMember(Name = "v2xChargingParameters")]
        public V2XChargingParameters V2XChargingParameters { get; set; }

        [DataMember(Name = "dcChargingParameters")]
        public DCChargingParameters DCChargingParameters { get; set; }

        [DataMember(Name = "acChargingParameters")]
        public ACChargingParameters ACChargingParameters { get; set; }

        [DataMember(Name = "evEnergyOffer")]
        public EvEnergyOffer EvEnergyOffer { get; set; }

        [DataMember(Name = "derChargingParameters")]
        public DERChargingParameters DerChargingParameters { get; set; }
    }

    [DataContract]
    public class V2XChargingParameters
    {
        [DataMember(Name = "minChargePower")]
        public int MinChargePowerL1 { get; set; }

        [DataMember(Name = "minChargePower_L2")]
        public int MinChargePowerL2 { get; set; }

        [DataMember(Name = "minChargePower_L3")]
        public int MinChargePowerL3 { get; set; }
    }

    [DataContract]
    public class DCChargingParameters
    {
        [DataMember(Name = "evMaxCurrent")]
        public int EVMaxCurrent { get; set; }

        [DataMember(Name = "evMaxVoltage")]
        public int EVMaxVoltage { get; set; }

        [DataMember(Name = "evMaxPower")]
        public int EVMaxPower { get; set; }

        [DataMember(Name = "evEnergyCapacity")]
        public int EVEnergyCapacity { get; set; }

        [DataMember(Name = "evEnergyAmount")]
        public int EVEnergyAmount { get; set; }

        [DataMember(Name = "stateOfCharge")]
        public int StateOfCharge { get; set; }

        [DataMember(Name = "fullSoC")]
        public int FullSoC { get; set; }

        [DataMember(Name = "bulkSoC")]
        public int BulkSoC { get; set; }
    }

    [DataContract]
    public class ACChargingParameters
    {
        [DataMember(Name = "energyAmount")]
        public int EnergyAmount { get; set; }

        [DataMember(Name = "evMinCurrent")]
        public int EvMinCurrent { get; set; }

        [DataMember(Name = "evMaxCurrent")]
        public int EvMaxCurrent { get; set; }

        [DataMember(Name = "evMaxVoltage")]
        public int EvMaxVoltage { get; set; }
    }

    [DataContract]
    public class EvEnergyOffer
    {
        [DataMember(Name = "evPowerSchedule")]
        public EvPowerSchedule EvPowerSchedule { get; set; }

        [DataMember(Name = "evAbsolutePriceShedule")]
        public EvAbsolutePriceSchedule EvAbsolutePriceSchedule { get; set; }
    }

    [DataContract]
    public class EvPowerSchedule
    {
        [DataMember (Name = "timeAnchor")]
        public DateTime TimeAnchor { get; set; }

        [DataMember(Name = "evPowerScheduleEntries")]
        public List<PowerScheduleEntry> EvPowerScheduleEntries { get; set; }
    }

    [DataContract]
    public class PowerScheduleEntry
    {
        [DataMember(Name = "duration")]
        public int Duration { get; set; }

        [DataMember(Name = "power")]
        public int Power { get; set; }
    }

    [DataContract]
    public class EvAbsolutePriceSchedule
    {
        [DataMember(Name = "timeAnchor")]
        public DateTime TimeAnchor { get; set; }

        [DataMember(Name = "currency")]
        public string Currency { get; set; }

        [DataMember(Name = "priceAlgorithm")]
        public string PriceAlgorithm { get; set; }

        [DataMember(Name = "evAbsolutePriceScheduleEntries")]
        public List<AbsolutePriceScheduleEntry> EvAbsolutePriceScheduleEntries { get; set; }
    }

    [DataContract]
    public class AbsolutePriceScheduleEntry
    {
        [DataMember(Name = "duration")]
        public int Duration { get; set; }

        [DataMember(Name = "evPriceRule")]
        public EVPriceRule EVPriceRule { get; set; }
    }

    [DataContract]
    public class EVPriceRule
    {
        [DataMember(Name = "energyFee")]
        public decimal EnergyFee { get; set; }
    }

    [DataContract]
    public class DERChargingParameters
    {
        [DataMember(Name = "evSupportedDERControl")]
        public DERControl EvSupportedDERControl { get; set; }

        [DataMember(Name = "evOverExcitedMaxDischargePower")]
        public int EvOverExcitedMaxDischargePower { get; set; }

        [DataMember(Name = "evOverExcitedPowerFactor")]
        public int EvOverExcitedPowerFactor { get; set; }

        [DataMember(Name = "evUnderExcitedMaxDischargePower")]
        public int EvUnderExcitedMaxDischargePower { get; set; }

        [DataMember(Name = "evUnderExcitedPowerFactor")]
        public int EvUnderExcitedPowerFactor { get; set; }

        [DataMember(Name = "maxApparentPower")]
        public int MaxApparentPower { get; set; }

        [DataMember(Name = "maxChargeApparentPower")]
        public int MaxChargeApparentPowerL1 { get; set; }

        [DataMember(Name = "maxChargeApparentPower_L2")]
        public int MaxChargeApparentPowerL2 { get; set; }

        [DataMember(Name = "maxChargeApparentPower_L3")]
        public int MaxChargeApparentPowerL3 { get; set; }

        [DataMember(Name = "maxDischargeApparentPower")]
        public int MaxDischargeApparentPowerL1 { get; set; }

        [DataMember(Name = "maxDischargeApparentPower_L2")]
        public int MaxDischargeApparentPowerL2 { get; set; }

        [DataMember(Name = "maxDischargeApparentPower_L3")]
        public int MaxDischargeApparentPowerL3 { get; set; }

        [DataMember(Name = "maxChargeReactivePower")]
        public int MaxChargeReactivePowerL1 { get; set; }

        [DataMember(Name = "maxChargeReactivePower_L2")]
        public int MaxChargeReactivePowerL2 { get; set; }

        [DataMember(Name = "maxChargeReactivePower_L3")]
        public int MaxChargeReactivePowerL3 { get; set; }

        [DataMember(Name = "minChargeReactivePower")]
        public int MinChargeReactivePowerL1 { get; set; }
        
        [DataMember(Name = "minChargeReactivePower_L2")]
        public int MinChargeReactivePowerL2 { get; set; }

        [DataMember(Name = "minChargeReactivePower_L3")]
        public int MinChargeReactivePowerL3 { get; set; }

        [DataMember(Name = "maxDischargeReactivePower")]
        public int MaxDischargeReactivePowerL1 { get; set; }

        [DataMember(Name = "maxDischargeReactivePower_L2")]
        public int MaxDischargeReactivePowerL2 { get; set; }

        [DataMember(Name = "maxDischargeReactivePower_L3")]
        public int MaxDischargeReactivePowerL3 { get; set; }

        [DataMember(Name = "minDischargeReactivePower")]
        public int MinDischargeReactivePowerL1 { get; set; }

        [DataMember(Name = "minDischargeReactivePower_L2")]
        public int MinDischargeReactivePowerL2 { get; set; }

        [DataMember(Name = "minDischargeReactivePower_L3")]
        public int MinDischargeReactivePowerL3 { get; set; }

        [DataMember(Name = "nominalVoltage")]
        public int NominalVoltage { get; set; }

        [DataMember(Name = "nominalVoltageOffset")]
        public int NominalVoltageOffset { get; set; }

        [DataMember(Name = "maxNominalVoltage")]
        public int MaxNominalVoltage { get; set; }

        [DataMember(Name = "minNominalVoltage")]
        public int MinNominalVoltage { get; set; }

        [DataMember(Name = "evInverterManufacturer")]
        public string EvInverterManufacturer { get; set; }

        [DataMember(Name = "evInverterModel")]
        public string EvInverterVendor { get; set; }

        [DataMember(Name = "evInverterSerialNumber")]
        public string EvInverterSerialNumber { get; set; }

        [DataMember(Name = "evInverterSwVersion")]
        public string EvInverterSwVersion { get; set; }

        [DataMember(Name = "evInverterHwVersion")]
        public string EvInverterHwVersion { get; set; }

        [DataMember(Name = "evIslandingDetectionMethod")]
        public IslandingDetection EvIslandingDetectionMethod { get; set; }

        [DataMember(Name = "evIslandingTripTime")]
        public int EvIslandingTripTime { get; set; }

        [DataMember(Name = "evMaxLevel1DCInjection")]
        public int EvMaxLevel1DCInjection { get; set; }

        [DataMember(Name = "evDurationLevel1DCInjection")]
        public int EvDurationLevel1DCInjection { get; set; }

        [DataMember(Name = "evMaxLevel2DCInjection")]
        public int EvMaxLevel2DCInjection { get; set; }

        [DataMember(Name = "evDurationLevel2DCInjection")]
        public int EvDurationLevel2DCInjection { get; set; }

        [DataMember(Name = "evReactiveSusceptance")]
        public int EvReactiveSusceptance { get; set; }

        [DataMember(Name = "evSessionTotalDischargeEnergyAvailable")]
        public int EvSessionTotalDischargeEnergyAvailable { get; set; }
    }

    [DataContract]
    public class NotifyEVChargingNeedsResponse
    {
        [DataMember(Name = "status")]
        public NotifyEVChargingNeedsStatus Status { get; set; }

        [DataMember(Name = "statusInfo")]
        public StatusInfo StatusInfo { get; set; }
    }

    [DataContract]
    public class NotifyChargingLimitsRequest
    {
        [DataMember(Name = "evseId")]
        public string EvseId { get; set; }

        [DataMember(Name = "chargingLimit")]
        public ChargingLimit ChargingLimit { get; set; }

        [DataMember(Name = "chargingSchedule")]
        public List<ChargingSchedule> ChargingSchedule { get; set; }
    }

    [DataContract]
    public class ChargingLimit
    {
        [DataMember(Name = "chargingLimitSource")]
        public string ChargingLimitSource { get; set; }

        [DataMember(Name = "isLocalGeneration")]
        public bool IsLocalGeneration { get; set; }

        [DataMember(Name = "isGridCritical")]
        public bool IsGridCritical { get; set; }
    }

    [DataContract]
    public class NotifyChargingLimitsResponse
    {
    }
}
