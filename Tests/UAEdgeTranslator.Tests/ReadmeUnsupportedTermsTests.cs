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

            // The README groups these: current terms appear in the table, and the
            // withdrawn pre-0.3 terms are named in the sentence below it. Either
            // placement counts - what matters is that a reader can find every
            // term the driver will reject.
            List<string> missing = LoRaWANForm.UnsupportedTerms
                .Where(term => !readme.Contains("`" + term + "`", StringComparison.Ordinal))
                .ToList();

            Assert.True(
                missing.Count == 0,
                "The README does not document these rejected terms: " + string.Join(", ", missing));
        }

        [Fact]
        public void The_readme_table_lists_only_current_binding_terms()
        {
            string readme = ReadReadme();

            // The binding withdrew its pre-0.3 terms. Listing them in the main
            // table alongside current ones implies they are worth reaching for,
            // so they belong in the migration sentence instead.
            string[] withdrawn =
            [
                "lorav:presenceField",
                "lorav:presenceBit",
                "lorav:switchField",
                "lorav:switchValue",
                "lorav:var",
                "lorav:ref",
                "lorav:polynomial",
                "lorav:compute",
                "lorav:guard",
                "lorav:transform"
            ];

            List<string> inTable = ReadTableTerms(readme)
                .Where(term => withdrawn.Contains(term))
                .ToList();

            Assert.True(
                inTable.Count == 0,
                "These withdrawn terms should not be in the main table: " + string.Join(", ", inTable));
        }

        /// <summary>
        /// Returns the <c>lorav:</c> terms named in the README's unsupported-terms
        /// table, stopping at the first non-row line so the prose below it is not
        /// scooped up.
        /// </summary>
        private static List<string> ReadTableTerms(string readme)
        {
            int tableStart = readme.IndexOf("| Term | Purpose |", StringComparison.Ordinal);

            Assert.True(tableStart >= 0, "The unsupported-terms table is missing from the README.");

            List<string> rows = [];

            foreach (string line in readme.Substring(tableStart).Split('\n').Skip(1))
            {
                string trimmed = line.TrimStart('>', ' ').TrimEnd('\r');

                if (!trimmed.StartsWith('|'))
                {
                    break;
                }

                rows.Add(trimmed);
            }

            return Regex
                .Matches(string.Join("\n", rows), @"`(lorav:[A-Za-z]+)`")
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList();
        }

        [Fact]
        public void The_readme_does_not_invent_unsupported_terms()
        {
            string readme = ReadReadme();

            // Everything listed in the README's unsupported-terms table must
            // actually be rejected; documenting a supported term as unsupported
            // would send a reader looking for a workaround they do not need.
            List<string> documented = ReadTableTerms(readme);

            Assert.NotEmpty(documented);

            List<string> notActuallyRejected = documented
                .Where(term => !LoRaWANForm.UnsupportedTerms.Contains(term))
                .ToList();

            Assert.True(
                notActuallyRejected.Count == 0,
                "The README lists these as unsupported, but the driver accepts them: "
                    + string.Join(", ", notActuallyRejected));
        }
    }
}
