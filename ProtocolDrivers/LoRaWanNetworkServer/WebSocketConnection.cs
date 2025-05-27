// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using LoRaTools;

namespace LoRaWANContainer.LoRaWan.NetworkServer
{
    using System;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;

    public sealed class WebSocketConnection
    {
        private readonly HttpContext httpContext;
        private readonly ILogger? logger;

        public WebSocketConnection(HttpContext httpContext, ILogger? logger)
        {
            this.httpContext = httpContext ?? throw new ArgumentNullException(nameof(httpContext));
            this.logger = logger;
        }

        public async Task<HttpContext> HandleAsync(Func<HttpContext, WebSocket, CancellationToken, Task> handler, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(handler);

            if (!this.httpContext.WebSockets.IsWebSocketRequest)
            {
                this.httpContext.Response.StatusCode = 400;
                return this.httpContext;
            }

            using var socket = await this.httpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            this.logger?.LogDebug("WebSocket connection from {RemoteIpAddress} established", this.httpContext.Connection.RemoteIpAddress);

            try
            {
                await handler(this.httpContext, socket, cancellationToken).ConfigureAwait(false);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Goodbye", cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
#pragma warning disable CA1508 // Avoid dead conditional code (false positive)
                when (ex is { InnerException: WebSocketException { WebSocketErrorCode: WebSocketError.ConnectionClosedPrematurely } })
#pragma warning restore CA1508 // Avoid dead conditional code
            {
                // Client lost connectivity
                this.logger?.LogDebug(ex, "Client lost connectivity: {Exception}", ex.Message);
            }

            return this.httpContext;
        }
    }
}
