namespace WotOpcUaMapper.Services
{
    /// <summary>
    /// Persists Web of Things files (Thing Models and generated Thing Descriptions) to a
    /// server-side working directory so that generated Thing Descriptions survive restarts
    /// and can be picked up by other tooling. The base directory can be provided upfront to
    /// the container via the WOT_DIRECTORY environment variable (or the "Wot:Directory"
    /// configuration key); it defaults to an "App_Data/wot" folder under the content root.
    ///
    /// The service is registered as scoped so that each user session (Blazor circuit) works
    /// against its own isolated sub-directory. This guarantees that two users using the app
    /// at the same time never see or overwrite each other's loaded/generated WoT files.
    /// </summary>
    public class WotFileService : IDisposable
    {
        public WotFileService(IWebHostEnvironment env, IConfiguration configuration)
        {
            var configured = configuration["WOT_DIRECTORY"] ?? configuration["Wot:Directory"];
            var baseDirectory = !string.IsNullOrWhiteSpace(configured)
                ? configured
                : Path.Combine(env.ContentRootPath, "App_Data", "wot");

            // Isolate every circuit into its own tenant folder so concurrent users cannot
            // see or clobber each other's files.
            Directory = Path.Combine(baseDirectory, "tenants", Guid.NewGuid().ToString("N"));

            System.IO.Directory.CreateDirectory(Directory);
        }

        /// <summary>
        /// Absolute path of the per-session directory where this user's WoT files are stored.
        /// </summary>
        public string Directory { get; }

        /// <summary>
        /// Writes <paramref name="content"/> to <paramref name="fileName"/> inside the working
        /// directory, overwriting any existing file with the same name. Returns the full path.
        /// </summary>
        public string Save(string fileName, string content)
        {
            var safeName = Path.GetFileName(fileName);
            var fullPath = Path.Combine(Directory, safeName);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        /// <summary>
        /// Deletes the given file (by name) from the working directory if it exists.
        /// </summary>
        public void Delete(string fileName)
        {
            var safeName = Path.GetFileName(fileName);
            var fullPath = Path.Combine(Directory, safeName);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        /// <summary>
        /// Removes this session's isolated working directory when the circuit ends so that
        /// per-user folders do not accumulate on the server over time.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                {
                    System.IO.Directory.Delete(Directory, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; ignore failures (e.g. files still in use).
            }
        }
    }
}
