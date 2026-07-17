namespace WotOpcUaMapper.Services
{
    /// <summary>
    /// Persists Web of Things files (Thing Models and generated Thing Descriptions) to a
    /// server-side working directory so that generated Thing Descriptions survive restarts
    /// and can be picked up by other tooling. The directory can be provided upfront to the
    /// container via the WOT_DIRECTORY environment variable (or the "Wot:Directory"
    /// configuration key); it defaults to an "App_Data/wot" folder under the content root.
    /// </summary>
    public class WotFileService
    {
        public WotFileService(IWebHostEnvironment env, IConfiguration configuration)
        {
            var configured = configuration["WOT_DIRECTORY"] ?? configuration["Wot:Directory"];
            Directory = !string.IsNullOrWhiteSpace(configured)
                ? configured
                : Path.Combine(env.ContentRootPath, "App_Data", "wot");

            System.IO.Directory.CreateDirectory(Directory);
        }

        /// <summary>
        /// Absolute path of the directory where WoT files are stored.
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
    }
}
