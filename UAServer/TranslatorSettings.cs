namespace Opc.Ua.Edge.Translator
{
    using Serilog;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// Describes one environment variable UA Edge Translator reads. Protocol drivers are
    /// independent of the translator, so the variables they read are not listed.
    /// </summary>
    /// <param name="Name">Variable name.</param>
    /// <param name="Category">Group the setting is shown under in the dashboard.</param>
    /// <param name="Description">What the setting does.</param>
    /// <param name="DefaultValue">Behaviour or value used when the variable is not set (display only).</param>
    /// <param name="IsSecret">The value is never shown in the dashboard.</param>
    /// <param name="RequiresRestart">The value is only read at startup, so a change takes effect after a restart.</param>
    /// <param name="IsRequired">The server refuses to start without a value, so it cannot be overridden with an empty value.</param>
    /// <param name="NotEmpty">An empty or whitespace value is invalid, so it cannot be saved. Unlike <paramref name="IsRequired"/>,
    /// the setting can still be left unset, in which case <paramref name="DefaultValue"/> applies.</param>
    public sealed record SettingDefinition(
        string Name,
        string Category,
        string Description,
        string DefaultValue = null,
        bool IsSecret = false,
        bool RequiresRestart = false,
        bool IsRequired = false,
        bool NotEmpty = false)
    {
        public bool AllowsEmpty => !IsRequired && !NotEmpty;
    }

    /// <summary>Where the effective value of a setting comes from.</summary>
    public enum SettingSource
    {
        NotSet,
        Environment,
        Persisted
    }

    /// <summary>The current state of one setting.</summary>
    public sealed record SettingState(
        string Name,
        SettingDefinition Definition,
        string EnvironmentValue,
        string PersistedValue,
        bool IsPersisted,
        bool PendingRestart)
    {
        public SettingSource Source =>
            IsPersisted ? SettingSource.Persisted
            : !string.IsNullOrEmpty(EnvironmentValue) ? SettingSource.Environment
            : SettingSource.NotSet;

        public string EffectiveValue => IsPersisted ? PersistedValue : EnvironmentValue;

        public bool HasValue => !string.IsNullOrEmpty(EffectiveValue);
    }

    /// <summary>Outcome of a change made through <see cref="TranslatorSettings"/>.</summary>
    public sealed record SettingChangeResult(bool Success, string Message);

    /// <summary>
    /// Exposes every environment variable of the translator and lets them be overridden at runtime.
    /// Overrides are persisted to <c>settings/translator-settings.json</c> and, because
    /// <see cref="Load"/> runs first thing at startup and writes them into the process environment,
    /// a persisted value wins over the value the process was started with. All code keeps reading
    /// <see cref="Environment.GetEnvironmentVariable(string)"/>, so settings that are read on use
    /// apply immediately; settings read only at startup apply after a restart.
    /// </summary>
    public sealed class TranslatorSettings
    {
        public const string FileName = "translator-settings.json";

        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

        private readonly object _lock = new();

        // Values the process was started with, captured before any override was applied, so
        // removing an override restores the original environment value.
        private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);

        private readonly SortedDictionary<string, string> _persisted = new(StringComparer.Ordinal);

        // Effective values when the settings were loaded, to tell which startup-only
        // settings have changed since and need a restart.
        private readonly Dictionary<string, string> _startup = new(StringComparer.Ordinal);

        public TranslatorSettings(string filePath)
        {
            ArgumentException.ThrowIfNullOrEmpty(filePath);
            FilePath = filePath;
        }

        /// <summary>The settings in use by the running translator, loaded in <see cref="Program.Main"/>.</summary>
        public static TranslatorSettings Current { get; set; }

        public static string DefaultFilePath => Path.Combine(Directory.GetCurrentDirectory(), "settings", FileName);

        public string FilePath { get; }

        public static IReadOnlyList<SettingDefinition> Definitions { get; } = new List<SettingDefinition>
        {
            // Server
            new("OPCUA_USERNAME", "Server", "Username OPC UA clients and the dashboard log in with.", IsRequired: true, RequiresRestart: true),
            new("OPCUA_PASSWORD", "Server", "Password OPC UA clients and the dashboard log in with.", IsSecret: true, IsRequired: true, RequiresRestart: true),
            new("APP_NAME", "Server", "OPC UA application name; also used for the application URI and certificate subject.", "UAEdgeTranslator", RequiresRestart: true, NotEmpty: true),
            new("IGNORE_PROVISIONING_MODE", "Server", "Set to 1 to allow asset-tag access while in provisioning mode. Untrusted client certificates are then rejected instead of auto-accepted."),
            new("LICENSE_KEY", "Server", "Activation key required by the OPC UA License write method. License activation is rejected when unset.", IsSecret: true),
            new("WOT_MAX_FILE_BYTES", "Server", "Maximum size in bytes accepted per upload through the OPC UA File API.", "5242880", RequiresRestart: true),

            // Asset management
            new("DISABLE_ASSET_CONNECTION_TEST", "Asset management", "Set to 1 to skip the connection test when mapping an asset to OPC UA."),
            new("DELETE_WOT_FILE_ON_ASSET_DELETE", "Asset management", "Set to 1 to also delete an asset's Thing Description file when the asset is deleted."),
            new("WOTCON_MANAGEMENT_ROLE", "Asset management", "NodeId of the role required to call the asset-management methods.", "i=15716 (ConfigureAdmin)"),
            new("DISABLE_WOTCON_REGISTRY", "Asset management", "Set to 1 to turn off the WoT Connectivity 1.1 registry (preview).", RequiresRestart: true),

            // UA Cloud Library
            new("UACLURL", "UA Cloud Library", "UA Cloud Library URL, e.g. https://uacloudlibrary.opcfoundation.org.", RequiresRestart: true),
            new("UACLUsername", "UA Cloud Library", "UA Cloud Library username."),
            new("UACLPassword", "UA Cloud Library", "UA Cloud Library password.", IsSecret: true),

            // Driver allow-list
            new("DRIVER_ALLOWLIST_OIDC_ISSUER", "Driver allow-list", "Fulcio OIDC issuer the driver allow-list signing certificate must come from.", "https://token.actions.githubusercontent.com", RequiresRestart: true),
            new("DRIVER_ALLOWLIST_OIDC_REPO", "Driver allow-list", "GitHub owner/repo whose driver-pack workflow is allowed to sign the driver allow-list.", "OPCFoundation/UA-EdgeTranslator", RequiresRestart: true),
            new("DRIVER_ALLOWLIST_OFFLINE_MODE", "Driver allow-list", "Set to allow-hash-only to permit hash-only enforcement when the Sigstore bundle cannot be verified.", "fail-closed", RequiresRestart: true),
        };

        public static IReadOnlyList<string> Categories { get; } = Definitions.Select(d => d.Category).Distinct().ToList();

        public static SettingDefinition FindDefinition(string name)
        {
            return Definitions.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// Snapshots the process environment and applies the persisted overrides to it.
        /// Call once at startup, before anything reads the environment.
        /// </summary>
        public void Load()
        {
            lock (_lock)
            {
                _environment.Clear();
                foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                {
                    string name = entry.Key as string;
                    if (FindDefinition(name) != null)
                    {
                        _environment[name] = entry.Value as string;
                    }
                }

                _persisted.Clear();
                foreach (KeyValuePair<string, string> pair in ReadFile())
                {
                    if (FindDefinition(pair.Key) == null)
                    {
                        Log.Logger.Warning("Ignoring unknown setting {Name} in {File}.", pair.Key, FilePath);
                        continue;
                    }

                    _persisted[pair.Key] = pair.Value ?? string.Empty;
                    Environment.SetEnvironmentVariable(pair.Key, string.IsNullOrEmpty(pair.Value) ? null : pair.Value);
                }

                _startup.Clear();
                foreach (string name in _environment.Keys.Concat(_persisted.Keys))
                {
                    _startup[name] = EffectiveValue(name);
                }

                if (_persisted.Count > 0)
                {
                    Log.Logger.Information("Applied {Count} persisted setting(s) from {File}: {Names}.", _persisted.Count, FilePath, string.Join(", ", _persisted.Keys));
                }
            }
        }

        /// <summary>Returns the state of every setting.</summary>
        public IReadOnlyList<SettingState> GetAll()
        {
            lock (_lock)
            {
                return Definitions.Select(d => CreateState(d.Name, d)).ToList();
            }
        }

        public SettingState Get(string name)
        {
            SettingDefinition definition = FindDefinition(name);
            if (definition == null)
            {
                return null;
            }

            lock (_lock)
            {
                return CreateState(name, definition);
            }
        }

        /// <summary>
        /// Persists <paramref name="value"/> for <paramref name="name"/> and applies it to the
        /// process environment. An empty value persists "unset", overriding an environment value.
        /// </summary>
        public SettingChangeResult Set(string name, string value)
        {
            SettingDefinition definition = FindDefinition(name);
            if (definition == null)
            {
                return new SettingChangeResult(false, $"'{name}' is not a setting of UA Edge Translator.");
            }

            value ??= string.Empty;
            if (value.Contains('\0', StringComparison.Ordinal))
            {
                return new SettingChangeResult(false, $"The value for {name} contains an invalid character.");
            }

            if (!definition.AllowsEmpty && string.IsNullOrWhiteSpace(value))
            {
                return new SettingChangeResult(false, $"{name} cannot be empty.");
            }

            lock (_lock)
            {
                var updated = new SortedDictionary<string, string>(_persisted, StringComparer.Ordinal) { [name] = value };
                string error = WriteFile(updated);
                if (error != null)
                {
                    return new SettingChangeResult(false, $"Could not save {name}: {error}");
                }

                _persisted[name] = value;
                Environment.SetEnvironmentVariable(name, string.IsNullOrEmpty(value) ? null : value);
            }

            Log.Logger.Information("Setting {Name} was changed from the dashboard.", name);
            return new SettingChangeResult(true, ChangedMessage(name, definition, string.IsNullOrEmpty(value) ? "cleared" : "saved"));
        }

        /// <summary>
        /// Removes the persisted override of <paramref name="name"/>, restoring the value the
        /// process was started with.
        /// </summary>
        public SettingChangeResult Reset(string name)
        {
            SettingDefinition definition = FindDefinition(name);
            if (definition == null)
            {
                return new SettingChangeResult(false, $"'{name}' is not a setting of UA Edge Translator.");
            }

            lock (_lock)
            {
                if (!_persisted.ContainsKey(name))
                {
                    return new SettingChangeResult(true, $"{name} has no saved value.");
                }

                _environment.TryGetValue(name, out string original);
                if (definition.IsRequired && string.IsNullOrEmpty(original))
                {
                    return new SettingChangeResult(false, $"{name} is required and is not set in the environment, so its saved value cannot be removed.");
                }

                var updated = new SortedDictionary<string, string>(_persisted, StringComparer.Ordinal);
                updated.Remove(name);
                string error = WriteFile(updated);
                if (error != null)
                {
                    return new SettingChangeResult(false, $"Could not reset {name}: {error}");
                }

                _persisted.Remove(name);
                Environment.SetEnvironmentVariable(name, string.IsNullOrEmpty(original) ? null : original);
            }

            Log.Logger.Information("Setting {Name} was reset to its environment value from the dashboard.", name);
            return new SettingChangeResult(true, ChangedMessage(name, definition, "reset to its environment value"));
        }

        private static string ChangedMessage(string name, SettingDefinition definition, string action) =>
            definition.RequiresRestart
                ? $"{name} {action}. Restart UA Edge Translator for the change to take effect."
                : $"{name} {action}. The change is in effect.";

        private SettingState CreateState(string name, SettingDefinition definition)
        {
            _environment.TryGetValue(name, out string environmentValue);
            bool persisted = _persisted.TryGetValue(name, out string persistedValue);
            _startup.TryGetValue(name, out string startupValue);
            bool pendingRestart = definition.RequiresRestart
                && !string.Equals(EffectiveValue(name) ?? string.Empty, startupValue ?? string.Empty, StringComparison.Ordinal);
            return new SettingState(name, definition, environmentValue, persistedValue, persisted, pendingRestart);
        }

        private string EffectiveValue(string name) =>
            _persisted.TryGetValue(name, out string persisted)
                ? persisted
                : _environment.TryGetValue(name, out string environment) ? environment : null;

        private Dictionary<string, string> ReadFile()
        {
            if (!File.Exists(FilePath))
            {
                return new Dictionary<string, string>();
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))
                    ?? new Dictionary<string, string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Logger.Error(ex, "Could not read persisted settings from {File}; using the environment only.", FilePath);
                return new Dictionary<string, string>();
            }
        }

        // Writes to a temporary file and moves it into place so a crash never leaves a
        // truncated file. The file can hold secrets, so on Unix only the owner may read it.
        private string WriteFile(IDictionary<string, string> values)
        {
            string tempPath = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath)));

                var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows())
                {
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }

                using (var stream = new FileStream(tempPath, options))
                {
                    JsonSerializer.Serialize(stream, values, s_jsonOptions);
                }

                File.Move(tempPath, FilePath, overwrite: true);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Logger.Error(ex, "Could not write persisted settings to {File}.", FilePath);
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    // Best effort.
                }

                return ex.Message;
            }
        }
    }
}
