// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using LoRaWANContainer.LoRaWan.NetworkServer;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models;
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
    using static LoRaWan.ReceiveWindowNumber;

    public class JoinRequestMessageHandler(ConcentratorDeduplication concentratorDeduplication,
                                           ILogger<JoinRequestMessageHandler> logger)
    {
        public void DispatchRequest(LoRaRequest request)
        {
            _ = Task.Run(() => ProcessJoinRequestAsync(request));
        }

        public LoRaDevice GetDeviceForJoinRequestAsync(DevEui devEUI)
        {
            if (!SearchDevicesResult.DeviceList.ContainsKey(devEUI.ToString()))
            {
                logger.LogInformation("join refused: no devices found matching join request");
                return null;
            }

            return new LoRaDevice(devEUI) { AppKey = AppKey.Parse(SearchDevicesResult.DeviceList[devEUI.ToString()]) };
        }

        internal async Task ProcessJoinRequestAsync(LoRaRequest request)
        {
            var joinReq = (LoRaPayloadJoinRequest)request.Payload;

            var devEui = joinReq.DevEUI;

            LoRaDevice loRaDevice = null;

            try
            {
                var timeWatcher = request.GetTimeWatcher();
                var processingTimeout = timeWatcher.GetRemainingTimeToJoinAcceptSecondWindow() - TimeSpan.FromMilliseconds(100);

                logger.LogInformation("join request received");

                var deduplicationResult = concentratorDeduplication.CheckDuplicateJoin(request);
                if (deduplicationResult is ConcentratorDeduplicationResult.Duplicate)
                {
                    request.NotifyFailed(devEui.ToString(), LoRaDeviceRequestFailedReason.DeduplicationDrop);
                    logger.LogInformation("join request dropped due to deduplication");
                    return;
                }

                loRaDevice = GetDeviceForJoinRequestAsync(devEui);
                if (loRaDevice == null)
                {
                    request.NotifyFailed(devEui.ToString(), LoRaDeviceRequestFailedReason.UnknownDevice);
                    logger.LogInformation("join request failed: device not found");
                    return;
                }

                loRaDevice.DevNonce = joinReq.DevNonce;
                loRaDevice.AppEui = joinReq.AppEui;

                var netId = new NetId(1);
                var appNonce = new AppNonce(RandomNumberGenerator.GetInt32(toExclusive: AppNonce.MaxValue + 1));
                var appSKey = OTAAKeysGenerator.CalculateAppSessionKey(appNonce, netId, joinReq.DevNonce, (AppKey)loRaDevice.AppKey);
                var nwkSKey = OTAAKeysGenerator.CalculateNetworkSessionKey(appNonce, netId, joinReq.DevNonce, (AppKey)loRaDevice.AppKey);
                var address = RandomNumberGenerator.GetInt32(toExclusive: DevAddr.MaxNetworkAddress + 1);
                // The 7 LBS of the NetID become the NwkID of a DevAddr:
                loRaDevice.DevAddr = new DevAddr(unchecked((byte)netId.NetworkId), address);

                if (!timeWatcher.InTimeForJoinAccept())
                {
                    // in this case it's too late, we need to break and avoid saving twins
                    logger.LogInformation("join refused: processing of the join request took too long, sending no message");
                    request.NotifyFailed(loRaDevice, LoRaDeviceRequestFailedReason.ReceiveWindowMissed);
                    return;
                }

                var updatedProperties = new LoRaDeviceJoinUpdateProperties
                {
                    DevAddr = loRaDevice.DevAddr,
                    NwkSKey = nwkSKey,
                    AppSKey = appSKey,
                    AppNonce = appNonce,
                    DevNonce = joinReq.DevNonce,
                    NetId = netId,
                    Region = request.Region.LoRaRegion
                };

                if (loRaDevice.ClassType == LoRaDeviceClassType.C)
                {
                    updatedProperties.SavePreferredGateway = true;
                    updatedProperties.SaveRegion = true;
                    updatedProperties.StationEui = request.StationEui;
                }

                DeviceJoinInfo deviceJoinInfo = null;
                if (request.Region.LoRaRegion == LoRaRegionType.CN470RP2)
                {
                    if (request.Region.TryGetJoinChannelIndex(request.RadioMetadata.Frequency, out var channelIndex))
                    {
                        updatedProperties.CN470JoinChannel = channelIndex;
                        deviceJoinInfo = new DeviceJoinInfo(channelIndex);
                    }
                    else
                    {
                        logger.LogError("failed to retrieve the join channel index for device");
                    }
                }

                await loRaDevice.ApplyDeviceSettings(updatedProperties).ConfigureAwait(false);

                var windowToUse = timeWatcher.ResolveJoinAcceptWindowToUse();
                if (windowToUse is null)
                {
                    logger.LogInformation("join refused: processing of the join request took too long, sending no message");
                    request.NotifyFailed(loRaDevice, LoRaDeviceRequestFailedReason.ReceiveWindowMissed);
                    return;
                }

                // check if the device is already joined
                bool deviceFound = false;
                foreach (KeyValuePair<StationEui, GatewayConnection> gateway in WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways)
                {
                    foreach (KeyValuePair<DevEui, LoRaDevice> device in gateway.Value.Devices)
                    {
                        // check if this is the same device that is already joined
                        if (device.Value.DevEUI != loRaDevice.DevEUI)
                        {
                            continue;
                        }
                        else
                        {
                            deviceFound = true;
                            if (device.Value.DevNonce == joinReq.DevNonce)
                            {
                                logger.LogError($"join refused: Device {loRaDevice.DevAddr} already joined and device nounce the same -> replay attack!");
                                request.NotifyFailed(loRaDevice, LoRaDeviceRequestFailedReason.JoinDevNonceAlreadyUsed);
                                return;
                            }
                            else
                            {
                                // replace the device with the one that is joining
                                WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways[gateway.Key].Devices[device.Key] = loRaDevice;
                                logger.LogInformation($"join request succeeded: device {loRaDevice.DevAddr} already joined, replacing with new device");
                            }
                        }
                    }
                }

                if (!deviceFound)
                {
                    // add the device to the connected gateways
                    WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways[request.StationEui].Devices.Add(loRaDevice.DevEUI, loRaDevice);
                    logger.LogInformation($"join request succeeded: device {loRaDevice.DevAddr} added to connected gateways");
                }

                // Build join accept downlink message
                // Build the DlSettings fields that is a superposition of RX2DR and RX1DROffset field
                var dlSettings = new byte[1];

                if (loRaDevice.DesiredRX2DataRate.HasValue)
                {
                    if (request.Region.DRtoConfiguration.ContainsKey(loRaDevice.DesiredRX2DataRate.Value))
                    {
                        dlSettings[0] = (byte)((byte)loRaDevice.DesiredRX2DataRate & 0b00001111);
                    }
                    else
                    {
                        logger.LogError("twin RX2 DR value is not within acceptable values");
                    }
                }

                if (request.Region.IsValidRX1DROffset(loRaDevice.DesiredRX1DROffset))
                {
                    var rx1droffset = (byte)(loRaDevice.DesiredRX1DROffset << 4);
                    dlSettings[0] = (byte)(dlSettings[0] + rx1droffset);
                }
                else
                {
                    logger.LogError("twin RX1 offset DR value is not within acceptable values");
                }

                // The following DesiredRxDelay is different than the RxDelay to be passed to Serialize function
                // This one is a delay between TX and RX for any message to be processed by joining device
                // The field accepted by Serialize method is an indication of the delay (compared to receive time of join request)
                // of when the message Join Accept message should be sent
                var loraSpecDesiredRxDelay = RxDelay.RxDelay0;
                if (Enum.IsDefined(loRaDevice.DesiredRXDelay))
                {
                    loraSpecDesiredRxDelay = loRaDevice.DesiredRXDelay;
                }
                else
                {
                    logger.LogError("twin RX delay value is not within acceptable values");
                }

                var loRaPayloadJoinAccept = new LoRaPayloadJoinAccept(
                    netId, // NETID 0 / 1 is default test
                    loRaDevice.DevAddr, // todo add device address management
                    appNonce,
                    dlSettings,
                    loraSpecDesiredRxDelay,
                    null);

                var loraRegion = request.Region;
                if (!loraRegion.TryGetDownstreamChannelFrequency(request.RadioMetadata.Frequency, upstreamDataRate: request.RadioMetadata.DataRate, deviceJoinInfo: deviceJoinInfo, downstreamFrequency: out var freq))
                {
                    logger.LogError("could not resolve DR and/or frequency for downstream");
                    request.NotifyFailed(loRaDevice, LoRaDeviceRequestFailedReason.InvalidUpstreamMessage);
                    return;
                }

                var joinAcceptBytes = loRaPayloadJoinAccept.Serialize((AppKey)loRaDevice.AppKey);

                // For join accept messages the RX1DROffsetvalue is ignored as the join accept message carry the settings to the device.
                var rx1 = windowToUse is not ReceiveWindow2
                        ? new ReceiveWindow(loraRegion.GetDownstreamDataRate(request.RadioMetadata.DataRate), freq)
                        : (ReceiveWindow?)null;

                var rx2 = new ReceiveWindow(loraRegion.GetDefaultRX2ReceiveWindow(deviceJoinInfo).DataRate,
                                            loraRegion.GetDefaultRX2ReceiveWindow(deviceJoinInfo).Frequency);

                var downlinkMessage = new DownlinkMessage(joinAcceptBytes,
                                                          request.RadioMetadata.UpInfo.Xtime,
                                                          rx1,
                                                          rx2,
                                                          loRaDevice.DevEUI,
                                                          loraRegion.JoinAcceptDelay1,
                                                          loRaDevice.ClassType,
                                                          request.StationEui,
                                                          request.RadioMetadata.UpInfo.AntennaPreference);

                _ = request.DownlinkMessageSender.SendDownlinkAsync(downlinkMessage);

                request.NotifySucceeded(loRaDevice, downlinkMessage);

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    var jsonMsg = JsonConvert.SerializeObject(downlinkMessage);
                    logger.LogDebug($"{MacMessageType.JoinAccept} {jsonMsg}");
                }
                else
                {
                    logger.LogInformation($"Join from {loRaDevice.DevEUI} via {request.StationEui} accepted", loRaDevice.DevEUI, request.StationEui);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"failed to handle join request. {ex.Message}", LogLevel.Error);

                request.NotifyFailed(loRaDevice, ex);
                throw;
            }
        }
    }
}
