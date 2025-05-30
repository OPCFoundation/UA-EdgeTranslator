// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System.Collections.Generic;
    using global::LoRaWan;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;

    /// <summary>
    /// Defines the contract for a LoRa cloud to device message.
    /// </summary>
    public class LoRaCloudToDeviceMessage : ILoRaCloudToDeviceMessage
    {
        private const string DevEuiPropertyName = "DevEUI";

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public DevEui? DevEUI { get; set; }

        [Newtonsoft.Json.JsonProperty(DevEuiPropertyName)]
        [System.Text.Json.Serialization.JsonPropertyName(DevEuiPropertyName)]
        public string DevEuiString
        {
            get => DevEUI?.ToString();
            set => DevEUI = value is null ? null : DevEui.Parse(value);
        }

        public FramePort Fport { get; set; }

        /// <summary>
        /// Gets or sets payload as base64 string
        /// Use this to send bytes.
        /// </summary>
        public string RawPayload { get; set; }

        /// <summary>
        /// Gets or sets payload as string
        /// Use this to send text.
        /// </summary>
        public string Payload { get; set; }

        public bool Confirmed { get; set; }

        public string MessageId { get; set; }

        public IList<MacCommand> MacCommands { get; } = new List<MacCommand>();

        /// <summary>
        /// Gets if the cloud to device message has any payload data (mac commands don't count).
        /// </summary>
        protected bool HasPayload() => !string.IsNullOrEmpty(Payload) || !string.IsNullOrEmpty(RawPayload);

        /// <summary>
        /// Identifies if the message is a valid LoRa downstream message.
        /// </summary>
        /// <param name="errorMessage">Returns the error message in case it fails.</param>
        /// <returns>True if the message is valid, false otherwise.</returns>
        public virtual bool IsValid(out string errorMessage)
        {
            // ensure fport follows LoRa specification
            // 0    => reserved for mac commands
            // 224+ => reserved for future applications
            if (Fport.IsReserved())
            {
                errorMessage = $"invalid fport '{(byte)Fport}' in cloud to device message '{MessageId}'";
                return false;
            }

            // fport 0 is reserved for mac commands
            if (Fport == FramePort.MacCommand)
            {
                // Not valid if there is no mac command or there is a payload
                if ((MacCommands?.Count ?? 0) == 0 || HasPayload())
                {
                    errorMessage = $"invalid MAC command fport usage in cloud to device message '{MessageId}'";
                    return false;
                }
            }

            errorMessage = null;
            return true;
        }
    }
}
