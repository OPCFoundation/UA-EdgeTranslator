namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Xunit;

    /// <summary>
    /// Guards the <c>name</c> field of every shipped Thing Description.
    /// <para>
    /// <c>UANodeManager.OnboardAssetFromWoTFileAsync</c> rejects a Thing
    /// Description whose name fails <c>IsSafeAssetName</c>, because the value is
    /// used to build a namespace URI, dictionary keys and a file path. A sample
    /// that violates the rule cannot be onboarded at all, so it is broken as
    /// shipped rather than merely untidy.
    /// </para>
    /// </summary>
    public class SampleThingDescriptionNameTests
    {
        private const int MaxAssetNameLength = 128;

        /// <summary>
        /// Mirrors <c>UANodeManager.IsSafeAssetName</c>, which is private.
        /// </summary>
        private static bool IsSafeAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (name.Length > MaxAssetNameLength)
            {
                return false;
            }

            if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
            {
                return false;
            }

            foreach (char c in name)
            {
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-'))
                {
                    return false;
                }
            }

            return name[0] != '.';
        }

        public static IEnumerable<object[]> SampleFiles()
        {
            foreach (string file in Directory.EnumerateFiles(FindRepoFolder("Samples"), "*.td.jsonld"))
            {
                yield return new object[] { Path.GetFileName(file) };
            }
        }

        [Theory]
        [MemberData(nameof(SampleFiles))]
        public void A_sample_declares_an_onboardable_name(string fileName)
        {
            string path = Path.Combine(FindRepoFolder("Samples"), fileName);

            ThingDescription td =
                JsonConvert.DeserializeObject<ThingDescription>(File.ReadAllText(path).Trim('\uFEFF'));

            Assert.NotNull(td);

            Assert.True(
                IsSafeAssetName(td.Name),
                $"'{fileName}' declares name '{td.Name}', which UANodeManager rejects at onboarding. " +
                "Names may contain only letters, digits, '.', '_' and '-'.");
        }

        [Theory]
        [MemberData(nameof(SampleFiles))]
        public void A_sample_keeps_its_human_readable_title(string fileName)
        {
            // The name is machine-facing and constrained; the descriptive text
            // belongs in 'title', which has no such restriction. Tightening a
            // name must not silently discard the readable form.
            string path = Path.Combine(FindRepoFolder("Samples"), fileName);

            ThingDescription td =
                JsonConvert.DeserializeObject<ThingDescription>(File.ReadAllText(path).Trim('\uFEFF'));

            Assert.False(
                string.IsNullOrWhiteSpace(td.Title),
                $"'{fileName}' has no 'title'; the human-readable name would be lost.");
        }

        private static string FindRepoFolder(string folderName)
        {
            DirectoryInfo directory = new(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, folderName);

                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException($"Could not locate the '{folderName}' folder.");
        }
    }
}
