// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using static ReceiveWindowNumber;
    using static RxDelay;

    /// <summary>
    /// Helper class to create <see cref="DownlinkMessage"/>.
    /// </summary>
    internal static class DownlinkMessageBuilder
    {
        private static readonly RandomNumberGenerator RndKeysGenerator = RandomNumberGenerator.Create();

        /// <summary>
        /// Creates downlink message with ack for confirmation or device message.
        /// </summary>
        internal static DownlinkMessageBuilderResponse CreateDownlinkMessage(
            NetworkServerConfiguration configuration,
            LoRaDevice loRaDevice,
            LoRaRequest request,
            LoRaOperationTimeWatcher timeWatcher,
            LoRaPayloadData payload,
            bool fpending,
            uint fcntDown,
            LoRaADRResult loRaADRResult,
            ILogger logger)
        {
            ArgumentOutOfRangeException.ThrowIfZero(fcntDown);

            var radioMetadata = request.RadioMetadata;
            var loRaRegion = request.Region;
            var isMessageTooLong = false;

            // default fport
            var fctrl = FrameControlFlags.None;
            if (payload.MessageType == MacMessageType.ConfirmedDataUp)
            {
                // Confirm receiving message to device
                fctrl = FrameControlFlags.Ack;
            }

            // Calculate receive window
            var receiveWindow = timeWatcher.ResolveReceiveWindowToUse(loRaDevice);
            if (receiveWindow is null)
            {
                // No valid receive window. Abandon the message
                isMessageTooLong = true;
                return new DownlinkMessageBuilderResponse(null, isMessageTooLong, receiveWindow);
            }

            var rndToken = new byte[2];

            RndKeysGenerator.GetBytes(rndToken);

            DataRateIndex datr;
            Hertz freq;

            var deviceJoinInfo = request.Region.LoRaRegion == LoRaRegionType.CN470RP2
                ? new DeviceJoinInfo(loRaDevice.ReportedCN470JoinChannel, loRaDevice.DesiredCN470JoinChannel)
                : null;

            if (loRaRegion is DwellTimeLimitedRegion someRegion)
                someRegion.UseDwellTimeSetting(loRaDevice.ReportedDwellTimeSetting);

            if (receiveWindow is ReceiveWindow2)
            {
                freq = loRaRegion.GetDefaultRX2ReceiveWindow(deviceJoinInfo).Frequency;
                datr = loRaRegion.GetDefaultRX2ReceiveWindow(deviceJoinInfo).DataRate;
            }
            else
            {
                datr = loRaRegion.GetDownstreamDataRate(radioMetadata.DataRate, loRaDevice.ReportedRX1DROffset);

                // The logic for passing CN470 join channel will change as part of #561
                if (!loRaRegion.TryGetDownstreamChannelFrequency(radioMetadata.Frequency, upstreamDataRate: radioMetadata.DataRate, deviceJoinInfo: deviceJoinInfo, downstreamFrequency: out freq))
                {
                    logger.LogError("there was a problem in setting the frequency in the downstream message settings");
                    return new DownlinkMessageBuilderResponse(null, false, receiveWindow);
                }
            }

            var rx2 = new ReceiveWindow(loRaRegion.GetDefaultRX2ReceiveWindow(deviceJoinInfo).DataRate, loRaRegion.GetDefaultRX2ReceiveWindow(deviceJoinInfo).Frequency);

            // get max. payload size based on data rate from LoRaRegion
            var maxPayloadSize = loRaRegion.GetMaxPayloadSize(datr);

            // Deduct 8 bytes from max payload size.
            maxPayloadSize -= Constants.LoraProtocolOverheadSize;

            var availablePayloadSize = maxPayloadSize;

            var macCommands = new List<MacCommand>();

            FramePort? fport = null;
            var requiresDeviceAcknowlegement = false;

            byte[] frmPayload = null;

            // Get request Mac commands
            var macCommandsRequest = PrepareMacCommandAnswer(payload.MacCommands, request, loRaADRResult, logger);

            // Calculate request Mac commands size
            var macCommandsRequestSize = macCommandsRequest?.Sum(x => x.Length) ?? 0;

            // Try adding request Mac commands
            if (availablePayloadSize >= macCommandsRequestSize)
            {
                if (macCommandsRequest?.Count > 0)
                {
                    foreach (var macCommand in macCommandsRequest)
                    {
                        macCommands.Add(macCommand);
                    }
                }
            }

            if (fpending || isMessageTooLong)
            {
                fctrl |= FrameControlFlags.DownlinkFramePending;
            }

            if (payload.IsDataRateNetworkControlled)
            {
                fctrl |= FrameControlFlags.Adr;
            }

            var msgType = requiresDeviceAcknowlegement ? MacMessageType.ConfirmedDataDown : MacMessageType.UnconfirmedDataDown;
            var ackLoRaMessage = new LoRaPayloadData(
                msgType,
                payload.DevAddr,
                fctrl,
                (ushort)fcntDown,
                macCommands,
                fport,
                frmPayload,
                1,
                loRaDevice.Supports32BitFCnt ? fcntDown : null);

            // following calculation is making sure that ReportedRXDelay is chosen if not default
            var messageBytes = ackLoRaMessage.Serialize(loRaDevice.AppSKey.Value, loRaDevice.NwkSKey.Value);
            var downlinkMessage = new DownlinkMessage(
                messageBytes,
                radioMetadata.UpInfo.Xtime,
                receiveWindow is ReceiveWindow2 ? null : new ReceiveWindow(datr, freq),
                rx2,
                loRaDevice.DevEUI,
                loRaDevice.ReportedRXDelay,
                loRaDevice.ClassType,
                request.StationEui,
                radioMetadata.UpInfo.AntennaPreference
                );

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug($"{ackLoRaMessage.MessageType} {JsonConvert.SerializeObject(downlinkMessage)}");
            }

            return new DownlinkMessageBuilderResponse(downlinkMessage, isMessageTooLong, receiveWindow);
        }

        /// <summary>
        /// Prepare the Mac Commands to be sent in the downstream message.
        /// </summary>
        private static List<MacCommand> PrepareMacCommandAnswer(
           IEnumerable<MacCommand> requestedMacCommands,
           LoRaRequest loRaRequest,
           LoRaADRResult loRaADRResult,
           ILogger logger)
        {
            var cids = new HashSet<Cid>();
            var macCommands = new List<MacCommand>();

            if (requestedMacCommands != null)
            {
                foreach (var requestedMacCommand in requestedMacCommands)
                {
                    switch (requestedMacCommand.Cid)
                    {
                        case Cid.LinkCheckCmd:
                        case Cid.LinkADRCmd:
                            if (loRaRequest != null)
                            {
                                var linkCheckAnswer = new LinkCheckAnswer(checked((byte)loRaRequest.Region.GetModulationMargin(loRaRequest.RadioMetadata.DataRate, loRaRequest.RadioMetadata.UpInfo.SignalNoiseRatio)), 1);
                                if (cids.Add(Cid.LinkCheckCmd))
                                {
                                    macCommands.Add(linkCheckAnswer);
                                    logger.LogInformation($"answering to a MAC command request {linkCheckAnswer}");
                                }
                            }
                            break;
                        case Cid.DeviceTimeCmd:
                            if (loRaRequest != null)
                            {
                                var deviceTime = new DeviceTimeAnswer();
                                if (cids.Add(Cid.DeviceTimeCmd))
                                {
                                    macCommands.Add(deviceTime);
                                    logger.LogInformation($"answering to a MAC command request {deviceTime}");
                                }
                            }
                            break;
                        case Cid.DutyCycleCmd:
                        case Cid.RXParamCmd:
                        case Cid.DevStatusCmd:
                        case Cid.NewChannelCmd:
                        case Cid.RXTimingCmd:
                        case Cid.TxParamSetupCmd:
                        default:
                            break;
                    }
                }
            }

            // ADR Part.
            // Currently only replying on ADR Req
            if (loRaADRResult?.CanConfirmToDevice == true)
            {
                const int placeholderChannel = 25;
                var linkADR = new LinkADRRequest((byte)loRaADRResult.DataRate, (byte)loRaADRResult.TxPower, placeholderChannel, 0, (byte)loRaADRResult.NbRepetition);
                macCommands.Add(linkADR);
                logger.LogInformation($"performing a rate adaptation: DR {loRaADRResult.DataRate}, transmit power {loRaADRResult.TxPower}, #repetition {loRaADRResult.NbRepetition}");
            }

            return macCommands;
        }
    }
}
