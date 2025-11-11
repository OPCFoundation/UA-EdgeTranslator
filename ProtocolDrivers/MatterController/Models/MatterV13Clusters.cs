namespace Matter.Core
{
    using System.Collections.Generic;

    public class MatterV13Clusters
    {
        public static readonly IReadOnlyDictionary<ulong, string> IdToName = new Dictionary<ulong, string>
        {
            // ---- Foundation / General Application & Utility ----
            { 0x0003, "Identify" },
            { 0x0004, "Groups" },
            { 0x0005, "Scenes" },
            { 0x0006, "On/Off" },

            { 0x0008, "Level Control" },
            { 0x0016, "Root Node" },
            { 0x0017, "Descriptor" },
            { 0x0018, "Access Control" },
            { 0x0019, "Basic Information" },
            { 0x001A, "Power Configuration" },
            { 0x001B, "Device Temperature Configuration" },

            { 0x001C, "Pulse Width Modulation" },
            { 0x001D, "Descriptor" },
            { 0x001E, "Binding" },
            { 0x001F, "Access Control" },

            { 0x0025, "Actions" },
            { 0x0028, "Basic Information" },
            { 0x0029, "OTA Software Update Provider" },
            { 0x002A, "OTA Software Update Requestor" },
            { 0x002B, "Localization Configuration" },
            { 0x002C, "Time Format Localization" },
            { 0x002D, "Unit Localization" },
            { 0x002E, "Power Source Configuration" },
            { 0x002F, "Power Source" },

            { 0x0030, "General Commissioning" },
            { 0x0031, "Network Commissioning" },
            { 0x0032, "Diagnostic Logs" },
            { 0x0033, "General Diagnostics" },
            { 0x0034, "Software Diagnostics" },
            { 0x0035, "Thread Network Diagnostics" },
            { 0x0036, "Wi‑Fi Network Diagnostics" },
            { 0x0037, "Ethernet Network Diagnostics" },
            { 0x0038, "Time Synchronization" },
            { 0x0039, "Bridged Device Basic Information" },
            { 0x003B, "Switch" },
            { 0x003C, "Administrator Commissioning" },
            { 0x003E, "Operational Credentials" },
            { 0x003F, "Group Key Management" },

            { 0x0040, "Fixed Label" },
            { 0x0041, "User Label" },

            // Proxy features (provisional in 1.3 per spec table)
            { 0x0042, "Proxy Configuration" },
            { 0x0043, "Proxy Discovery" },
            { 0x0044, "Valid Proxies" },

            { 0x0045, "Boolean State" },
            { 0x0046, "ICD Management" },

            // Large Appliance & Operational State family (added/expanded through 1.3)
            { 0x0048, "Oven Cavity Operational State" },
            { 0x0049, "Oven Mode" },
            { 0x004A, "Laundry Dryer Controls" },

            { 0x0050, "Mode Select" },
            { 0x0051, "Laundry Washer Mode" },
            { 0x0052, "Refrigerator And Temperature Controlled Cabinet Mode" },
            { 0x0053, "Laundry Washer Controls" },
            { 0x0054, "RVC Run Mode" },
            { 0x0055, "RVC Clean Mode" },
            { 0x0056, "Temperature Control" },
            { 0x0057, "Refrigerator Alarm" },

            { 0x0059, "Dishwasher Mode" },
            { 0x005B, "Air Quality" },
            { 0x005C, "Smoke CO Alarm" },
            { 0x005D, "Dishwasher Alarm" },
            { 0x005E, "Microwave Oven Mode" },

            // ---- Lighting / Color ----
            { 0x0300, "Color Control" },

            // ---- Door/Window/Access ----
            { 0x0101, "Door Lock" },
            { 0x0102, "Window Covering" },

            // ---- HVAC / Environment Control ----
            { 0x0201, "Thermostat" },
            { 0x0202, "Fan Control" },
            { 0x0203, "Pump Configuration and Control" },
            { 0x0204, "Thermostat User Interface Configuration" },

            // ---- Measurement & Sensing ----
            { 0x0400, "Illuminance Measurement" },
            { 0x0402, "Temperature Measurement" },
            { 0x0403, "Pressure Measurement" },
            { 0x0404, "Flow Measurement" },
            { 0x0405, "Relative Humidity Measurement" },
            { 0x0406, "Occupancy Sensing" },

            // ---- Energy & Electrical (1.3 adds energy-management context) ----
            { 0x0B04, "Electrical Measurement" },
            { 0x0B05, "Energy Management" },
        };
    }
}
