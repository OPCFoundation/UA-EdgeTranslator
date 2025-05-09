// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using global::LoRaWan;

namespace LoRaWANContainer.LoRaWan.NetworkServer
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using LoRaWANContainer.LoRaWan.NetworkServer.Models;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    internal sealed class LoggerConfigurationMonitor : IDisposable
    {
        private readonly IDisposable onChangeToken;

        public LoRaLoggerConfiguration Configuration { get; private set; }
        public IExternalScopeProvider? ScopeProvider { get; private set; }

        public LoggerConfigurationMonitor(IOptionsMonitor<LoRaLoggerConfiguration> optionsMonitor)
        {
            ArgumentNullException.ThrowIfNull(optionsMonitor);

            // Initialize onChangeToken to a default value to satisfy the non-nullable requirement
            this.onChangeToken = optionsMonitor.OnChange(UpdateConfiguration) ?? throw new InvalidOperationException("OnChange returned null.");
            UpdateConfiguration(optionsMonitor.CurrentValue);
        }

        public void Dispose()
        {
            this.onChangeToken.Dispose();
        }

        [MemberNotNull(nameof(Configuration))]
        private void UpdateConfiguration(LoRaLoggerConfiguration configuration)
        {
            Configuration = configuration;
            ScopeProvider = Configuration.UseScopes ? new LoggerExternalScopeProvider() : null;
        }
    }
}
