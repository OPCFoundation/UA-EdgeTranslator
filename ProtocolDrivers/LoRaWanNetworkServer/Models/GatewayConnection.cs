// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan
{
    using LoRaWan.NetworkServer;
    using System.Collections.Generic;
    using System.Net.WebSockets;

    public class GatewayConnection
    {
        public GatewayConnection(string name, WebSocket webSocket)
        {
            GatewayName = name;
            WebSocket = webSocket;
        }

        public string GatewayName { get; set; }

        public WebSocket WebSocket { get; set; }

        public Dictionary<DevAddr, LoRaDevice> Devices { get; } = new();
    }
}
