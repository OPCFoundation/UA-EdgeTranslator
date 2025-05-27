// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace LoRaWan.NetworkServer.BasicsStation
{
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Server.Kestrel.Https;
    using Microsoft.Extensions.DependencyInjection;
    using Opc.Ua.Edge.Translator;
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public static class BasicsStationNetworkServer
    {
        internal const string DiscoveryEndpoint = "/router-info";
        internal const string RouterIdPathParameterName = "routerId";
        internal const string DataEndpoint = "/router-data";

        internal const int LnsSecurePort = 5001;
        internal const int LnsPort = 5000;

        public static async Task RunServerAsync(NetworkServerConfiguration configuration, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var requireCertificate = (configuration.ClientCertificateMode != ClientCertificateMode.NoCertificate);
            using var webHost = WebHost.CreateDefaultBuilder()
                                       .UseUrls(requireCertificate ? [FormattableString.Invariant($"https://0.0.0.0:{LnsSecurePort}")]
                                                                   : [FormattableString.Invariant($"http://0.0.0.0:{LnsPort}")])
                                       .UseStartup<BasicsStationNetworkServerStartup>()
                                       .UseKestrel(config =>
                                       {
                                           if (requireCertificate)
                                           {
                                               config.ConfigureHttpsDefaults(https => ConfigureHttpsSettings(configuration,
                                                                                                             config.ApplicationServices.GetService<IClientCertificateValidatorService>(),
                                                                                                             https));
                                           }
                                       })
                                       .Build();

            try
            {
                await webHost.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            }
        }

        internal static void ConfigureHttpsSettings(NetworkServerConfiguration configuration,
                                                    IClientCertificateValidatorService? clientCertificateValidatorService,
                                                    HttpsConnectionAdapterOptions https)
        {
            https.ServerCertificate = Program.App.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate;

            if (configuration.ClientCertificateMode is not ClientCertificateMode.NoCertificate)
            {
                ArgumentNullException.ThrowIfNull(clientCertificateValidatorService);

                https.ClientCertificateMode = configuration.ClientCertificateMode;
                https.ClientCertificateValidation = (cert, chain, err) => clientCertificateValidatorService.ValidateAsync(cert, chain, err, default).GetAwaiter().GetResult();
            }
        }
    }
}
