namespace Matter.Core
{
    using System.Collections.Generic;

    public static class MatterV13DeviceTypes
    {
        public static readonly IReadOnlyDictionary<ulong, string> IdToName = new Dictionary<ulong, string>
        {
            // --- Basic device types (depricated) ---
            { 0x0000, "Unknown" },
            { 0x0001, "Administrator" },
            { 0x0002, "Light" },
            { 0x0003, "Thermostat" },
            { 0x0004, "Door" },
            { 0x0005, "Window" },
            { 0x0006, "Shade" },
            { 0x0007, "Lock" },
            { 0x0008, "Sensor" },
            { 0x0009, "Switch" },

            // --- Access / Closures ---
            { 0x000A, "Door Lock" },
            { 0x000B, "Door Lock Controller" },

            // --- Aggregation / Switch ---
            { 0x000E, "Aggregator" },
            { 0x000F, "Generic Switch" },

            // --- Utility device types ---
            { 0x0011, "Power Source" },
            { 0x0012, "OTA Requestor" },
            { 0x0013, "Bridged Node" },
            { 0x0014, "OTA Provider" },
            { 0x0015, "Contact Sensor" },
            { 0x0016, "Root Node" },
            { 0x0017, "Solar Power" },
            { 0x0018, "Battery Storage" },
            { 0x0019, "Secondary Network Interface" },

            // --- Media & Mode Select ---
            { 0x0022, "Speaker" },
            { 0x0023, "Casting Video Player" },
            { 0x0024, "Content App" },
            { 0x0027, "Mode Select" },
            { 0x0028, "Basic Video Player" },
            { 0x0029, "Casting Video Client" },
            { 0x002A, "Video Remote Control" },

            // --- HVAC / Air ---
            { 0x002B, "Fan" },
            { 0x002C, "Air Quality Sensor" },
            { 0x002D, "Air Purifier" },

            // --- Irrigation / Water sensors & actuators ---
            { 0x0040, "Irrigation System" },      // C
            { 0x0041, "Water Freeze Detector" },
            { 0x0042, "Water Valve" },
            { 0x0043, "Water Leak Detector" },
            { 0x0044, "Rain Sensor" },
            { 0x0045, "Soil Sensor" },            // P

            // --- Appliances ---
            { 0x0070, "Refrigerator" },
            { 0x0071, "Temperature Controlled Cabinet" },
            { 0x0072, "Room Air Conditioner" },
            { 0x0073, "Laundry Washer" },
            { 0x0074, "Robotic Vacuum Cleaner" },
            { 0x0075, "Dishwasher" },
            { 0x0076, "Smoke CO Alarm" },
            { 0x0077, "Cook Surface" },
            { 0x0078, "Cooktop" },
            { 0x0079, "Microwave Oven" },
            { 0x007A, "Extractor Hood" },
            { 0x007B, "Oven" },
            { 0x007C, "Laundry Dryer" },

            // --- Network infrastructure ---
            { 0x0090, "Network Infrastructure Manager" },
            { 0x0091, "Thread Border Router" },

            // --- Lighting & plugs ---
            { 0x0100, "On/Off Light" },
            { 0x0101, "Dimmable Light" },
            { 0x0103, "On/Off Light Switch" },
            { 0x0104, "Dimmer Switch" },
            { 0x0105, "Color Dimmer Switch" },
            { 0x0106, "Light Sensor" },
            { 0x0107, "Occupancy Sensor" },
            { 0x010A, "On/Off Plug-in Unit" },
            { 0x010B, "Dimmable Plug-In Unit" },
            { 0x010C, "Color Temperature Light" },
            { 0x010D, "Extended Color Light" },
            { 0x010F, "Mounted On/Off Control" },
            { 0x0110, "Mounted Dimmable Load Control" },

            // --- Admin / Fabric (provisional utility) ---
            { 0x0130, "Joint Fabric Administrator" }, // P

            // --- Cameras / Doorbells (provisional unless marked C) ---
            { 0x0140, "Intercom" },                   // P
            { 0x0141, "Audio Doorbell" },             // P
            { 0x0142, "Camera" },                     // P
            { 0x0143, "Video Doorbell" },             // C
            { 0x0144, "Floodlight Camera" },          // C
            { 0x0145, "Snapshot Camera" },            // P
            { 0x0146, "Chime" },                      // P
            { 0x0147, "Camera Controller" },          // P
            { 0x0148, "Doorbell" },                   // P

            // --- Window coverings ---
            { 0x0202, "Window Covering" },
            { 0x0203, "Window Covering Controller" },
            { 0x0230, "Closure" },                    // P
            { 0x0231, "Closure Panel" },              // P
            { 0x023E, "Closure Controller" },         // P

            // --- HVAC & environmental sensors/actuators ---
            { 0x0300, "Heating/Cooling Unit" },
            { 0x0301, "Thermostat" },
            { 0x0302, "Temperature Sensor" },
            { 0x0303, "Pump" },
            { 0x0304, "Pump Controller" },
            { 0x0305, "Pressure Sensor" },
            { 0x0306, "Flow Sensor" },
            { 0x0307, "Humidity Sensor" },
            { 0x0309, "Heat Pump" },
            { 0x030A, "Thermostat Controller" },

            // --- Energy & metering ---
            { 0x050C, "Energy EVSE" },                // EV Charger
            { 0x050D, "Device Energy Management" },
            { 0x050F, "Water Heater" },
            { 0x0510, "Electrical Sensor" },
            { 0x0511, "Electrical Utility Meter" },   // P
            { 0x0512, "Meter Reference Point" },      // C
            { 0x0513, "Electrical Energy Tariff" },   // C
            { 0x0514, "Electrical Meter" },           // C

            // --- Bridges & boolean/on-off sensor ---
            { 0x0840, "Control Bridge" },
            { 0x0850, "On/Off Sensor" },
        };
    }
}
