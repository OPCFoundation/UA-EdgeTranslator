// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;
    using System.Collections.Generic;
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;

    [JsonConverter(typeof(MacCommandJsonConverter))]
    public abstract class MacCommand
    {
        /// <summary>
        /// Gets or sets cid number of.
        /// </summary>
        [JsonProperty("cid")]
        public Cid Cid { get; set; }

        [JsonIgnore]
        public abstract int Length { get; }

        public abstract override string ToString();

        /// <summary>
        /// Initializes a new instance of the <see cref="MacCommand"/> class.
        /// create.
        /// </summary>
        protected MacCommand(ReadOnlySpan<byte> input)
        {
            if (input.Length < Length)
                throw new MacCommandException("The Mac Command was not well formed, aborting mac command processing");
        }

        protected MacCommand()
        {
        }

        public abstract IEnumerable<byte> ToBytes();

        /// <summary>
        /// Create a List of Mac commands from client based on a sequence of bytes.
        /// </summary>
        public static IList<MacCommand> CreateMacCommandFromBytes(ReadOnlyMemory<byte> input, ILogger logger = null)
        {
            var pointer = 0;
            var macCommands = new List<MacCommand>(3);

            try
            {
                while (pointer < input.Length)
                {
                    var cid = (Cid)input.Span[pointer];
                    switch (cid)
                    {
                        case Cid.LinkCheckCmd:
                            var linkCheck = new LinkCheckRequest();
                            pointer += linkCheck.Length;
                            macCommands.Add(linkCheck);
                            break;
                        case Cid.LinkADRCmd:
                            var linkAdrAnswer = new LinkADRAnswer(input.Span[pointer..]);
                            pointer += linkAdrAnswer.Length;
                            macCommands.Add(linkAdrAnswer);
                            break;
                        case Cid.DutyCycleCmd:
                            var dutyCycle = new DutyCycleAnswer();
                            pointer += dutyCycle.Length;
                            macCommands.Add(dutyCycle);
                            break;
                        case Cid.RXParamCmd:
                            var rxParamSetup = new RXParamSetupAnswer(input.Span[pointer..]);
                            pointer += rxParamSetup.Length;
                            macCommands.Add(rxParamSetup);
                            break;
                        case Cid.DevStatusCmd:
                            // Added this case to enable unit testing
                            if (input.Length == 1)
                            {
                                var devStatusRequest = new DevStatusRequest();
                                pointer += devStatusRequest.Length;
                                macCommands.Add(devStatusRequest);
                            }
                            else
                            {
                                var devStatus = new DevStatusAnswer(input.Span[pointer..]);
                                pointer += devStatus.Length;
                                macCommands.Add(devStatus);
                            }

                            break;
                        case Cid.NewChannelCmd:
                            var newChannel = new NewChannelAnswer(input.Span[pointer..]);
                            pointer += newChannel.Length;
                            macCommands.Add(newChannel);
                            break;
                        case Cid.RXTimingCmd:
                            var rxTimingSetup = new RXTimingSetupAnswer();
                            pointer += rxTimingSetup.Length;
                            macCommands.Add(rxTimingSetup);
                            break;
                        case Cid.TxParamSetupCmd:
                            var txParamSetupAnswer = new TxParamSetupAnswer();
                            pointer += txParamSetupAnswer.Length;
                            macCommands.Add(txParamSetupAnswer);
                            break;
                        default:
                            logger?.LogError($"a transmitted Mac Command value ${input.Span[pointer]} was not from a supported type. Aborting Mac Command processing");
                            return null;
                    }
                }
            }
            catch (MacCommandException ex) when (ExceptionFilterUtility.True(() => logger?.LogError(ex.ToString())))
            {
                // continue
            }

            return macCommands;
        }

        /// <summary>
        /// Create a List of Mac commands from server based on a sequence of bytes.
        /// </summary>
        public static IList<MacCommand> CreateServerMacCommandFromBytes(ReadOnlyMemory<byte> input, ILogger logger = null)
        {
            var pointer = 0;
            var macCommands = new List<MacCommand>(3);

            while (pointer < input.Length)
            {
                try
                {
                    var cid = (Cid)input.Span[pointer];
                    switch (cid)
                    {
                        case Cid.LinkCheckCmd:
                            var linkCheck = new LinkCheckAnswer(input.Span[pointer..]);
                            pointer += linkCheck.Length;
                            macCommands.Add(linkCheck);
                            break;
                        case Cid.DevStatusCmd:
                            var devStatusRequest = new DevStatusRequest();
                            pointer += devStatusRequest.Length;
                            macCommands.Add(devStatusRequest);
                            break;
                        case Cid.LinkADRCmd:
                        case Cid.DutyCycleCmd:
                        case Cid.RXParamCmd:
                        case Cid.NewChannelCmd:
                        case Cid.RXTimingCmd:
                        case Cid.TxParamSetupCmd:
                        default:
                            logger?.LogError($"a Mac command transmitted from the server, value ${input.Span[pointer]} was not from a supported type. Aborting Mac Command processing");
                            return null;
                    }

                    var addedMacCommand = macCommands[^1];
                    logger?.LogDebug($"{addedMacCommand.Cid} mac command detected in upstream payload: {addedMacCommand}");
                }
                catch (MacCommandException ex)
                {
                    logger?.LogError(ex.ToString());
                }
            }

            return macCommands;
        }
    }
}
