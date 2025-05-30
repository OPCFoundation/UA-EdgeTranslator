// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using LoRaTools;

namespace LoRaWANContainer.LoRaWan.NetworkServer.Models
{
    using System;

    public static class NoopDisposable
    {
        public static readonly IDisposable Instance = new Disposable();

        private sealed class Disposable : IDisposable
        {
            public void Dispose() { /* noop */ }
        }
    }
}
