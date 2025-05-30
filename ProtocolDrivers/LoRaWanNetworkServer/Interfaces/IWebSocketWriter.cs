// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using global::LoRaWan;

namespace LoRaWANContainer.LoRaWan.NetworkServer.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IWebSocketWriter<in T> where T : notnull
    {
        bool IsClosed { get; }
        ValueTask SendAsync(T message, CancellationToken cancellationToken);
    }
}
