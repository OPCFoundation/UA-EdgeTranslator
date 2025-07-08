// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace LoRaWan.NetworkServer.BasicsStation
{
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Server.Kestrel.Https;
    using Microsoft.Extensions.DependencyInjection;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator;
    using Opc.Ua.Security.Certificates;
    using System;
    using System.IO;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    public static class BasicsStationNetworkServer
    {
        internal const int LnsSecurePort = 5001;
        internal const int LnsPort = 5000;

        public static async Task RunServerAsync(NetworkServerConfiguration configuration, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            bool secureComms = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_TLS"));
            using var webHost = WebHost.CreateDefaultBuilder()
                                       .UseUrls(secureComms ? [FormattableString.Invariant($"https://0.0.0.0:{LnsSecurePort}")]
                                                            : [FormattableString.Invariant($"http://0.0.0.0:{LnsPort}")])
                                       .UseStartup<BasicsStationNetworkServerStartup>()
                                       .UseKestrel(config =>
                                       {
                                           if (secureComms)
                                           {
                                               config.ConfigureHttpsDefaults(https => ConfigureHttpsSettings(configuration,
                                                                                                             config.ApplicationServices.GetService<ClientCertificateValidatorService>(),
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
                                                    ClientCertificateValidatorService? clientCertificateValidatorService,
                                                    HttpsConnectionAdapterOptions https)
        {
            X509Certificate2 opcuaCert = Program.App.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate;

            if (File.Exists(Path.Combine("pki/own/private", $"{configuration.GatewayID}-with-san.pfx")))
            {
                https.ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine("pki/own/private", $"{configuration.GatewayID}-with-san.pfx"), string.Empty);
            }
            else
            {
                // create a new X.509 certificate for LoRaWAN Network Server, based on the OPC UA application certificate, but set the certificate alternate name to the gateway ID
                https.ServerCertificate = CertificateFactory.CreateCertificate(opcuaCert.Subject)
                    .AddExtension(new X509SubjectAltNameExtension("urn:UAEdgeTranslator", ["UAEdgeTranslator"]))
                    .SetNotBefore(DateTime.Today.AddDays(-1))
                    .SetLifeTime(12)
                    .SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(2048))
                    .SetCAConstraint()
                    .SetRSAKeySize(2048)
                    .CreateForRSA();

                byte[] bytes = https.ServerCertificate.Export(X509ContentType.Cert);
                string base64 = Convert.ToBase64String(bytes);

                string pem = "-----BEGIN CERTIFICATE-----\n";
                for (int i = 0; i < base64.Length; i += 64)
                {
                    pem += base64.Substring(i, Math.Min(64, base64.Length - i)) + "\n";
                }
                pem += "-----END CERTIFICATE-----\n";

                File.WriteAllText(Path.Combine("pki/own/private", $"{configuration.GatewayID}-with-san.pem"), pem);
                File.WriteAllBytes(Path.Combine("pki/own/private", $"{configuration.GatewayID}-with-san.pfx"), https.ServerCertificate.Export(X509ContentType.Pfx, string.Empty));
            }

            bool secureComms = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_TLS"));
            if (secureComms)
            {
                ArgumentNullException.ThrowIfNull(clientCertificateValidatorService);

                https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                https.ClientCertificateValidation = (cert, chain, err) => clientCertificateValidatorService.ValidateAsync(cert, chain, err, default).GetAwaiter().GetResult();
            }
        }
    }
}
