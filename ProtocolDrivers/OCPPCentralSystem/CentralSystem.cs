
namespace OCPPCentralSystem
{
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Server.Kestrel.Https;
    using Microsoft.Extensions.DependencyInjection;
    using Opc.Ua.Edge.Translator;
    using System;
    using System.Threading.Tasks;

    public static class CentralSystem
    {
        public static async Task RunServerAsync()
        {
            bool requireCertificate = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OCPP_USE_TLS"));
            using var webHost = WebHost.CreateDefaultBuilder()
                                       .UseUrls(requireCertificate ? [FormattableString.Invariant($"https://0.0.0.0:19521")]
                                                                   : [FormattableString.Invariant($"http://0.0.0.0:19520")])
                                       .UseStartup<OCPPStartup>()
                                       .UseKestrel(config =>
                                       {
                                           if (requireCertificate)
                                           {
                                               config.ConfigureHttpsDefaults(https =>
                                                   ConfigureHttpsSettings(requireCertificate,
                                                                          config.ApplicationServices.GetService<OCPPClientCertificateValidatorService>(),
                                                                          https));
                                           }
                                       })
                                       .Build();

            try
            {
                await webHost.RunAsync();
            }
            finally
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        internal static void ConfigureHttpsSettings(bool requireCertificate, 
                                                    OCPPClientCertificateValidatorService clientCertificateValidatorService,
                                                    HttpsConnectionAdapterOptions https)
        {
            https.ServerCertificate = Program.App.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate;

            if (requireCertificate)
            {
                ArgumentNullException.ThrowIfNull(clientCertificateValidatorService);

                https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                https.ClientCertificateValidation = (cert, chain, err) => clientCertificateValidatorService.ValidateAsync(cert, chain, err, default).GetAwaiter().GetResult();
            }
        }
    }
}
