/*
Copyright 2020 Cognizant
Copyright 2021 Microsoft Corporation
*/

namespace OCPPCentralSystem.Models
{
    public class StringConstants
    {
        public static string NotImplemented = "NotImplmented";

        public static string RequiredProtocol = "ocpp1.6";
        
        public static string NoProtocolHeaderMessage = "No sub-protocol header";
        
        public static string SubProtocolNotSupportedMessage = "Sub-protocol not supported";
        
        public static string ClientInitiatedNewWebsocketMessage = "Client sent new websocket request";
        
        public static string ChargerNewWebRequestMessage = "New websocket request received for this charger";
        
        public static string RequestContentFormat = "application/json";
        
        public static string StationChargerTag = "StationChargerId";
        
        public static string ClientRequestedClosureMessage = "Client requested closure";
        
        public static string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    }
}