// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System.Globalization;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;

    /// <summary>
    /// LoRa payload decoder.
    /// </summary>
    public sealed class LoRaPayloadDecoder() : ILoRaPayloadDecoder
    {
        public ValueTask<DecodePayloadResult> DecodeMessageAsync(DevEui devEui, byte[] payload, FramePort fport, string sensorDecoder)
        {
            sensorDecoder ??= string.Empty;

            var decoderType = typeof(LoRaPayloadDecoder);
            var toInvoke = decoderType.GetMethod(sensorDecoder, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);

            if (toInvoke != null)
            {
                return ValueTask.FromResult(new DecodePayloadResult(toInvoke.Invoke(null, [devEui, payload, fport])));
            }
            else
            {
                return ValueTask.FromResult(new DecodePayloadResult()
                {
                    Error = $"'{sensorDecoder}' decoder not found",
                });
            }
        }

        /// <summary>
        /// Value sensor decoding, from <see cref="byte[]"/> to <see cref="DecodePayloadResult"/>.
        /// </summary>
        /// <param name="devEui">Device identifier.</param>
        /// <param name="payload">The payload to decode.</param>
        /// <param name="fport">The received frame port.</param>
        /// <returns>The decoded value as a JSON string.</returns>
#pragma warning disable CA1801 // Review unused parameters
#pragma warning disable IDE0060 // Remove unused parameter
        // Method is invoked via reflection.
        public static object DecoderValueSensor(DevEui devEui, byte[] payload, FramePort fport)
#pragma warning restore IDE0060 // Remove unused parameter
#pragma warning restore CA1801 // Review unused parameters
        {
            var payloadText = ((payload?.Length ?? 0) == 0) ? string.Empty : Encoding.UTF8.GetString(payload);

            if (long.TryParse(payloadText, NumberStyles.Float, CultureInfo.InvariantCulture, out var longValue))
            {
                return new DecodedPayloadValue(longValue);
            }

            if (double.TryParse(payloadText, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return new DecodedPayloadValue(doubleValue);
            }

            return new DecodedPayloadValue(payloadText);
        }

        /// <summary>
        /// Value Hex decoding, from <see cref="byte[]"/> to <see cref="DecodePayloadResult"/>.
        /// </summary>
        /// <param name="devEui">Device identifier.</param>
        /// <param name="payload">The payload to decode.</param>
        /// <param name="fport">The received frame port.</param>
        /// <returns>The decoded value as a JSON string.</returns>
#pragma warning disable CA1801 // Review unused parameters
#pragma warning disable IDE0060 // Remove unused parameter
        // Method is invoked via reflection and part of a public API.
        public static object DecoderHexSensor(DevEui devEui, byte[] payload, FramePort fport)
#pragma warning restore IDE0060 // Remove unused parameter
#pragma warning restore CA1801 // Review unused parameters
        {
            var payloadHex = ((payload?.Length ?? 0) == 0) ? string.Empty : payload.ToHex();
            return new DecodedPayloadValue(payloadHex);
        }
    }
}
