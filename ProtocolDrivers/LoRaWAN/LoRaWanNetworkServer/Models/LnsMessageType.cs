// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using System;
    using System.Runtime.Serialization;

    [JsonConverter(typeof(StringEnumConverter))]
    public enum LnsMessageType
    {
        [EnumMember(Value = @"version")]
        Version,

        [EnumMember(Value = @"router_config")]
        RouterConfig,

        [EnumMember(Value = @"jreq")]
        JoinRequest,

        [EnumMember(Value = @"updf")]
        UplinkDataFrame,

        [EnumMember(Value = @"dntxed")]
        TransmitConfirmation,

        [EnumMember(Value = @"dnmsg")]
        DownlinkMessage,

        // the below message types are not handled in current LoRaWan Network Server implementation

        [EnumMember(Value = @"propdf")]
        ProprietaryDataFrame,

        [EnumMember(Value = @"dnsched")]
        MulticastSchedule,

        [EnumMember(Value = @"timesync")]
        TimeSync,

        [EnumMember(Value = @"runcmd")]
        RunCommand,

        [EnumMember(Value = @"rmtsh")]
        RemoteShell
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
