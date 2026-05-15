namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.IO;

    /// <summary>
    /// RAII helper that switches <see cref="Directory.SetCurrentDirectory"/> into
    /// a fresh, unique temp directory for the lifetime of the test, then restores
    /// the previous working directory and best-effort deletes the temp tree.
    ///
    /// Several production helpers (FileManager, UACloudLibraryClient,
    /// DriverLoadContext, UANodeManager) compose paths from
    /// <c>Directory.GetCurrentDirectory()</c>. Using this helper keeps those
    /// tests hermetic so they cannot pollute the developer's repo or interfere
    /// with each other when xUnit runs them in parallel.
    /// </summary>
    internal sealed class TestWorkingDirectory : IDisposable
    {
        private readonly string _previous;

        public TestWorkingDirectory()
        {
            _previous = Directory.GetCurrentDirectory();
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "uaedge-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Directory.SetCurrentDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.SetCurrentDirectory(_previous);
            }
            catch
            {
                // best-effort
            }

            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup; the OS will reclaim eventually
            }
        }
    }
}
