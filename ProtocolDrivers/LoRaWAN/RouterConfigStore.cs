namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Holds the Basic Station <c>router_config</c> messages available to the
    /// LoRaWAN network server.
    /// <para>
    /// A router configuration describes a gateway's radio hardware — region,
    /// frequency range, data rates and SX1301 channel plan — and has no WoT
    /// affordances, so it is stored as a plain JSON file in the settings folder
    /// rather than as a Thing Description.
    /// </para>
    /// <para>
    /// A file may be named after the gateway it serves
    /// (<c>&lt;devEUI&gt;.json</c>, 16 hex digits) or after the hardware model
    /// (<c>SX1301EU.json</c>). Model-named files act as a default, because a
    /// router configuration is a property of the radio hardware rather than of
    /// an individual gateway, and most deployments run one gateway type.
    /// </para>
    /// </summary>
    public static class RouterConfigStore
    {
        private static readonly Dictionary<string, string> _configsByDevEui = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new();

        private static string _defaultConfig;

        /// <summary>
        /// The router configuration served to a gateway that has no
        /// configuration registered under its own DevEUI.
        /// </summary>
        public static string DefaultConfig
        {
            get
            {
                lock (_lock)
                {
                    return _defaultConfig;
                }
            }
        }

        /// <summary>
        /// Loads every router configuration in the settings folder. Called once
        /// when the network server is instantiated, so that a gateway sending
        /// its 'version' message is answered immediately rather than only after
        /// an asset happens to be onboarded.
        /// </summary>
        public static void LoadAll(string settingsFolder)
        {
            if (string.IsNullOrWhiteSpace(settingsFolder) || !Directory.Exists(settingsFolder))
            {
                return;
            }

            lock (_lock)
            {
                _configsByDevEui.Clear();
                _defaultConfig = null;
            }

            foreach (string path in Directory.EnumerateFiles(settingsFolder, "*.json"))
            {
                try
                {
                    string contents = File.ReadAllText(path);

                    // The settings folder holds unrelated JSON too, so identify
                    // router configurations by their message type rather than by
                    // assuming every file is one.
                    if (!IsRouterConfig(contents))
                    {
                        continue;
                    }

                    string name = Path.GetFileNameWithoutExtension(path);

                    lock (_lock)
                    {
                        if (IsDevEui(name))
                        {
                            _configsByDevEui[name] = contents;

                            Log.Logger.Information("Loaded LoRaWAN router configuration for gateway {DevEui}.", name);
                        }
                        else
                        {
                            _defaultConfig = contents;

                            Log.Logger.Information("Loaded default LoRaWAN router configuration from '{Name}'.", name);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // One malformed file must not stop the others loading, but
                    // it has to be visible: a gateway relying on it will never
                    // come up.
                    Log.Logger.Error("Could not load LoRaWAN router configuration '{Path}': {Message}", path, ex.Message);
                }
            }
        }

        /// <summary>
        /// Returns the router configuration for a gateway, falling back to the
        /// default, or <c>null</c> when none is available.
        /// </summary>
        public static string Get(string devEui)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(devEui) && _configsByDevEui.TryGetValue(devEui, out string config))
                {
                    return config;
                }

                return _defaultConfig;
            }
        }

        /// <summary>
        /// Registers a router configuration for a specific gateway.
        /// </summary>
        public static void Add(string devEui, string routerConfig)
        {
            if (string.IsNullOrWhiteSpace(devEui))
            {
                throw new ArgumentException("A gateway DevEUI is required.", nameof(devEui));
            }

            lock (_lock)
            {
                _configsByDevEui[devEui] = routerConfig;
            }
        }

        private static bool IsRouterConfig(string contents)
        {
            try
            {
                JObject parsed = JObject.Parse(contents);

                return string.Equals(
                    parsed["msgtype"]?.ToString(),
                    "router_config",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsDevEui(string name)
        {
            if (name is not { Length: 16 })
            {
                return false;
            }

            foreach (char c in name)
            {
                if (!Uri.IsHexDigit(c))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
