// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Constants = LoRaWANContainer.LoRaWan.NetworkServer.Models.Constants;

    public class DefaultLoRaDataRequestHandler(
        NetworkServerConfiguration configuration,
        ILoRaDeviceFrameCounterUpdateStrategyProvider frameCounterUpdateStrategyProvider,
        IConcentratorDeduplication concentratorDeduplication,
        ILoRaPayloadDecoder payloadDecoder,
        ILoRaADRStrategyProvider loRaADRStrategyProvider,
        ILoRAADRManagerFactory loRaADRManagerFactory,
        ILogger<DefaultLoRaDataRequestHandler> logger,
        Meter meter) : ILoRaDataRequestHandler
    {
        private readonly Counter<int> receiveWindowMissed = meter?.CreateCounter<int>(MetricRegistry.ReceiveWindowMisses);
        private readonly Counter<int> receiveWindowHits = meter?.CreateCounter<int>(MetricRegistry.ReceiveWindowHits);
        private readonly Histogram<int> d2cPayloadSizeHistogram = meter?.CreateHistogram<int>(MetricRegistry.D2CMessageSize);
        private readonly Counter<int> c2dMessageTooLong = meter?.CreateCounter<int>(MetricRegistry.C2DMessageTooLong);
        private IClassCDeviceMessageSender classCDeviceMessageSender;

        private sealed class ProcessingState(LoRaDevice device)
        {
            private List<Task> secondaryTasks;

            public ICollection<Task> SecondaryTasks =>
                (ICollection<Task>)this.secondaryTasks ?? Array.Empty<Task>();

            public void Track(Task task)
            {
                this.secondaryTasks ??= new List<Task>();
                this.secondaryTasks.Add(task);
            }

            public IAsyncDisposable DeviceConnectionActivity { get; private set; }

            public bool BeginDeviceClientConnectionActivity()
            {
                if (DeviceConnectionActivity is not null)
                    throw new InvalidOperationException();
                var activity = device.BeginDeviceClientConnectionActivity();
                if (activity is null)
                    return false;
                DeviceConnectionActivity = activity;
                return true;
            }
        }

        public async Task<LoRaDeviceRequestProcessResult> ProcessRequestAsync(LoRaRequest request, LoRaDevice loRaDevice)
        {
            ArgumentNullException.ThrowIfNull(request, nameof(request));
            ArgumentNullException.ThrowIfNull(loRaDevice, nameof(loRaDevice));

            var processingState = new ProcessingState(loRaDevice);

            try
            {
                return await ProcessRequestAsync(request, loRaDevice, processingState).ConfigureAwait(false);
            }
            finally
            {
                if (processingState.SecondaryTasks is { Count: > 0 } secondaryTasks)
                {
                    _ = await Task.WhenAny(Task.WhenAll(secondaryTasks)).ConfigureAwait(false);

                    var exception = secondaryTasks.GetExceptions() switch
                    {
                        { Count: 1 } exs => exs[0],
                        { Count: > 1 } exs => new AggregateException(exs),
                        _ => null
                    };

                    if (exception is { } someException)
                        logger.LogError(someException, "Processing of secondary tasks failed.");
                }
            }
        }

        private async Task<LoRaDeviceRequestProcessResult> ProcessRequestAsync(LoRaRequest request, LoRaDevice loRaDevice,
                                                                               ProcessingState state)
        {
            var timeWatcher = request.GetTimeWatcher();

            var loraPayload = (LoRaPayloadData)request.Payload;
            this.d2cPayloadSizeHistogram?.Record(loraPayload.Frmpayload.Length);

            var payloadFcnt = loraPayload.Fcnt;

            var payloadFcntAdjusted = LoRaPayloadData.InferUpper32BitsForClientFcnt(payloadFcnt, loRaDevice.FCntUp);
            logger.LogDebug($"converted 16bit FCnt {payloadFcnt} to 32bit FCnt {payloadFcntAdjusted}");

            var requiresConfirmation = loraPayload.RequiresConfirmation;

            LoRaADRResult loRaADRResult = null;

            var frameCounterStrategy = frameCounterUpdateStrategyProvider.GetStrategy(loRaDevice.GatewayID);
            if (frameCounterStrategy == null)
            {
                logger.LogError($"failed to resolve frame count update strategy, device gateway: {loRaDevice.GatewayID}, message ignored");
                return new LoRaDeviceRequestProcessResult(loRaDevice, request, LoRaDeviceRequestFailedReason.ApplicationError);
            }

            ReceivedLoRaCloudToDeviceMessage cloudToDeviceMessage = null;

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

            var useMultipleGateways = string.IsNullOrEmpty(loRaDevice.GatewayID);
            var fcntResetSaved = false;

            try
            {
                #region FunctionBundler
                FunctionBundlerResult bundlerResult = null;
                if (useMultipleGateways
                    && concentratorDeduplicationResult is ConcentratorDeduplicationResult.NotDuplicate
                                                       or ConcentratorDeduplicationResult.DuplicateDueToResubmission)
                {
                    // in the case of resubmissions we need to contact the function to get a valid frame counter down
                    throw new NotImplementedException("Azure Function is not supported anymore!");

                    //if (CreateBundler(loraPayload, loRaDevice, request) is { } bundler)
                    //{
                    //    if (loRaDevice.IsConnectionOwner is false && IsProcessingDelayEnabled())
                    //    {
                    //        await DelayProcessing().ConfigureAwait(false);
                    //    }
                    //    bundlerResult = await TryUseBundler(bundler, loRaDevice).ConfigureAwait(false);
                    //}
                }
                #endregion

                loRaADRResult = bundlerResult?.AdrResult;

                if (bundlerResult?.PreferredGatewayResult != null)
                {
                    HandlePreferredGatewayChanges(request, loRaDevice, bundlerResult);
                }

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

                if (useMultipleGateways)
                {
                    // applying the correct deduplication
                    if (bundlerResult?.DeduplicationResult != null && !bundlerResult.DeduplicationResult.CanProcess)
                    {
                        if (IsProcessingDelayEnabled())
                        {
                            loRaDevice.IsConnectionOwner = false;
                            loRaDevice.CloseConnection(CancellationToken.None);
                        }
                        // duplication strategy is indicating that we do not need to continue processing this message
                        logger.LogDebug($"duplication strategy indicated to not process message: {payloadFcnt}");
                        return new LoRaDeviceRequestProcessResult(loRaDevice, request, LoRaDeviceRequestFailedReason.DeduplicationDrop);
                    }
                }
                else
                {
                    // we must save class C devices regions in order to send c2d messages
                    if (loRaDevice.ClassType == LoRaDeviceClassType.C && request.Region.LoRaRegion != loRaDevice.LoRaRegion)
                        loRaDevice.UpdateRegion(request.Region.LoRaRegion, acceptChanges: false);
                }

                loRaDevice.IsConnectionOwner = true;
                if (!state.BeginDeviceClientConnectionActivity())
                {
                    return new LoRaDeviceRequestProcessResult(loRaDevice, request, LoRaDeviceRequestFailedReason.DeviceClientConnectionFailed);
                }

                // saving fcnt reset changes
                if (isFrameCounterFromNewlyStartedDevice)
                {
                    await SaveChangesToDeviceAsync(loRaDevice, isFrameCounterFromNewlyStartedDevice).ConfigureAwait(false);
                    fcntResetSaved = true;
                }

                #region FrameCounterDown
                // if deduplication already processed the next framecounter down, use that
                var fcntDown = loRaADRResult?.FCntDown != null ? loRaADRResult.FCntDown : bundlerResult?.NextFCntDown;
                LogNotNullFrameCounterDownState(loRaDevice, fcntDown);

                // If we can send message downstream, we need to update the frame counter down
                // Multiple gateways: in redis, otherwise in device twin
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

                    object payloadData = null;
                    byte[] decryptedPayloadData = null;

                    if (loraPayload.Frmpayload.Length > 0)
                    {
                        try
                        {
                            decryptedPayloadData = loraPayload.Fport == FramePort.MacCommand
                                ? loraPayload.GetDecryptedPayload(loRaDevice.NwkSKey ?? throw new LoRaProcessingException("No NwkSKey set for the LoRaDevice.", LoRaProcessingErrorCode.PayloadDecryptionFailed))
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
                        if (string.IsNullOrEmpty(loRaDevice.SensorDecoder))
                        {
                            logger.LogDebug($"no decoder set in device twin. port: {(byte)payloadPort}");
                            payloadData = new UndecodedPayload(decryptedPayloadData);
                        }
                        else
                        {
                            logger.LogDebug($"decoding with: {loRaDevice.SensorDecoder} port: {(byte)payloadPort}");
                            var decodePayloadResult = await payloadDecoder.DecodeMessageAsync(loRaDevice.DevEUI, decryptedPayloadData, payloadPort, loRaDevice.SensorDecoder).ConfigureAwait(false);
                            payloadData = decodePayloadResult.GetDecodedPayload();

                            if (decodePayloadResult.CloudToDeviceMessage != null)
                            {
                                if (decodePayloadResult.CloudToDeviceMessage.DevEUI is null || loRaDevice.DevEUI == decodePayloadResult.CloudToDeviceMessage.DevEUI)
                                {
                                    // sending c2d to same device
                                    cloudToDeviceMessage = decodePayloadResult.CloudToDeviceMessage;
                                    fcntDown = await EnsureHasFcntDownAsync(loRaDevice, fcntDown, payloadFcntAdjusted, frameCounterStrategy).ConfigureAwait(false);

                                    if (!fcntDown.HasValue || fcntDown <= 0)
                                    {
                                        // We did not get a valid frame count down, therefore we should not process the message
                                        state.Track(cloudToDeviceMessage.AbandonAsync());

                                        cloudToDeviceMessage = null;
                                    }
                                    else
                                    {
                                        requiresConfirmation = true;
                                    }
                                }
                                else
                                {
                                    if (this.classCDeviceMessageSender != null)
                                    {
                                        state.Track(this.classCDeviceMessageSender.SendAsync(decodePayloadResult.CloudToDeviceMessage));
                                    }
                                }
                            }
                        }
                    }

                    if (request.Region is DwellTimeLimitedRegion someDwellTimeLimitedRegion
                        && loRaDevice.ReportedDwellTimeSetting != someDwellTimeLimitedRegion.DesiredDwellTimeSetting
                        && cloudToDeviceMessage == null)
                    {
                        logger.LogDebug("Preparing 'TxParamSetupReq' MAC command downstream.");
                        // put the MAC command into a C2D message.
                        cloudToDeviceMessage = new ReceivedLoRaCloudToDeviceMessage() { MacCommands = { new TxParamSetupRequest(someDwellTimeLimitedRegion.DesiredDwellTimeSetting) } };
                        fcntDown = await EnsureHasFcntDownAsync(loRaDevice, fcntDown, payloadFcntAdjusted, frameCounterStrategy).ConfigureAwait(false);
                        requiresConfirmation = true;
                    }

                    var sendUpstream = concentratorDeduplicationResult is ConcentratorDeduplicationResult.NotDuplicate || loRaDevice.Deduplication is not DeduplicationMode.Drop;

                    // We send it to the IoT Hub:
                    // - when it's a new message or it's a resubmission/duplicate but with a strategy that is not drop
                    // - and it's not a MAC command
                    if (sendUpstream && loraPayload.Fport != FramePort.MacCommand)
                    {
                        // combine the results of the 2 deduplications: on the concentrator level and on the network server layer
                        var isDuplicate = concentratorDeduplicationResult is not ConcentratorDeduplicationResult.NotDuplicate || (bundlerResult?.DeduplicationResult?.IsDuplicate ?? false);
                        if (!await SendDeviceEventAsync(request, loRaDevice, timeWatcher, payloadData, isDuplicate, decryptedPayloadData).ConfigureAwait(false))
                        {
                            // failed to send event to IoT Hub, stop now
                            return new LoRaDeviceRequestProcessResult(loRaDevice, request, LoRaDeviceRequestFailedReason.IoTHubProblem);
                        }
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
                        this.receiveWindowMissed?.Add(1);
                        logger.LogInformation($"too late for down message ({timeWatcher.GetElapsedTime()})");
                    }

                    return new LoRaDeviceRequestProcessResult(loRaDevice, request);
                }

                // If it is confirmed and
                // - Downlink is disabled for the device or
                // - we don't have time to check c2d and send to device we return now
                if (requiresConfirmation && (!loRaDevice.DownlinkEnabled || timeToSecondWindow.Subtract(LoRaOperationTimeWatcher.ExpectedTimeToPackageAndSendMessage) <= LoRaOperationTimeWatcher.MinimumAvailableTimeToCheckForCloudMessage))
                {
                    var downlinkMessageBuilderResp = DownlinkMessageBuilder.CreateDownlinkMessage(
                        configuration,
                        loRaDevice,
                        request,
                        timeWatcher,
                        cloudToDeviceMessage,
                        false, // fpending
                        fcntDown.GetValueOrDefault(),
                        loRaADRResult,
                        logger);

                    if (downlinkMessageBuilderResp.DownlinkMessage != null)
                    {
                        this.receiveWindowHits?.Add(1, KeyValuePair.Create(MetricRegistry.ReceiveWindowTagName, (object)downlinkMessageBuilderResp.ReceiveWindow));
                        await request.DownstreamMessageSender.SendDownstreamAsync(downlinkMessageBuilderResp.DownlinkMessage).ConfigureAwait(false);

                        if (cloudToDeviceMessage != null)
                        {
                            if (downlinkMessageBuilderResp.IsMessageTooLong)
                            {
                                this.c2dMessageTooLong?.Add(1);
                                _ = await cloudToDeviceMessage.AbandonAsync().ConfigureAwait(false);
                            }
                            else
                            {
                                _ = await cloudToDeviceMessage.CompleteAsync().ConfigureAwait(false);
                            }
                        }
                    }

                    return new LoRaDeviceRequestProcessResult(loRaDevice, request, downlinkMessageBuilderResp.DownlinkMessage);
                }

                // Flag indicating if there is another C2D message waiting
                var fpending = false;

                // If downlink is enabled and we did not get a cloud to device message from decoder
                // try to get one from IoT Hub C2D
                if (loRaDevice.DownlinkEnabled && cloudToDeviceMessage == null)
                {
                    // ReceiveAsync has a longer timeout
                    // But we wait less that the timeout (available time before 2nd window)
                    // if message is received after timeout, keep it in loraDeviceInfo and return the next call
                    var timeAvailableToCheckCloudToDeviceMessages = timeWatcher.GetAvailableTimeToCheckCloudToDeviceMessage(loRaDevice);
                    if (timeAvailableToCheckCloudToDeviceMessages >= LoRaOperationTimeWatcher.MinimumAvailableTimeToCheckForCloudMessage)
                    {
                        cloudToDeviceMessage = null;
                        if (cloudToDeviceMessage != null && !ValidateCloudToDeviceMessage(loRaDevice, request, cloudToDeviceMessage))
                        {
                            // Reject cloud to device message based on result from ValidateCloudToDeviceMessage
                            state.Track(cloudToDeviceMessage.RejectAsync());
                            cloudToDeviceMessage = null;
                        }

                        if (cloudToDeviceMessage != null)
                        {
                            if (!requiresConfirmation)
                            {
                                // The message coming from the device was not confirmed, therefore we did not computed the frame count down
                                // Now we need to increment because there is a C2D message to be sent
                                fcntDown = await EnsureHasFcntDownAsync(loRaDevice, fcntDown, payloadFcntAdjusted, frameCounterStrategy).ConfigureAwait(false);

                                if (!fcntDown.HasValue || fcntDown <= 0)
                                {
                                    // We did not get a valid frame count down, therefore we should not process the message
                                    state.Track(cloudToDeviceMessage.AbandonAsync());
                                    cloudToDeviceMessage = null;
                                }
                                else
                                {
                                    requiresConfirmation = true;
                                }
                            }
                        }
                    }
                }

                if (loRaDevice.ClassType is LoRaDeviceClassType.C
                    && loRaDevice.LastProcessingStationEui != request.StationEui)
                {
                    loRaDevice.SetLastProcessingStationEui(request.StationEui);
                }

                // No C2D message and request was not confirmed, return nothing
                if (!requiresConfirmation)
                {
                    return new LoRaDeviceRequestProcessResult(loRaDevice, request);
                }

                var confirmDownlinkMessageBuilderResp = DownlinkMessageBuilderResponse
                    (request, loRaDevice, timeWatcher, loRaADRResult, cloudToDeviceMessage, fcntDown, fpending);

                if (cloudToDeviceMessage != null)
                {
                    if (confirmDownlinkMessageBuilderResp.DownlinkMessage == null)
                    {
                        this.receiveWindowMissed?.Add(1);
                        logger.LogInformation($"out of time for downstream message, will abandon cloud to device message id: {cloudToDeviceMessage.MessageId ?? "undefined"}");
                        state.Track(cloudToDeviceMessage.AbandonAsync());
                    }
                    else if (confirmDownlinkMessageBuilderResp.IsMessageTooLong)
                    {
                        this.c2dMessageTooLong?.Add(1);
                        logger.LogError($"payload will not fit in current receive window, will abandon cloud to device message id: {cloudToDeviceMessage.MessageId ?? "undefined"}");
                        state.Track(cloudToDeviceMessage.AbandonAsync());
                    }
                    else
                    {
                        state.Track(cloudToDeviceMessage.CompleteAsync());
                    }
                }

                if (confirmDownlinkMessageBuilderResp.DownlinkMessage != null)
                {
                    this.receiveWindowHits?.Add(1, KeyValuePair.Create(MetricRegistry.ReceiveWindowTagName, (object)confirmDownlinkMessageBuilderResp.ReceiveWindow));
                    await SendMessageDownstreamAsync(request, confirmDownlinkMessageBuilderResp).ConfigureAwait(false);
                }

                return new LoRaDeviceRequestProcessResult(loRaDevice, request, confirmDownlinkMessageBuilderResp.DownlinkMessage);
                #endregion
            }
            finally
            {
                if (loRaDevice.IsConnectionOwner is true)
                    state.Track(SaveChangesToDeviceAsync(loRaDevice, isFrameCounterFromNewlyStartedDevice && !fcntResetSaved));
            }
        }

        internal virtual DownlinkMessageBuilderResponse DownlinkMessageBuilderResponse(LoRaRequest request, LoRaDevice loRaDevice, LoRaOperationTimeWatcher timeWatcher, LoRaADRResult loRaADRResult, IReceivedLoRaCloudToDeviceMessage cloudToDeviceMessage, uint? fcntDown, bool fpending)
        {
            _ = loRaDevice ?? throw new ArgumentNullException(nameof(loRaDevice));
            _ = request ?? throw new ArgumentNullException(nameof(request));
            _ = timeWatcher ?? throw new ArgumentNullException(nameof(timeWatcher));

            return DownlinkMessageBuilder.CreateDownlinkMessage(
                configuration,
                loRaDevice,
                request,
                timeWatcher,
                cloudToDeviceMessage,
                fpending,
                fcntDown.GetValueOrDefault(),
                loRaADRResult,
                logger);
        }

        protected virtual Task SendMessageDownstreamAsync(LoRaRequest request, DownlinkMessageBuilderResponse confirmDownlinkMessageBuilderResp)
        {
            _ = request ?? throw new ArgumentNullException(nameof(request));
            _ = confirmDownlinkMessageBuilderResp ?? throw new ArgumentNullException(nameof(confirmDownlinkMessageBuilderResp));

            return request.DownstreamMessageSender.SendDownstreamAsync(confirmDownlinkMessageBuilderResp.DownlinkMessage);
        }

        internal virtual async Task SaveChangesToDeviceAsync(LoRaDevice loRaDevice, bool force)
        {
            _ = loRaDevice ?? throw new ArgumentNullException(nameof(loRaDevice));

            _ = await loRaDevice.SaveChangesAsync(force: force).ConfigureAwait(false);
        }

        private void HandlePreferredGatewayChanges(
            LoRaRequest request,
            LoRaDevice loRaDevice,
            FunctionBundlerResult bundlerResult)
        {
            var preferredGatewayResult = bundlerResult.PreferredGatewayResult;
            if (preferredGatewayResult.IsSuccessful())
            {
                var currentIsPreferredGateway = bundlerResult.PreferredGatewayResult.PreferredGatewayID == configuration.GatewayID;

                var preferredGatewayChanged = bundlerResult.PreferredGatewayResult.PreferredGatewayID != loRaDevice.PreferredGatewayID;
                if (preferredGatewayChanged)
                    logger.LogDebug($"preferred gateway changed from '{loRaDevice.PreferredGatewayID}' to '{preferredGatewayResult.PreferredGatewayID}'");

                if (preferredGatewayChanged)
                {
                    loRaDevice.UpdatePreferredGatewayID(bundlerResult.PreferredGatewayResult.PreferredGatewayID, acceptChanges: !currentIsPreferredGateway);
                }

                // Save the region if we are the winning gateway and it changed
                if (request.Region.LoRaRegion != loRaDevice.LoRaRegion)
                {
                    loRaDevice.UpdateRegion(request.Region.LoRaRegion, acceptChanges: !currentIsPreferredGateway);
                }
            }
            else
            {
                logger.LogError($"failed to resolve preferred gateway: {preferredGatewayResult}");
            }
        }

        public void SetClassCMessageSender(IClassCDeviceMessageSender classCMessageSender) => this.classCDeviceMessageSender = classCMessageSender;

        private bool ValidateCloudToDeviceMessage(LoRaDevice loRaDevice, LoRaRequest request, ReceivedLoRaCloudToDeviceMessage cloudToDeviceMsg)
        {
            if (!cloudToDeviceMsg.IsValid(out var errorMessage))
            {
                logger.LogError(errorMessage);
                return false;
            }

            var radioMetadata = request.RadioMetadata;
            var loRaRegion = request.Region;
            uint maxPayload;

            // If preferred Window is RX2, this is the max. payload
            if (loRaDevice.PreferredWindow == ReceiveWindowNumber.ReceiveWindow2)
            {
                // Get max. payload size for RX2, considering possible user provided Rx2DataRate
                if (configuration.Rx2DataRate is null)
                {
                    var deviceJoinInfo = loRaRegion.LoRaRegion == LoRaRegionType.CN470RP2
                        ? new DeviceJoinInfo(loRaDevice.ReportedCN470JoinChannel, loRaDevice.DesiredCN470JoinChannel)
                        : null;

                    var rx2ReceiveWindow = loRaRegion.GetDefaultRX2ReceiveWindow(deviceJoinInfo);
                    (_, maxPayload) = loRaRegion.DRtoConfiguration[rx2ReceiveWindow.DataRate];
                }
                else
                {
                    maxPayload = loRaRegion.GetMaxPayloadSize(configuration.Rx2DataRate.Value);
                }
            }

            // Otherwise, it is RX1.
            else
            {
                var downstreamDataRate = loRaRegion.GetDownstreamDataRate(radioMetadata.DataRate);
                maxPayload = loRaRegion.GetMaxPayloadSize(downstreamDataRate);
            }

            // Deduct 8 bytes from max payload size.
            maxPayload -= Constants.LoraProtocolOverheadSize;

            // Calculate total C2D message size based on optional C2D Mac commands.
            var totalPayload = cloudToDeviceMsg.GetPayload()?.Length ?? 0;

            if (cloudToDeviceMsg.MacCommands?.Count > 0)
            {
                foreach (var macCommand in cloudToDeviceMsg.MacCommands)
                {
                    totalPayload += macCommand.Length;
                }
            }

            // C2D message and optional C2D Mac commands are bigger than max. payload size: REJECT.
            // This message can never be delivered.
            if (totalPayload > maxPayload)
            {
                logger.LogError($"message payload size ({totalPayload}) exceeds maximum allowed payload size ({maxPayload}) in cloud to device message");
                return false;
            }

            return true;
        }

        internal virtual Task<bool> SendDeviceEventAsync(LoRaRequest request, LoRaDevice loRaDevice, LoRaOperationTimeWatcher timeWatcher, object decodedValue, bool isDuplicate, byte[] decryptedPayloadData)
        {
            _ = loRaDevice ?? throw new ArgumentNullException(nameof(loRaDevice));
            _ = timeWatcher ?? throw new ArgumentNullException(nameof(timeWatcher));
            _ = request ?? throw new ArgumentNullException(nameof(request));

            var loRaPayloadData = (LoRaPayloadData)request.Payload;
            var deviceTelemetry = new LoRaDeviceTelemetry(request, loRaPayloadData, decodedValue, decryptedPayloadData)
            {
                DeviceEUI = loRaDevice.DevEUI.ToString(),
                GatewayID = configuration.GatewayID,
                Edgets = (long)(timeWatcher.Start - DateTime.UnixEpoch).TotalMilliseconds
            };

            if (isDuplicate)
            {
                deviceTelemetry.DupMsg = true;
            }

            Dictionary<string, string> eventProperties = null;
            if (loRaPayloadData.IsUpwardAck)
            {
                eventProperties = new Dictionary<string, string>();
                logger.LogInformation($"message ack received for cloud to device message id {loRaDevice.LastConfirmedC2DMessageID}");
                eventProperties.Add(Constants.C2D_MSG_PROPERTY_VALUE_NAME, loRaDevice.LastConfirmedC2DMessageID ?? Constants.C2D_MSG_ID_PLACEHOLDER);
                loRaDevice.LastConfirmedC2DMessageID = null;
            }

            ProcessAndSendMacCommands(loRaPayloadData, ref eventProperties);

            return Task.FromResult(false);
        }

        /// <summary>
        /// Send detected MAC commands as message properties.
        /// </summary>
        private static void ProcessAndSendMacCommands(LoRaPayloadData payloadData, ref Dictionary<string, string> eventProperties)
        {
            if (payloadData.MacCommands?.Count > 0)
            {
                eventProperties ??= new Dictionary<string, string>(payloadData.MacCommands.Count);

                for (var i = 0; i < payloadData.MacCommands.Count; i++)
                {
                    eventProperties[payloadData.MacCommands[i].Cid.ToString()] = JsonConvert.SerializeObject(payloadData.MacCommands[i].ToString(), Formatting.None);
                }
            }
        }

        /// <summary>
        /// Helper method to resolve FcntDown in case one was not yet acquired.
        /// </summary>
        /// <returns>0 if the resolution failed or > 0 if a valid frame count was acquired.</returns>
        private async ValueTask<uint> EnsureHasFcntDownAsync(
            LoRaDevice loRaDevice,
            uint? fcntDown,
            uint payloadFcnt,
            ILoRaDeviceFrameCounterUpdateStrategy frameCounterStrategy)
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

        internal virtual bool IsProcessingDelayEnabled() => configuration.ProcessingDelayInMilliseconds > 0;

        protected virtual async Task DelayProcessing() => await Task.Delay(TimeSpan.FromMilliseconds(configuration.ProcessingDelayInMilliseconds)).ConfigureAwait(false);

        protected virtual async Task<LoRaADRResult> PerformADR(LoRaRequest request, LoRaDevice loRaDevice, LoRaPayloadData loraPayload, uint payloadFcnt, LoRaADRResult loRaADRResult, ILoRaDeviceFrameCounterUpdateStrategy frameCounterStrategy)
        {
            _ = loRaDevice ?? throw new ArgumentNullException(nameof(loRaDevice));
            _ = request ?? throw new ArgumentNullException(nameof(request));
            _ = loraPayload ?? throw new ArgumentNullException(nameof(loraPayload));

            var loRaADRManager = loRaADRManagerFactory.Create(loRaADRStrategyProvider, frameCounterStrategy, loRaDevice);

            var loRaADRTableEntry = new LoRaADRTableEntry()
            {
                DevEUI = loRaDevice.DevEUI,
                FCnt = payloadFcnt,
                GatewayId = configuration.GatewayID,
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
                    configuration.GatewayID,
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
            ILoRaDeviceFrameCounterUpdateStrategy frameCounterStrategy,
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
                            _ = await frameCounterStrategy.ResetAsync(loRaDevice, payloadFcnt, configuration.GatewayID).ConfigureAwait(false);
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
