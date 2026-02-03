// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace OCPPCentralSystem
{
    using Serilog;
    using System;
    using System.Linq;
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed partial class OCPPClientCertificateValidatorService()
    {
        public Task<bool> ValidateAsync(X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            ArgumentNullException.ThrowIfNull(chain);

            var commonName = certificate.GetNameInfo(X509NameType.SimpleName, false);
            Log.Logger.Information("Received client cert {commonName}", commonName);

            // Logging any chain related issue that is causing verification to fail
            if (chain.ChainStatus.Any(s => s.Status != X509ChainStatusFlags.NoError))
            {
                foreach (var status in chain.ChainStatus)
                {
                    Log.Logger.Warning("{Status} {StatusInformation}", status.Status, status.StatusInformation);
                }
            }

            return Task.FromResult(true);
        }
    }
}
