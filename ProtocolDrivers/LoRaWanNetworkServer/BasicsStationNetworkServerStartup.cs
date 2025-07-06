// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer.BasicsStation
{
    using global::LoRaWan;
    using LoRaWan.NetworkServer;
    using LoRaWan.NetworkServer.BasicsStation.Processors;
    using LoRaWANContainer.LoRaWan.NetworkServer;
    using LoRaWANContainer.LoRaWan.NetworkServer.Interfaces;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Server.Kestrel.Https;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Globalization;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

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
                .AddSingleton<LoRaDeviceFrameCounterUpdateStrategyProvider>()
                .AddSingleton<LoRaADRStrategyProvider>()
                .AddSingleton<LoRAADRManagerFactory>()
                .AddSingleton<LoRaPayloadDecoder>()
                .AddSingleton<DefaultLoRaDataRequestHandler>()
                .AddSingleton<JoinRequestMessageHandler>()
                .AddSingleton<MessageDispatcher>()
                .AddSingleton<BasicsStationConfigurationService>()
                .AddSingleton<DefaultClassCDevicesMessageSender>()
                .AddSingleton<WebSocketWriterRegistry<StationEui, string>>()
                .AddSingleton<DownlinkMessageSender>()
                .AddTransient<LnsProtocolMessageProcessor>()
                .AddSingleton<ConcentratorDeduplication>();

            if (NetworkServerConfiguration.ClientCertificateMode is not ClientCertificateMode.NoCertificate)
            {
                _ = services.AddSingleton<ClientCertificateValidatorService>();
            }
        }

#pragma warning disable CA1822 // Mark members as static
        // Startup class methods should not be static
        public void Configure(IApplicationBuilder app)
#pragma warning restore CA1822 // Mark members as static
        {
            // Manually set the class C as otherwise the DI fails.
            var classCMessageSender = app.ApplicationServices.GetService<DefaultClassCDevicesMessageSender>();
            var dataHandlerImplementation = app.ApplicationServices.GetService<DefaultLoRaDataRequestHandler>();
            dataHandlerImplementation.SetClassCMessageSender(classCMessageSender);

            _ = app.UseRouting()
                   .UseWebSockets()
                   .UseEndpoints(endpoints =>
                   {
                       Map(HttpMethod.Get, BasicsStationNetworkServer.DiscoveryEndpoint,
                          (LnsProtocolMessageProcessor processor) => processor.HandleDiscoveryAsync);

                       Map(HttpMethod.Get, $"{BasicsStationNetworkServer.DataEndpoint}/{{{BasicsStationNetworkServer.RouterIdPathParameterName}:required}}",
                          (LnsProtocolMessageProcessor processor) => processor.HandleDataAsync);

                       void Map<TService>(HttpMethod method, string pattern,
                                          Func<TService, Func<HttpContext, CancellationToken, Task>> handlerMapper)
                       {
                           _ = endpoints.MapMethods(pattern, [method.ToString()], async context =>
                           {
                               var processor = context.RequestServices.GetRequiredService<TService>();
                               var handler = handlerMapper(processor);
                               await handler(context, context.RequestAborted).ConfigureAwait(false);
                           });
                       }
                   });
        }
    }
}
