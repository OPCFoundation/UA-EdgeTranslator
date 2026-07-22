using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using WotOpcUaMapper.Models;

namespace WotOpcUaMapper.Services
{
    /// <summary>
    /// Holds <see cref="AppSettings"/> (Cloud Library URL and credentials) for the current user.
    /// Registered as scoped so that each user configures their own Cloud Library instance and
    /// credentials without seeing or affecting other users. Settings are persisted to the
    /// browser's protected session storage, so they survive a page refresh (F5) while remaining
    /// isolated per user and are never written to shared server-side storage.
    /// </summary>
    public class SettingsService
    {
        private const string StorageKey = "wotmapper.cloudlibrary.settings";

        private readonly ProtectedSessionStorage _storage;
        private readonly object _lock = new();
        private AppSettings _settings = new();

        public SettingsService(ProtectedSessionStorage storage)
        {
            _storage = storage;
        }

        /// <summary>True once <see cref="HydrateAsync"/> has attempted to load stored settings.</summary>
        public bool Hydrated { get; private set; }

        public AppSettings Current
        {
            get
            {
                lock (_lock)
                {
                    // return a copy so callers can edit without mutating the stored instance
                    return new AppSettings
                    {
                        CloudLibraryUrl = _settings.CloudLibraryUrl,
                        UserName = _settings.UserName,
                        Password = _settings.Password
                    };
                }
            }
        }

        /// <summary>
        /// Loads any previously saved settings from the browser's session storage into memory.
        /// Must be called from a component after the first interactive render (JS interop is not
        /// available earlier). Safe to call repeatedly; only the first call reads storage.
        /// </summary>
        public async Task HydrateAsync()
        {
            if (Hydrated)
            {
                return;
            }

            try
            {
                var result = await _storage.GetAsync<AppSettings>(StorageKey);
                if (result.Success && result.Value != null)
                {
                    lock (_lock)
                    {
                        _settings = result.Value;
                    }
                }
            }
            catch
            {
                // No/invalid stored settings; keep the in-memory defaults.
            }
            finally
            {
                Hydrated = true;
            }
        }

        /// <summary>
        /// Stores the given settings in memory for this session and persists them to the browser's
        /// session storage so they survive a page refresh.
        /// </summary>
        public async Task SaveAsync(AppSettings settings)
        {
            lock (_lock)
            {
                _settings = settings;
            }

            await _storage.SetAsync(StorageKey, settings);
        }
    }
}
