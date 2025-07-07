// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer.BasicsStation
{
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;
    using Serilog;
    using System;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;

    public class DownlinkMessageSender(ILogger<DownlinkMessageSender> logger)
    {
        private readonly Random random = new Random();

        public Task SendDownlinkAsync(DownlinkMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            if (message.StationEui == default)
            {
                throw new ArgumentException($"A proper StationEui needs to be set. Received '{message.StationEui}'.");
            }

            WebsocketJsonMiddlewareLoRaWAN.PendingMessages.TryAdd(message.StationEui.ToString(), SerializeMessage(message));

            return Task.CompletedTask;
        }

        private string SerializeMessage(DownlinkMessage message)
        {
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms);

            writer.WriteStartObject();

            writer.WriteString("msgtype", LnsMessageType.DownlinkMessage.ToBasicStationString());
            writer.WriteString("DevEui", message.DevEui.ToString());

            writer.WriteNumber("dC", message.DeviceClassType switch
            {
                LoRaDeviceClassType.A => 0,
                LoRaDeviceClassType.B => 1,
                LoRaDeviceClassType.C => 2,
                _ => throw new SwitchExpressionException(),
            });

            // Getting and writing payload bytes
            var pduBytes = message.Data;
            var pduChars = new char[pduBytes.Length * 2];
            Hexadecimal.Write(pduBytes.Span, pduChars);
            writer.WriteString("pdu", pduChars);

#pragma warning disable CA5394 // Do not use insecure randomness. This is fine as not used for any crypto operations.
            var diid = this.random.Next(int.MinValue, int.MaxValue);
            writer.WriteNumber("diid", diid);
#pragma warning restore CA5394 // Do not use insecure randomness

            Log.Logger.Information("sending message to station with EUI '{StationEui}' with diid {Diid}. Payload '{Payload}'.", message.StationEui, diid, message.Data.ToHex());

            switch (message.DeviceClassType)
            {
                case LoRaDeviceClassType.A:
                    writer.WriteNumber("RxDelay", message.LnsRxDelay.ToSeconds());
                    if (message.Rx1 is var (datr, freq))
                    {
                        writer.WriteNumber("RX1DR", (int)datr);
                        writer.WriteNumber("RX1Freq", (ulong)freq);
                    }
                    writer.WriteNumber("RX2DR", (int)message.Rx2.DataRate);
                    writer.WriteNumber("RX2Freq", (ulong)message.Rx2.Frequency);
                    writer.WriteNumber("xtime", message.Xtime);
                    break;
                case LoRaDeviceClassType.B:
                    throw new NotSupportedException($"{nameof(DownlinkMessageSender)} does not support class B devices yet.");
                case LoRaDeviceClassType.C:
                    // if Xtime is not zero, it means that we are answering to a previous message
                    if (message.Xtime != 0)
                    {
                        writer.WriteNumber("RxDelay", message.LnsRxDelay.ToSeconds());
                        writer.WriteNumber("xtime", message.Xtime);
                        if (message.Rx1 is var (datrC, freqC))
                        {
                            writer.WriteNumber("RX1DR", (int)datrC);
                            writer.WriteNumber("RX1Freq", (ulong)freqC);
                        }
                    }
                    writer.WriteNumber("RX2DR", (int)message.Rx2.DataRate);
                    writer.WriteNumber("RX2Freq", (ulong)message.Rx2.Frequency);
                    break;
                default:
                    throw new SwitchExpressionException();
            }

            if (message.AntennaPreference.HasValue)
            {
                writer.WriteNumber("rctx", message.AntennaPreference.Value);
            }

            writer.WriteNumber("priority", 0); // Currently always setting to maximum priority.

            writer.WriteEndObject();

            writer.Flush();
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
