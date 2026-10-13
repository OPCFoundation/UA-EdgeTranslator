
namespace OCPPCentralSystem
{
    using Microsoft.AspNetCore.Builder;
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
            // The driver is loaded before the OPC UA server starts, but the TLS certificate
            // below is the OPC UA application certificate.
            await Program.ServerStarted.ConfigureAwait(false);

            bool secureComms = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_TLS"));
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton<OCPPStartup>();

            builder.WebHost
                .UseUrls(secureComms ? [FormattableString.Invariant($"https://0.0.0.0:19521")]
                                     : [FormattableString.Invariant($"http://0.0.0.0:19520")])
                .UseKestrel(config =>
                {
                    if (secureComms)
                    {
                        config.ConfigureHttpsDefaults(https => ConfigureHttpsSettings(
                            secureComms,
                            builder.Services.BuildServiceProvider().GetService<OCPPClientCertificateValidatorService>(),
                            https));
                    }
                });

            var preBuildStartup = new OCPPStartup(builder.Configuration);
            preBuildStartup.ConfigureServices(builder.Services);

            var webHost = builder.Build();

            var startup = webHost.Services.GetRequiredService<OCPPStartup>();
            startup.Configure(webHost, webHost.Environment);

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
            https.ServerCertificate = TlsServerCertificates.GetApplicationCertificate(Program.App.ApplicationConfiguration);

            if (secureComms)
            {
                ArgumentNullException.ThrowIfNull(clientCertificateValidatorService);

                https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                https.ClientCertificateValidation = (cert, chain, err) => clientCertificateValidatorService.ValidateAsync(cert, chain, err, default).GetAwaiter().GetResult();
            }
        }
    }
}
