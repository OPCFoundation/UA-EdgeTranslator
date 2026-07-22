namespace WotOpcUaMapper.Models
{
    /// <summary>
    /// User configurable settings for connecting to a UA Cloud Library instance.
    /// Held per user session (Blazor circuit) by <see cref="Services.SettingsService"/>.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Base URL of the UA Cloud Library server, e.g. https://uacloudlibrary.opcfoundation.org
        /// </summary>
        public string CloudLibraryUrl { get; set; } = "https://uacloudlibrary.opcfoundation.org";

        /// <summary>
        /// User name used for HTTP basic authentication against the Cloud Library.
        /// </summary>
        public string UserName { get; set; } = string.Empty;

        /// <summary>
        /// Password used for HTTP basic authentication against the Cloud Library.
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }
}
