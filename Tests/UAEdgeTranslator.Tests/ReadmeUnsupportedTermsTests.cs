namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Xunit;

    /// <summary>
    /// Keeps the README's list of unsupported LoRaWAN binding terms in step with
    /// <see cref="LoRaWANForm.UnsupportedTerms"/>.
    /// <para>
    /// The README previously listed only some of the rejected terms and trailed
    /// off with "and related", so a reader could not tell whether a given term
    /// would be accepted or would fail onboarding. Documentation that is merely
    /// close to the truth is worse than none here, because the failure it
    /// describes happens at deployment time.
    /// </para>
    /// </summary>
    public class ReadmeUnsupportedTermsTests
    {
        private static string ReadReadme()
        {
            DirectoryInfo directory = new(AppContext.BaseDirectory);

            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "README.md");

                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not locate README.md.");
        }

        [Fact]
        public void The_readme_documents_every_unsupported_term()
        {
            string readme = ReadReadme();

            // Every term the driver refuses must be findable in the README,
            // wherever it is documented, so a reader hitting the rejection can
            // look it up.
            List<string> missing = LoRaWANForm.UnsupportedTerms
                .Where(term => !readme.Contains("`" + term + "`", StringComparison.Ordinal))
                .ToList();

            Assert.True(
                missing.Count == 0,
                "The README does not document these rejected terms: " + string.Join(", ", missing));
        }

        [Fact]
        public void The_readme_documents_every_withdrawn_term_and_its_replacement()
        {
            string readme = ReadReadme();

            // A withdrawn term is rejected naming its replacement, so both ends
            // of that mapping have to appear or the advice is unfollowable.
            List<string> missing = [];

            foreach (KeyValuePair<string, string> entry in LoRaWANForm.WithdrawnTerms)
            {
                if (!readme.Contains("`" + entry.Key + "`", StringComparison.Ordinal))
                {
                    missing.Add(entry.Key);
                }
            }

            Assert.True(
                missing.Count == 0,
                "The README does not document these withdrawn terms: " + string.Join(", ", missing));
        }

        [Fact]
        public void The_readme_does_not_claim_a_supported_term_is_rejected()
        {
            string readme = ReadReadme();

            // Documenting a working term as unsupported would send a reader
            // looking for a workaround they do not need.
            string[] supported =
            [
                "lorav:derived",
                "lorav:tagFields",
                "lorav:tag",
                "lorav:slot",
                "lorav:padBefore",
                "lorav:presentWhen",
                "lorav:valueMap",
                "lorav:alias"
            ];

            foreach (string term in supported)
            {
                Assert.DoesNotContain(term, LoRaWANForm.UnsupportedTerms);
            }

            Assert.Contains("lorav:derived", readme, StringComparison.Ordinal);
        }

    }
}
