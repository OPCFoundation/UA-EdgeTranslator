namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Xunit;

    /// <summary>
    /// FileManager's constructor wires up OPC UA SDK callbacks on a real
    /// <c>FileState</c>, which can't be easily constructed in a unit test.
    /// The helpers under test (<c>SanitizeFileName</c>, <c>AtomicWriteAllText</c>,
    /// <c>ResolveMaxFileBytes</c>) are independent of any SDK state, so the
    /// tests instantiate FileManager with <see cref="RuntimeHelpers.GetUninitializedObject"/>
    /// and invoke them via reflection.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class FileManagerHelperTests
    {
        private static readonly Type _sut = typeof(FileManager);

        private static FileManager NewBareInstance() =>
            (FileManager)RuntimeHelpers.GetUninitializedObject(_sut);

        [Theory]
        [InlineData("normal.jsonld")]
        [InlineData("a/b/c")]
        [InlineData("..\\evil")]
        [InlineData("with*invalid?chars:.txt")]
        [InlineData("trailing.dots...")]
        [InlineData("   ")]
        [InlineData("")]
        [InlineData(null)]
        public void SanitizeFileName_strips_unsafe_characters(string input)
        {
            FileManager fm = NewBareInstance();
            string result = (string)_sut
                .GetMethod("SanitizeFileName", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(fm, new object[] { input })!;

            Assert.False(string.IsNullOrEmpty(result));

            // The exact mapping for path separators differs between OSes (because
            // Path.GetInvalidFileNameChars() differs); just enforce the safety
            // contract instead of brittle byte-for-byte equality. Use ordinal
            // comparison everywhere so culture-insensitive characters (e.g. '\0',
            // which has no collation weight) don't false-positive substring search.
            Assert.DoesNotContain("..", result, StringComparison.Ordinal);
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                Assert.DoesNotContain(c.ToString(), result, StringComparison.Ordinal);
            }

            // Spot-check the unnamed fallback explicitly.
            if (string.IsNullOrWhiteSpace(input))
            {
                Assert.Equal("unnamed", result);
            }
        }

        [Fact]
        public void AtomicWriteAllText_creates_file_and_directory_if_missing()
        {
            using TestWorkingDirectory tmp = new();
            FileManager fm = NewBareInstance();

            string nested = Path.Combine(tmp.Path, "sub", "deeper", "out.txt");

            _sut.GetMethod("AtomicWriteAllText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(fm, new object[] { nested, "hello" });

            Assert.True(File.Exists(nested));
            Assert.Equal("hello", File.ReadAllText(nested));
        }

        [Fact]
        public void AtomicWriteAllText_overwrites_existing_file()
        {
            using TestWorkingDirectory tmp = new();
            FileManager fm = NewBareInstance();

            string target = Path.Combine(tmp.Path, "out.txt");
            File.WriteAllText(target, "old");

            _sut.GetMethod("AtomicWriteAllText", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(fm, new object[] { target, "new" });

            Assert.Equal("new", File.ReadAllText(target));

            // No leftover .tmp.* siblings should remain in the directory
            string[] leftovers = Directory.GetFiles(tmp.Path, "*.tmp.*");
            Assert.Empty(leftovers);
        }

        [Fact]
        public void ResolveMaxFileBytes_uses_default_when_env_var_unset()
        {
            const string Var = "WOT_MAX_FILE_BYTES";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, null);
                int value = (int)_sut
                    .GetMethod("ResolveMaxFileBytes", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null)!;
                Assert.Equal(5 * 1024 * 1024, value);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-100")]
        [InlineData("not-an-int")]
        [InlineData("")]
        public void ResolveMaxFileBytes_falls_back_to_default_for_invalid_values(string raw)
        {
            const string Var = "WOT_MAX_FILE_BYTES";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, raw);
                int value = (int)_sut
                    .GetMethod("ResolveMaxFileBytes", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null)!;
                Assert.Equal(5 * 1024 * 1024, value);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }

        [Fact]
        public void ResolveMaxFileBytes_honors_positive_override()
        {
            const string Var = "WOT_MAX_FILE_BYTES";
            string previous = Environment.GetEnvironmentVariable(Var);
            try
            {
                Environment.SetEnvironmentVariable(Var, "12345");
                int value = (int)_sut
                    .GetMethod("ResolveMaxFileBytes", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, null)!;
                Assert.Equal(12345, value);
            }
            finally
            {
                Environment.SetEnvironmentVariable(Var, previous);
            }
        }
    }
}
