
namespace OCPPCentralSystem
{
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Server.Kestrel.Https;
    using Microsoft.Extensions.DependencyInjection;
    using Opc.Ua.Edge.Translator;
    using System;
    using System.Threading.Tasks;

    public static class CentralSystemServer
    {
        public static async Task RunServerAsync()
        {
            bool secureComms = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_TLS"));
            using var webHost = WebHost.CreateDefaultBuilder()
                                       .UseUrls(secureComms ? [FormattableString.Invariant($"https://0.0.0.0:19521")]
                                                            : [FormattableString.Invariant($"http://0.0.0.0:19520")])
                                       .UseStartup<OCPPStartup>()
                                       .UseKestrel(config =>
                                       {
                                           if (secureComms)
                                           {
                                               config.ConfigureHttpsDefaults(https =>
                                                   ConfigureHttpsSettings(secureComms,
                                                                          config.ApplicationServices.GetService<OCPPClientCertificateValidatorService>(),
                                                                          https));
                                           }
                                       })
                                       .Build();

            try
            {
                await webHost.RunAsync().ConfigureAwait(false);
            }
            finally
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        internal static void ConfigureHttpsSettings(bool secureComms,
                                                    OCPPClientCertificateValidatorService clientCertificateValidatorService,
                                                    HttpsConnectionAdapterOptions https)
        {
            https.ServerCertificate = Program.App.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.Certificate;

            if (secureComms)
            {
                ArgumentNullException.ThrowIfNull(clientCertificateValidatorService);

                https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                https.ClientCertificateValidation = (cert, chain, err) => clientCertificateValidatorService.ValidateAsync(cert, chain, err, default).GetAwaiter().GetResult();
            }
        }
    }
}
