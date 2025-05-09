// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.AspNetCore.Server.Kestrel.Https;

    // Network server configuration
    public class NetworkServerConfiguration
    {
        /// <summary>
        /// Gets or sets the gateway identifier.
        /// </summary>
        public string GatewayID { get; set; }

        /// <summary>
        /// Gets or sets the 2nd receive windows datarate.
        /// </summary>
        public DataRateIndex? Rx2DataRate { get; set; }

        /// <summary>
        /// Gets or sets the 2nd receive windows data frequency.
        /// </summary>
        public Hertz? Rx2Frequency { get; set; }

        /// <summary>
        /// Gets or sets  the logging level.
        /// Default: 4 (Log level: Error).
        /// </summary>
        public string LogLevel { get; set; } = "4";

        /// <summary>
        /// Gets or sets the gateway netword id.
        /// </summary>
        public NetId NetId { get; set; } = new NetId(1);

        /// <summary>
        /// Gets list of allowed dev addresses.
        /// </summary>
        public HashSet<DevAddr> AllowedDevAddresses { get; internal set; }

        /// <summary>
        /// Specifies the client certificate mode with which the server should be run
        /// Allowed values can be found at https://docs.microsoft.com/dotnet/api/microsoft.aspnetcore.server.kestrel.https.clientcertificatemode?view=aspnetcore-6.0
        /// </summary>
        public ClientCertificateMode ClientCertificateMode { get; internal set; }

        /// <summary>
        /// Specifies the Processing Delay in Milliseconds
        /// </summary>
        public int ProcessingDelayInMilliseconds { get; set; } = Constants.DefaultProcessingDelayInMilliseconds;

        // Creates a new instance of NetworkServerConfiguration by reading values from environment variables
        public static NetworkServerConfiguration CreateFromEnvironmentVariables()
        {
            var config = new NetworkServerConfiguration();

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSING_DELAY_IN_MS")))
            {
                var delay = int.Parse(Environment.GetEnvironmentVariable("PROCESSING_DELAY_IN_MS"), NumberFormatInfo.InvariantInfo);
                if (delay != 0)
                {
                    config.ProcessingDelayInMilliseconds = delay;
                }
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOG_LEVEL")))
            {
                config.LogLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RX2_DATR")))
            {
                config.Rx2DataRate = (DataRateIndex)int.Parse(Environment.GetEnvironmentVariable("RX2_DATR"), NumberFormatInfo.InvariantInfo);
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RX2_FREQ")))
            {
                config.Rx2Frequency = double.Parse(Environment.GetEnvironmentVariable("RX2_FREQ"), NumberFormatInfo.InvariantInfo) is { } someFreq ? Hertz.Mega(someFreq) : null;
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NETID")))
            {
                config.NetId = new NetId(int.Parse(Environment.GetEnvironmentVariable("NETID"), NumberFormatInfo.InvariantInfo));
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLIENT_CERTIFICATE_MODE")))
            {
                config.ClientCertificateMode = Enum.Parse<ClientCertificateMode>(Environment.GetEnvironmentVariable("CLIENT_CERTIFICATE_MODE"), true);
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AllowedDevAddresses")))
            {
                config.AllowedDevAddresses = [.. Environment.GetEnvironmentVariable("AllowedDevAddresses")
                    .Split(";")
                    .Select(s => DevAddr.TryParse(s, out var devAddr) ? (true, Value: devAddr) : default)
                    .Where(a => a is (true, _))
                    .Select(a => a.Value)];
            }

            return config;
        }
    }
}
