/*
Copyright 2020 Cognizant
Copyright 2021 Microsoft Corporation
*/

namespace OCPPCentralSystem
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using System;

    public class OCPPStartup
    {
        public OCPPStartup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddSingleton<OCPPClientCertificateValidatorService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.Use(async (context, next) =>
            {
                context.Request.Headers.TryGetValue("Origin", out var origin);
                context.Response.Headers.Append("Access-Control-Allow-Origin", origin);
                context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
                await next().ConfigureAwait(false);
            });

            var webSocketOptions = new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(1)
            };

            app.UseWebSockets(webSocketOptions);

            app.UseMiddleware<WebsocketJsonMiddlewareOCPP>();

            app.Run(async (context) =>
            {
                if (context.Request.Path.Value == "/")
                {
                    await context.Response.WriteAsync("OCPP Central System running.").ConfigureAwait(false);
                }
                else
                {
                    await context.Response.WriteAsync("Invalid Request").ConfigureAwait(false);
                }
            });
        }
    }
}
