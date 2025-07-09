// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using LoRaWANContainer.LoRaWan.NetworkServer;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Edge.Translator.ProtocolDrivers.LoRaWanNetworkServer.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Constants = LoRaWANContainer.LoRaWan.NetworkServer.Models.Constants;

    public class DataMessageHandler(
        LoRaADRInMemoryStore store,
        ConcentratorDeduplication concentratorDeduplication,
        ILogger<DataMessageHandler> logger)
    {
        public void ProcessData(LoRaRequest request)
        {
            // Unobserved task exceptions are logged as part of ProcessJoinRequestAsync.
            _ = Task.Run(() => ProcessDataAsync(request));
        }

        private async Task<LoRaDeviceRequestProcessResult> ProcessDataAsync(LoRaRequest request)
        {
            var timeWatcher = request.GetTimeWatcher();

            var loraPayload = (LoRaPayloadData)request.Payload;

            var payloadFcnt = loraPayload.Fcnt;

            DevEui devEui = new();
            foreach (KeyValuePair<DevEui, LoRaDevice> device in WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways[request.StationEui].Devices)
            {
                if (device.Value.DevAddr == request.Payload.DevAddr)
                {
                    devEui = device.Key;
                    break;
                }
            }

            LoRaDevice loRaDevice;
            if (WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways[request.StationEui].Devices.TryGetValue(devEui, out var value))
            {
                loRaDevice = value;
            }
            else
            {
                // Device not found, we cannot process the request.
                logger.LogDebug($"device with DevAddr {request.Payload.DevAddr} not found, ignoring message");
                request.NotifyFailed(LoRaDeviceRequestFailedReason.DeviceNotJoined);
                return new LoRaDeviceRequestProcessResult(null, request, LoRaDeviceRequestFailedReason.DeviceNotJoined);
            }

            var payloadFcntAdjusted = LoRaPayloadData.InferUpper32BitsForClientFcnt(payloadFcnt, loRaDevice.FCntUp);
            logger.LogDebug($"converted 16bit FCnt {payloadFcnt} to 32bit FCnt {payloadFcntAdjusted}");

            var requiresConfirmation = loraPayload.RequiresConfirmation;

            LoRaADRResult loRaADRResult = null;

            var frameCounterStrategy = new FrameCounterUpdateStrategy();
            var skipDownstreamToAvoidCollisions = false;
            var concentratorDeduplicationResult = concentratorDeduplication.CheckDuplicateData(request, loRaDevice);
            if (concentratorDeduplicationResult is ConcentratorDeduplicationResult.Duplicate)
            {
                return new LoRaDeviceRequestProcessResult(loRaDevice, request, LoRaDeviceRequestFailedReason.DeduplicationDrop);
            }
            else if (concentratorDeduplicationResult is ConcentratorDeduplicationResult.SoftDuplicateDueToDeduplicationStrategy)
            {
                // Request is allowed upstream but confirmation is skipped to avoid sending the answer to the device multiple times and potentially cause collisions on the air.
                skipDownstreamToAvoidCollisions = true;
            }

            // Leaf devices that restart lose the counter. In relax mode we accept the incoming frame counter
            // ABP device does not reset the Fcnt so in relax mode we should reset for 0 (LMIC based) or 1
            var isFrameCounterFromNewlyStartedDevice = await DetermineIfFramecounterIsFromNewlyStartedDeviceAsync(loRaDevice, payloadFcntAdjusted, frameCounterStrategy, concentratorDeduplicationResult).ConfigureAwait(false);

            // Reply attack or confirmed reply
            // Confirmed resubmit: A confirmed message that was received previously but we did not answer in time
            // Device will send it again and we just need to return an ack (but also check for C2D to send it over)
            if (ValidateRequest(loraPayload, isFrameCounterFromNewlyStartedDevice, payloadFcntAdjusted, loRaDevice, concentratorDeduplicationResult,
                                out var isConfirmedResubmit) is { } someFailedReason)
            {
                return new LoRaDeviceRequestProcessResult(loRaDevice, request, someFailedReason);
            }

            var fcntResetSaved = false;

            try
            {
                loRaADRResult = null;

                #region ADR
                if (loraPayload.IsAdrAckRequested)
                {
                    logger.LogDebug("ADR ack request received");
                }

                // ADR should be performed before the gateway deduplication as we still want to collect the signal info,
                // even if we drop it in the next step.
                // ADR is skipped for soft duplicates and will be enabled again in https://github.com/Azure/iotedge-lorawan-starterkit/issues/1017
                if (loRaADRResult == null && loraPayload.IsDataRateNetworkControlled && concentratorDeduplicationResult is not ConcentratorDeduplicationResult.SoftDuplicateDueToDeduplicationStrategy)
                {
                    loRaADRResult = await PerformADR(request, loRaDevice, loraPayload, payloadFcntAdjusted, loRaADRResult, frameCounterStrategy).ConfigureAwait(false);
                }
                #endregion

                if (loRaADRResult?.CanConfirmToDevice == true || loraPayload.IsAdrAckRequested)
                {
                    // if we got an ADR result or request, we have to send the update to the device
                    requiresConfirmation = true;
                }

                // we must save class C devices regions in order to send c2d messages
                if (loRaDevice.ClassType == LoRaDeviceClassType.C && request.Region.LoRaRegion != loRaDevice.LoRaRegion)
                    loRaDevice.UpdateRegion(request.Region.LoRaRegion, acceptChanges: false);

                loRaDevice.IsConnectionOwner = true;

                // saving fcnt reset changes
                if (isFrameCounterFromNewlyStartedDevice)
                {
                    await SaveChangesToDeviceAsync(loRaDevice, isFrameCounterFromNewlyStartedDevice).ConfigureAwait(false);
                    fcntResetSaved = true;
                }

                #region FrameCounterDown
                var fcntDown = loRaADRResult?.FCntDown;
                LogNotNullFrameCounterDownState(loRaDevice, fcntDown);

                // If we can send message downstream, we need to update the frame counter down
                if (requiresConfirmation && !skipDownstreamToAvoidCollisions)
                {
                    fcntDown = await EnsureHasFcntDownAsync(loRaDevice, fcntDown, payloadFcntAdjusted, frameCounterStrategy).ConfigureAwait(false);

                    var result = HandleFrameCounterDownResult(fcntDown, loRaDevice, ref skipDownstreamToAvoidCollisions);

                    if (result != null)
                        return new LoRaDeviceRequestProcessResult(loRaDevice, request, result.Value);
                }
                #endregion

                var canSendUpstream = isFrameCounterFromNewlyStartedDevice
                                  || payloadFcntAdjusted > loRaDevice.FCntUp
                                  || (payloadFcntAdjusted == loRaDevice.FCntUp && concentratorDeduplicationResult is ConcentratorDeduplicationResult.SoftDuplicateDueToDeduplicationStrategy);
                if (canSendUpstream || isConfirmedResubmit)
                {
                    if (!isConfirmedResubmit)
                    {
                        logger.LogDebug($"valid frame counter, msg: {payloadFcntAdjusted} server: {loRaDevice.FCntUp}");
                    }

                    byte[] decryptedPayloadData = null;
                    if (loraPayload.Frmpayload.Length > 0)
                    {
                        try
                        {
                            decryptedPayloadData = (loraPayload.Fport == FramePort.MacCommand)?
                                loraPayload.GetDecryptedPayload(loRaDevice.NwkSKey ?? throw new LoRaProcessingException("No NwkSKey set for the LoRaDevice.", LoRaProcessingErrorCode.PayloadDecryptionFailed))
                              : loraPayload.GetDecryptedPayload(loRaDevice.AppSKey ?? throw new LoRaProcessingException("No AppSKey set for the LoRaDevice.", LoRaProcessingErrorCode.PayloadDecryptionFailed));
                        }
                        catch (LoRaProcessingException ex) when (ex.ErrorCode == LoRaProcessingErrorCode.PayloadDecryptionFailed)
                        {
                            logger.LogError(ex.ToString());
                        }
                    }

                    #region Handling MacCommands
                    // if FPort is 0 (i.e. MacCommand) the commands are in the payload
                    // otherwise the commands are in FOpts field and already parsed
                    if (loraPayload.Fport == FramePort.MacCommand && decryptedPayloadData?.Length > 0)
                    {
                        loraPayload.MacCommands = MacCommand.CreateMacCommandFromBytes(decryptedPayloadData, logger);
                    }

                    if (loraPayload.MacCommands is { Count: > 0 } macCommands)
                    {
                        foreach (var macCommand in macCommands)
                        {
                            logger.LogDebug($"{macCommand.Cid} mac command detected in upstream payload: {macCommand}");
                        }
                    }

                    if (!skipDownstreamToAvoidCollisions && loraPayload.IsMacAnswerRequired)
                    {
                        fcntDown = await EnsureHasFcntDownAsync(loRaDevice, fcntDown, payloadFcntAdjusted, frameCounterStrategy).ConfigureAwait(false);

                        var result = HandleFrameCounterDownResult(fcntDown, loRaDevice, ref skipDownstreamToAvoidCollisions);

                        if (result != null)
                            return new LoRaDeviceRequestProcessResult(loRaDevice, request, result.Value);

                        requiresConfirmation = true;
                    }

                    // Persist dwell time settings in device reported properties
                    if (loraPayload.MacCommands is not null && loraPayload.MacCommands.Any(m => m.Cid == Cid.TxParamSetupCmd))
                    {
                        if (request.Region is DwellTimeLimitedRegion someRegion)
                        {
                            if (someRegion.DesiredDwellTimeSetting != loRaDevice.ReportedDwellTimeSetting)
                            {
                                loRaDevice.UpdateDwellTimeSetting(someRegion.DesiredDwellTimeSetting, acceptChanges: false);
                                _ = await loRaDevice.SaveChangesAsync(force: true).ConfigureAwait(false);
                            }
                            else
                            {
                                logger.LogDebug("Received 'TxParamSetupAns' even though reported dwell time settings match desired dwell time settings.");
                            }
                        }
                        else
                        {
                            logger.LogWarning("Received 'TxParamSetupAns' in region '{Region}' which does not support dwell limitations.", request.Region.LoRaRegion);
                        }
                    }

                    #endregion
                    if (loraPayload.Fport is { } payloadPort and not FramePort.MacCommand)
                    {
                        // store the last known decoded payload with the device
                        if (loRaDevice.LastKnownDecodedPayloads.ContainsKey(decryptedPayloadData.Length))
                        {
                            loRaDevice.LastKnownDecodedPayloads[decryptedPayloadData.Length] = new LoRaDevice.ReceivedPayload() {
                                Payload = decryptedPayloadData,
                                Timestamp = DateTime.UtcNow
                            };
                        }
                        else
                        {
                            loRaDevice.LastKnownDecodedPayloads.Add(decryptedPayloadData.Length, new LoRaDevice.ReceivedPayload() {
                                Payload = decryptedPayloadData,
                                Timestamp = DateTime.UtcNow
                            });
                        }
                    }

                    if (request.Region is DwellTimeLimitedRegion someDwellTimeLimitedRegion
                        && loRaDevice.ReportedDwellTimeSetting != someDwellTimeLimitedRegion.DesiredDwellTimeSetting)
                    {
                        logger.LogDebug("Preparing 'TxParamSetupReq' MAC command downstream.");

                        loraPayload.MacCommands.Add(new TxParamSetupRequest(someDwellTimeLimitedRegion.DesiredDwellTimeSetting));
                        fcntDown = await EnsureHasFcntDownAsync(loRaDevice, fcntDown, payloadFcntAdjusted, frameCounterStrategy).ConfigureAwait(false);
                        requiresConfirmation = true;
                    }

                    loRaDevice.SetFcntUp(payloadFcntAdjusted);
                }

                #region Downstream
                if (skipDownstreamToAvoidCollisions)
                {
                    logger.LogDebug($"skipping downstream messages due to deduplication ({timeWatcher.GetElapsedTime()})");
                    return new LoRaDeviceRequestProcessResult(loRaDevice, request);
                }

                // We check if we have time to futher progress or not
                // C2D checks are quite expensive so if we are really late we just stop here
                var timeToSecondWindow = timeWatcher.GetRemainingTimeToReceiveSecondWindow(loRaDevice);
                if (timeToSecondWindow < LoRaOperationTimeWatcher.ExpectedTimeToPackageAndSendMessage)
                {
                    if (requiresConfirmation)
                    {
                        logger.LogInformation($"too late for down message ({timeWatcher.GetElapsedTime()})");
                    }

                    return new LoRaDeviceRequestProcessResult(loRaDevice, request);
                }

                // If it is confirmed and
                // - Downlink is disabled for the device or
                // - we don't have time to check c2d and send to device we return now
                if (requiresConfirmation && loRaDevice.DownlinkEnabled)
                {
                    var downlinkMessageBuilderResp = DownlinkMessageBuilder.CreateDownlinkMessage(
                        loRaDevice,
                        request,
                        timeWatcher,
                        loraPayload,
                        false, // fpending
                        fcntDown.GetValueOrDefault(),
                        loRaADRResult,
                        logger);

                    if (downlinkMessageBuilderResp.DownlinkMessage != null)
                    {
                        await request.DownlinkMessageSender.SendDownlinkAsync(downlinkMessageBuilderResp.DownlinkMessage).ConfigureAwait(false);
                    }
                    else
                    {
                        logger.LogInformation($"out of time for downstream message, will abandon device message");
                    }

                    return new LoRaDeviceRequestProcessResult(loRaDevice, request, downlinkMessageBuilderResp.DownlinkMessage);
                }

                if (loRaDevice.ClassType is LoRaDeviceClassType.C && loRaDevice.LastProcessingStationEui != request.StationEui)
                {
                    loRaDevice.SetLastProcessingStationEui(request.StationEui);
                }

                return new LoRaDeviceRequestProcessResult(loRaDevice, request);
                #endregion
            }
            finally
            {
                if (loRaDevice.IsConnectionOwner is true)
                {
                    await SaveChangesToDeviceAsync(loRaDevice, isFrameCounterFromNewlyStartedDevice && !fcntResetSaved);
                }
            }
        }

        internal virtual async Task SaveChangesToDeviceAsync(LoRaDevice loRaDevice, bool force)
        {
            _ = loRaDevice ?? throw new ArgumentNullException(nameof(loRaDevice));

            _ = await loRaDevice.SaveChangesAsync(force: force).ConfigureAwait(false);
        }

        /// <summary>
        /// Helper method to resolve FcntDown in case one was not yet acquired.
        /// </summary>
        /// <returns>0 if the resolution failed or > 0 if a valid frame count was acquired.</returns>
        private async ValueTask<uint> EnsureHasFcntDownAsync(
            LoRaDevice loRaDevice,
            uint? fcntDown,
            uint payloadFcnt,
            FrameCounterUpdateStrategy frameCounterStrategy)
        {
            if (fcntDown.HasValue)
            {
                return fcntDown.Value;
            }

            var newFcntDown = await frameCounterStrategy.NextFcntDown(loRaDevice, payloadFcnt).ConfigureAwait(false);
            LogNotNullFrameCounterDownState(loRaDevice, newFcntDown);

            return newFcntDown;
        }

        private void LogNotNullFrameCounterDownState(LoRaDevice loRaDevice, uint? newFcntDown)
        {
            if (!newFcntDown.HasValue)
                return;

            if (newFcntDown <= 0)
            {
                logger.LogDebug("another gateway has already sent ack or downlink msg");
            }
            else
            {
                logger.LogDebug($"down frame counter: {loRaDevice.FCntDown}");
            }
        }

        /// <summary>
        /// Handles the result of frame counter down, depending on the <code>DeduplicationMode</code> used.
        /// Specifically, for invalid frame counter down:
        /// - when mode is Drop, we do not send the message upstream nor downstream
        /// - when mode is Mark or None, we allow upstream but skip downstream to avoid collisions
        /// </summary>
        /// <param name="skipDownstreamToAvoidCollisions">boolean that is used while deciding to send messages downstream</param>
        /// <returns><code>LoRaDeviceRequestFailedReason</code> when Drop, otherwise null</returns>
        private static LoRaDeviceRequestFailedReason? HandleFrameCounterDownResult(uint? fcntDown, LoRaDevice loRaDevice, ref bool skipDownstreamToAvoidCollisions)
        {
            LoRaDeviceRequestFailedReason? result = null;

            if (fcntDown <= 0)
            {
                // Failed to update the fcnt down:
                // This can only happen in multi gateway scenarios and
                // it means that another gateway has won the race to handle this message.
                if (loRaDevice.Deduplication == DeduplicationMode.Drop)
                    result = LoRaDeviceRequestFailedReason.HandledByAnotherGateway;
                else
                    skipDownstreamToAvoidCollisions = true;
            }

            return result;
        }

        protected virtual async Task<LoRaADRResult> PerformADR(LoRaRequest request, LoRaDevice loRaDevice, LoRaPayloadData loraPayload, uint payloadFcnt, LoRaADRResult loRaADRResult, FrameCounterUpdateStrategy frameCounterStrategy)
        {
            _ = loRaDevice ?? throw new ArgumentNullException(nameof(loRaDevice));
            _ = request ?? throw new ArgumentNullException(nameof(request));
            _ = loraPayload ?? throw new ArgumentNullException(nameof(loraPayload));

            // find our gateway id
            string gatewayId = string.Empty;
            foreach (KeyValuePair<StationEui, GatewayConnection> gatewayConnection in WebsocketJsonMiddlewareLoRaWAN.ConnectedGateways)
            {
                if (gatewayConnection.Value.Devices.ContainsKey(loRaDevice.DevEUI))
                {
                    gatewayId = gatewayConnection.Key.ToString();
                }
            }

            var loRaADRManager = new LoRaADRDefaultManager(store, frameCounterStrategy, loRaDevice);

            var loRaADRTableEntry = new LoRaADRTableEntry()
            {
                DevEUI = loRaDevice.DevEUI,
                FCnt = payloadFcnt,
                GatewayId = gatewayId,
                Snr = request.RadioMetadata.UpInfo.SignalNoiseRatio
            };

            // If the ADR req bit is not set we don't perform rate adaptation.
            if (!loraPayload.IsAdrAckRequested)
            {
                _ = loRaADRManager.StoreADREntryAsync(loRaADRTableEntry);
            }
            else
            {
                loRaADRResult = await loRaADRManager.CalculateADRResultAndAddEntryAsync(
                    loRaDevice.DevEUI,
                    gatewayId,
                    payloadFcnt,
                    loRaDevice.FCntDown,
                    (float)request.Region.RequiredSnr(request.RadioMetadata.DataRate),
                    request.RadioMetadata.DataRate,
                    request.Region.TXPowertoMaxEIRP.Count - 1,
                    request.Region.MaxADRDataRate,
                    loRaADRTableEntry).ConfigureAwait(false);
                logger.LogDebug("device sent ADR ack request, computing an answer");
            }

            return loRaADRResult;
        }

        /// <summary>
        /// Checks if a request is valid and flags whether it's a confirmation resubmit.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="isFrameCounterFromNewlyStartedDevice"></param>
        /// <param name="payloadFcnt"></param>
        /// <param name="loRaDevice"></param>
        /// <param name="isConfirmedResubmit"><code>True</code> when it's a confirmation resubmit.</param>
        /// <returns><code>LoRaDeviceRequestFailedReason</code> when the provided request is
        /// invalid, otherwise null.</returns>
        internal virtual LoRaDeviceRequestFailedReason? ValidateRequest(LoRaPayloadData payload, bool isFrameCounterFromNewlyStartedDevice, uint payloadFcnt, LoRaDevice loRaDevice, ConcentratorDeduplicationResult concentratorDeduplicationResult, out bool isConfirmedResubmit)
        {
            isConfirmedResubmit = false;

            if (!isFrameCounterFromNewlyStartedDevice && payloadFcnt <= loRaDevice.FCntUp)
            {
                // most probably we did not ack in time before or device lost the ack packet so we should continue but not send the msg to iothub
                if (payload.RequiresConfirmation && payloadFcnt == loRaDevice.FCntUp && (concentratorDeduplicationResult is ConcentratorDeduplicationResult.NotDuplicate or ConcentratorDeduplicationResult.DuplicateDueToResubmission))
                {
                    if (!loRaDevice.ValidateConfirmResubmit(payloadFcnt))
                    {
                        logger.LogError($"resubmit from confirmed message exceeds threshold of {LoRaDevice.MaxConfirmationResubmitCount}, message ignored, msg: {payloadFcnt} server: {loRaDevice.FCntUp}");
                        return LoRaDeviceRequestFailedReason.ConfirmationResubmitThresholdExceeded;
                    }

                    isConfirmedResubmit = true;
                    logger.LogInformation($"resubmit from confirmed message detected, msg: {payloadFcnt} server: {loRaDevice.FCntUp}");
                }
                else if (payloadFcnt == loRaDevice.FCntUp && concentratorDeduplicationResult == ConcentratorDeduplicationResult.SoftDuplicateDueToDeduplicationStrategy)
                {
                    // multi concentrator receive, with dedup strategy to send upstream
                    return null;
                }
                else
                {
                    logger.LogDebug($"invalid frame counter, message ignored, msg: {payloadFcnt} server: {loRaDevice.FCntUp}");
                    return LoRaDeviceRequestFailedReason.InvalidFrameCounter;
                }
            }

            // ensuring the framecount difference between the node and the server
            // is <= MAX_FCNT_GAP
            var diff = payloadFcnt > loRaDevice.FCntUp ? payloadFcnt - loRaDevice.FCntUp : loRaDevice.FCntUp - payloadFcnt;

            if (diff > Constants.MaxFcntGap)
            {
                logger.LogError($"invalid frame counter (diverges too much), message ignored, msg: {payloadFcnt} server: {loRaDevice.FCntUp}");
                return LoRaDeviceRequestFailedReason.InvalidFrameCounter;
            }

            return null; // no failure reason == success
        }

        private async Task<bool> DetermineIfFramecounterIsFromNewlyStartedDeviceAsync(
            LoRaDevice loRaDevice,
            uint payloadFcnt,
            FrameCounterUpdateStrategy frameCounterStrategy,
            ConcentratorDeduplicationResult concentratorDeduplicationResult)
        {
            _ = loRaDevice ?? throw new ArgumentNullException(nameof(loRaDevice));
            _ = frameCounterStrategy ?? throw new ArgumentNullException(nameof(frameCounterStrategy));

            var isFrameCounterFromNewlyStartedDevice = false;
            if (payloadFcnt <= 1)
            {
                if (loRaDevice.IsABP)
                {
                    if (loRaDevice.IsABPRelaxedFrameCounter && loRaDevice.FCntUp >= 0 && payloadFcnt <= 1)
                    {
                        // known problem when device restarts, starts fcnt from zero
                        // We need to await this reset to avoid races on the server with deduplication and
                        // fcnt down calculations
                        if (concentratorDeduplicationResult is ConcentratorDeduplicationResult.NotDuplicate)
                        {
                            _ = await frameCounterStrategy.ResetAsync(loRaDevice).ConfigureAwait(false);
                        }
                        isFrameCounterFromNewlyStartedDevice = true;
                    }
                }
                else if (loRaDevice.FCntUp == payloadFcnt && payloadFcnt == 0)
                {
                    // Some devices start with frame count 0
                    isFrameCounterFromNewlyStartedDevice = true;
                }
            }

            return isFrameCounterFromNewlyStartedDevice;
        }
    }
}
