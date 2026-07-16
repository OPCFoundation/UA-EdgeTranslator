using System.Text.Json;
using WotOpcUaMapper.Models;

namespace WotOpcUaMapper.Services
{
    /// <summary>
    /// Loads and persists <see cref="AppSettings"/> to a JSON file so that the
    /// Cloud Library URL and credentials survive restarts. Registered as a singleton.
    /// </summary>
    public class SettingsService
    {
        private readonly string _filePath;
        private readonly object _lock = new();
        private AppSettings _settings;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        public SettingsService(IWebHostEnvironment env)
        {
            var dir = Path.Combine(env.ContentRootPath, "App_Data");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "settings.json");
            _settings = Load();
        }

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

        public void Save(AppSettings settings)
        {
            lock (_lock)
            {
                _settings = settings;
                File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, SerializerOptions));
            }
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load settings: " + ex.Message);
            }

            return new AppSettings();
        }
    }
}
