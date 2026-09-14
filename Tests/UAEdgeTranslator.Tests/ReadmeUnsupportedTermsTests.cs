namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Xunit;

    /// <summary>
    /// Guards the LoRaWAN binding claims the README actually makes.
    /// <para>
    /// The README deliberately does not carry an exhaustive term list. These
    /// tests therefore check only that whatever it does say stays true. An
    /// earlier version asserted the reverse - requiring every vocabulary term to
    /// be named - which enforced a documentation policy the repository had moved
    /// away from and broke whenever the README was trimmed.
    /// </para>
    /// <para>
    /// The authoritative lists live in the code:
    /// <see cref="LoRaWANForm.UnsupportedTerms"/> and
    /// <see cref="LoRaWANForm.WithdrawnTerms"/>.
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
        public void The_readme_does_not_describe_a_supported_term_as_rejected()
        {
            // The README may mention any subset of the vocabulary, but a term it
            // calls out as "not decoded yet" must genuinely be rejected -
            // otherwise a reader goes looking for a workaround they do not need.
            string readme = ReadReadme();

            int noteStart = readme.IndexOf("not decoded yet", StringComparison.Ordinal);

            if (noteStart < 0)
            {
                // The README makes no such claim, so there is nothing to verify.
                return;
            }

            int noteEnd = readme.IndexOf("\n\n", noteStart, StringComparison.Ordinal);
            string note = noteEnd < 0 ? readme[noteStart..] : readme[noteStart..noteEnd];

            List<string> wronglyClaimed = Regex
                .Matches(note, @"`(lorav:[A-Za-z]+)`")
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .Where(term => !LoRaWANForm.UnsupportedTerms.Contains(term))
                .ToList();

            Assert.True(
                wronglyClaimed.Count == 0,
                "The README calls these terms undecoded, but the driver accepts them: "
                    + string.Join(", ", wronglyClaimed));
        }

        [Fact]
        public void Any_binding_term_the_readme_names_is_one_the_binding_defines()
        {
            // Catches a typo or an invented term in the documentation, which
            // would send a reader writing a Thing Description that silently
            // never matches.
            string readme = ReadReadme();

            HashSet<string> known = new(StringComparer.Ordinal);

            foreach (string term in LoRaWANForm.UnsupportedTerms)
            {
                known.Add(term);
            }

            foreach (string term in LoRaWANForm.WithdrawnTerms.Keys)
            {
                known.Add(term);
            }

            foreach (string term in ModelledTerms())
            {
                known.Add(term);
            }

            List<string> unknown = Regex
                .Matches(readme, @"`(lorav:[A-Za-z]+)`")
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .Where(term => !known.Contains(term))
                .ToList();

            Assert.True(
                unknown.Count == 0,
                "The README names these terms, which the binding does not define: "
                    + string.Join(", ", unknown));
        }

        /// <summary>
        /// The <c>lorav:</c> terms the driver binds, read from the model types
        /// so this cannot drift from the implementation.
        /// </summary>
        private static IEnumerable<string> ModelledTerms()
        {
            foreach (Type type in new[] { typeof(LoRaWANForm), typeof(LoRaWANThingDescription) })
            {
                foreach (PropertyInfo property in type.GetProperties())
                {
                    JsonPropertyAttribute attribute =
                        property.GetCustomAttribute<JsonPropertyAttribute>();

                    if (attribute?.PropertyName?.StartsWith("lorav:", StringComparison.Ordinal) == true)
                    {
                        yield return attribute.PropertyName;
                    }
                }
            }
        }

        [Fact]
        public void Every_withdrawn_term_names_a_replacement()
        {
            // The rejection message tells an author what to write instead, so an
            // empty replacement would make it unactionable. This is a property
            // of the code, independent of what the README chooses to document.
            foreach (KeyValuePair<string, string> entry in LoRaWANForm.WithdrawnTerms)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(entry.Value),
                    $"'{entry.Key}' is withdrawn but names no replacement.");
            }
        }

        [Fact]
        public void No_term_is_both_withdrawn_and_unsupported()
        {
            // A term in both lists would produce whichever message happened to
            // be checked first, which is order-dependent and confusing.
            foreach (string term in LoRaWANForm.UnsupportedTerms)
            {
                Assert.False(
                    LoRaWANForm.WithdrawnTerms.ContainsKey(term),
                    $"'{term}' appears in both UnsupportedTerms and WithdrawnTerms.");
            }
        }
    }
}
