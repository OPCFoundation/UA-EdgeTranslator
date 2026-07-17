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
        private readonly IConfiguration _configuration;
        private readonly object _lock = new();
        private AppSettings _settings;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        public SettingsService(IWebHostEnvironment env, IConfiguration configuration)
        {
            _configuration = configuration;
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
            // A user-saved settings.json (e.g. edited via the Settings page) always wins.
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

            // No saved settings yet: seed from environment variables / configuration so
            // the Cloud Library connection can be provided to the container upfront.
            return LoadFromConfiguration();
        }

        /// <summary>
        /// Builds settings from configuration (environment variables, appsettings.json, etc.),
        /// falling back to the built-in defaults when a value is not provided.
        /// Supported keys (in precedence order): the flat environment variables
        /// CLOUDLIB_URL / CLOUDLIB_USERNAME / CLOUDLIB_PASSWORD, then the
        /// "CloudLibrary" section (CloudLibrary:Url / CloudLibrary:UserName / CloudLibrary:Password,
        /// i.e. CloudLibrary__Url style environment variables).
        /// </summary>
        private AppSettings LoadFromConfiguration()
        {
            var defaults = new AppSettings();
            var section = _configuration.GetSection("CloudLibrary");

            string url = FirstNonEmpty(
                _configuration["CLOUDLIB_URL"],
                section["Url"],
                defaults.CloudLibraryUrl);

            string userName = FirstNonEmpty(
                _configuration["CLOUDLIB_USERNAME"],
                section["UserName"],
                defaults.UserName);

            string password = FirstNonEmpty(
                _configuration["CLOUDLIB_PASSWORD"],
                section["Password"],
                defaults.Password);

            return new AppSettings
            {
                CloudLibraryUrl = url,
                UserName = userName,
                Password = password
            };
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
