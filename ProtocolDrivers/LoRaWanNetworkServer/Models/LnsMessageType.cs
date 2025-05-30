// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;

    public enum LnsMessageType
    {
        Version,                // version
        RouterConfig,           // router_config
        JoinRequest,            // jreq
        UplinkDataFrame,        // updf
        TransmitConfirmation,   // dntxed
        DownlinkMessage,        // dnmsg

        // Following message types are not handled in current LoRaWan Network Server implementation
        ProprietaryDataFrame,   // propdf
        MulticastSchedule,      // dnsched
        TimeSync,               // timesync
        RunCommand,             // runcmd
        RemoteShell             // rmtsh
    }

    public static class LnsMessageTypeExtensions
    {
        public static string ToBasicStationString(this LnsMessageType lnsMessageType) => lnsMessageType switch
        {
            LnsMessageType.Version => "version",
            LnsMessageType.RouterConfig => "router_config",
            LnsMessageType.JoinRequest => "jreq",
            LnsMessageType.UplinkDataFrame => "updf",
            LnsMessageType.TransmitConfirmation => "dntxed",
            LnsMessageType.DownlinkMessage => "dnmsg",
            LnsMessageType.ProprietaryDataFrame => "propdf",
            LnsMessageType.MulticastSchedule => "dnsched",
            LnsMessageType.TimeSync => "timesync",
            LnsMessageType.RunCommand => "runcmd",
            LnsMessageType.RemoteShell => "rmtsh",
            _ => throw new ArgumentOutOfRangeException(nameof(lnsMessageType), lnsMessageType, null),
        };
    }
}
