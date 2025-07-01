/*
Copyright 2020 Cognizant
Copyright 2021 Microsoft Corporation
*/

namespace OCPPCentralSystem.Models
{
    using System.Net.WebSockets;

    public class ChargePointConnection
    {
        public ChargePointConnection(string name, WebSocket webSocket)
        {
            ChargingPointName = name;
            WebSocket = webSocket;
            WebsocketBusy = false;
            WaitingForResponse = false;
        }

        public string ChargingPointName { get; set; }

        public WebSocket WebSocket { get; set; }

        public bool WebsocketBusy { get; set; }

        public bool WaitingForResponse { get; set; }
    }
}
