// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace LoRaWan.NetworkServer.BasicsStation
{
    using System;
    using System.Linq;
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using Microsoft.Extensions.Logging;

    internal sealed partial class ClientCertificateValidatorService(ILogger<ClientCertificateValidatorService> logger) : IClientCertificateValidatorService
    {
        public Task<bool> ValidateAsync(X509Certificate2 certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            ArgumentNullException.ThrowIfNull(chain);

            var commonName = certificate.GetNameInfo(X509NameType.SimpleName, false);
            var regex = MyRegex().Match(commonName);
            var parseSuccess = StationEui.TryParse(regex.Value, out var stationEui);

            if (!parseSuccess)
            {
                logger.LogError("Could not find a possible StationEui in '{CommonName}'.", commonName);
                return Task.FromResult(false);
            }

            using var scope = logger.BeginEuiScope(stationEui);

            // Logging any chain related issue that is causing verification to fail
            if (chain.ChainStatus.Any(s => s.Status != X509ChainStatusFlags.NoError))
            {
                foreach (var status in chain.ChainStatus)
                {
                    logger.LogError("{Status} {StatusInformation}", status.Status, status.StatusInformation);
                }
                logger.LogError("Some errors were found in the chain.");
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        [GeneratedRegex("([a-fA-F0-9]{2}[-:]?){8}")]
        private static partial Regex MyRegex();
    }
}
