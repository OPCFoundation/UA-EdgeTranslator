// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer.BasicsStation
{
    using LoRaWan.NetworkServer;
    using LoRaWANContainer.LoRaWan.NetworkServer;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;

    internal sealed class BasicsStationNetworkServerStartup()
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMemoryCache();

            services.AddSingleton<LoRAADRManagerFactory>();
            services.AddSingleton<DataMessageHandler>();
            services.AddSingleton<JoinRequestMessageHandler>();
            services.AddSingleton<MessageDispatcher>();
            services.AddSingleton<BasicsStationConfigurationService>();
            services.AddSingleton<DownlinkMessageSender>();
            services.AddSingleton<ConcentratorDeduplication>();
            services.AddSingleton<ClientCertificateValidatorService>();
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
