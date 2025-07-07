// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer.BasicsStation
{
    using LoRaWan.NetworkServer;
    using LoRaWANContainer.LoRaWan.NetworkServer;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Server.Kestrel.Https;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;

    internal sealed class BasicsStationNetworkServerStartup(IConfiguration configuration)
    {
        public IConfiguration Configuration { get; } = configuration;

        public NetworkServerConfiguration NetworkServerConfiguration { get; } = NetworkServerConfiguration.CreateFromEnvironmentVariables();

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddSingleton(NetworkServerConfiguration);
            services.AddSingleton<LoRaADRStrategyProvider>();
            services.AddSingleton<LoRAADRManagerFactory>();
            services.AddSingleton<DataMessageHandler>();
            services.AddSingleton<JoinRequestMessageHandler>();
            services.AddSingleton<MessageDispatcher>();
            services.AddSingleton<BasicsStationConfigurationService>();
            services.AddSingleton<DownlinkMessageSender>();
            services.AddSingleton<ConcentratorDeduplication>();

            if (NetworkServerConfiguration.ClientCertificateMode is not ClientCertificateMode.NoCertificate)
            {
                services.AddSingleton<ClientCertificateValidatorService>();
            }
        }

        // Startup class methods should not be static
        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();

            app.UseWebSockets();
            app.UseMiddleware<WebsocketJsonMiddlewareLoRaWAN>();

            app.Run(async (context) => {
                if (context.Request.Path.Value == "/")
                {
                    await context.Response.WriteAsync("LoRaWAN Network Server running.").ConfigureAwait(false);
                }
                else
                {
                    await context.Response.WriteAsync("Invalid Request").ConfigureAwait(false);
                }
            });
        }
    }
}
