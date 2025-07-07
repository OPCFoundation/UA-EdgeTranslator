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
    using Microsoft.Extensions.Logging;
    using System;
    using System.Globalization;

    internal sealed class BasicsStationNetworkServerStartup(IConfiguration configuration)
    {
        public IConfiguration Configuration { get; } = configuration;

        public NetworkServerConfiguration NetworkServerConfiguration { get; } = NetworkServerConfiguration.CreateFromEnvironmentVariables();

        public void ConfigureServices(IServiceCollection services)
        {
            _ = services.AddLogging(loggingBuilder =>
                {
                    _ = loggingBuilder.ClearProviders();
                    var logLevel = int.TryParse(NetworkServerConfiguration.LogLevel, NumberStyles.Integer, CultureInfo.InvariantCulture, out var logLevelNum)
                        ? (LogLevel)logLevelNum is var level && Enum.IsDefined(level) ? level : throw new InvalidCastException()
                        : Enum.Parse<LogLevel>(NetworkServerConfiguration.LogLevel, true);

                    _ = loggingBuilder.SetMinimumLevel(logLevel);
                    _ = loggingBuilder.AddLoRaConsoleLogger(c => c.LogLevel = logLevel);
                })
                .AddMemoryCache()
                .AddSingleton(NetworkServerConfiguration)
                .AddSingleton<LoRaADRStrategyProvider>()
                .AddSingleton<LoRAADRManagerFactory>()
                .AddSingleton<DataMessageHandler>()
                .AddSingleton<JoinRequestMessageHandler>()
                .AddSingleton<MessageDispatcher>()
                .AddSingleton<BasicsStationConfigurationService>()
                .AddSingleton<DownlinkMessageSender>()
                .AddSingleton<ConcentratorDeduplication>();

            if (NetworkServerConfiguration.ClientCertificateMode is not ClientCertificateMode.NoCertificate)
            {
                _ = services.AddSingleton<ClientCertificateValidatorService>();
            }
        }

        // Startup class methods should not be static
        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting()
               .UseWebSockets()
               .UseMiddleware<WebsocketJsonMiddlewareLoRaWAN>()
               .Run(async (context) =>
               {
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
