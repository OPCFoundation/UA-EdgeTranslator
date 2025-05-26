/*
Copyright 2020 Cognizant
Copyright 2021 Microsoft Corporation
*/

namespace OCPPCentralSystem.Models
{
    using System.Net.WebSockets;
    using System.Collections.Generic;

    public class ChargePointConnection
    {
        public string ChargingPointName { get; set; }

        public Dictionary<string, string> RequestDictionary { get; set; } //Used for Central system initiated commands

        public Dictionary<string,object> ChargerResponse { get; set; } //Used for Central system initiated commands

        public WebSocket WebSocket { get; set; }

        public bool WebsocketBusy { get; set; }

        public bool Authorized { get; set; }

        public bool WaitingResponse => RequestDictionary.Count != 0;

        public ChargePointConnection(string name, WebSocket webSocket)
        {
            ChargingPointName = name;
            WebSocket = webSocket;
            RequestDictionary = new Dictionary<string, string>();
            ChargerResponse = new Dictionary<string, object>();
            WebsocketBusy = false;
            Authorized=false;
        }
    }
}
