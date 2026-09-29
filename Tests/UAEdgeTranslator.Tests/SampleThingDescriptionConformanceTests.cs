namespace Opc.Ua.Edge.Translator.Tests
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// Guards the W3C WoT TD 1.1 conformance of every shipped Thing Description.
    /// <para>
    /// The translator's own parser is tolerant, so a malformed sample still
    /// onboards and nothing fails at runtime. A standards-compliant WoT consumer
    /// is not tolerant: it validates against the official JSON Schema and
    /// rejects the document outright. These tests pin the violations that the
    /// generators used to emit, so a regression is caught here rather than by a
    /// third party consuming our TDs.
    /// </para>
    /// <para>
    /// This is a targeted stand-in for full schema validation, which would need
    /// a JSON Schema dependency the project does not currently take.
    /// </para>
    /// </summary>
    public class SampleThingDescriptionConformanceTests
    {
        public static IEnumerable<object[]> SampleFiles()
        {
            foreach (string file in Directory.EnumerateFiles(FindRepoFolder("Samples"), "*.td.jsonld"))
            {
                yield return new object[] { Path.GetFileName(file) };
            }
        }

        [Theory]
        [MemberData(nameof(SampleFiles))]
        public void A_sample_is_parseable_without_a_byte_order_mark(string fileName)
        {
            // A UTF-8 BOM is not valid JSON. Newtonsoft skips it, but strict
            // parsers (including the W3C validator) reject the file outright.
            byte[] head = new byte[3];
            using (FileStream stream = File.OpenRead(Path.Combine(FindRepoFolder("Samples"), fileName)))
            {
                int read = stream.Read(head, 0, 3);
                Assert.Equal(3, read);
            }

            Assert.False(
                head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF,
                $"'{fileName}' starts with a UTF-8 BOM, which strict JSON parsers reject.");
        }

        [Theory]
        [MemberData(nameof(SampleFiles))]
        public void A_sample_never_emits_a_null_for_an_optional_term(string fileName)
        {
            // TD 1.1 types 'op' as a string or array of strings and 'forms' as
            // an array. An explicit JSON null satisfies neither: an absent term
            // is valid, a null one is not. Optional members must therefore be
            // omitted, which is what NullValueHandling.Ignore does on the model.
            JObject td = ParseSample(fileName);

            foreach (JProperty property in td.Descendants().OfType<JProperty>())
            {
                if (property.Name is "op" or "forms")
                {
                    Assert.False(
                        property.Value.Type == JTokenType.Null,
                        $"'{fileName}' emits \"{property.Name}\": null at '{property.Path}'. " +
                        "Omit the term instead of writing a JSON null.");
                }
            }
        }

        [Theory]
        [MemberData(nameof(SampleFiles))]
        public void A_sample_declares_an_id_that_is_a_valid_uri(string fileName)
        {
            // TD 1.1 requires 'id' to be a URI. Asset names routinely contain
            // spaces, so the id must be percent-encoded; see ThingDescriptionId.
            JObject td = ParseSample(fileName);

            string id = (string)td["id"];
            if (id is null)
            {
                return; // 'id' is optional.
            }

            Assert.True(
                Uri.IsWellFormedUriString(id, UriKind.Absolute),
                $"'{fileName}' declares id '{id}', which is not a well-formed URI.");
        }

        [Theory]
        [MemberData(nameof(SampleFiles))]
        public void A_sample_gives_every_action_a_form(string fileName)
        {
            // An ActionAffordance requires 'forms'. Unlike properties, actions
            // have no constant form, so there is no reason to omit it.
            JObject td = ParseSample(fileName);

            foreach (JProperty action in (td["actions"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                JToken forms = action.Value["forms"];

                Assert.True(
                    forms is JArray { Count: > 0 },
                    $"'{fileName}' action '{action.Name}' has no forms; TD 1.1 requires at least one.");
            }
        }

        [Theory]
        [MemberData(nameof(SampleFiles))]
        public void A_sample_property_without_forms_is_a_constant(string fileName)
        {
            // UANodeManager treats a property with no forms as a constant (see
            // AddConstantProperty, the else branch of the forms check), so the
            // absence of 'forms' is load-bearing here and cannot simply be
            // filled in. It is only defensible when 'const' supplies the value.
            JObject td = ParseSample(fileName);

            foreach (JProperty property in (td["properties"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                JToken forms = property.Value["forms"];
                if (forms is JArray { Count: > 0 })
                {
                    continue;
                }

                Assert.True(
                    property.Value["const"] is not null,
                    $"'{fileName}' property '{property.Name}' has neither forms nor a const value, " +
                    "so it is neither readable nor constant.");
            }
        }

        [Theory]
        [MemberData(nameof(SampleFiles))]
        public void A_sample_form_agrees_with_the_type_of_its_property(string fileName)
        {
            // The form's xsd type drives AssetTag.Type, so a form that
            // contradicts its property creates a tag of the wrong type.
            JObject td = ParseSample(fileName);

            foreach (JProperty property in (td["properties"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                string declared = (string)property.Value["type"];
                if (declared is null || property.Value["forms"] is not JArray forms)
                {
                    continue;
                }

                foreach (JToken form in forms)
                {
                    string formType = (string)form["type"];
                    if (formType is null)
                    {
                        continue;
                    }

                    // Compare on the bare name: the drivers spell the same
                    // type both as "xsd:boolean" and as "Boolean", and both
                    // deserialize to the same TypeString member.
                    string bare = (formType.StartsWith("xsd:", StringComparison.Ordinal)
                        ? formType.Substring(4)
                        : formType).ToLowerInvariant();

                    bool numeric = bare is "float" or "double" or "short" or "integer"
                        or "byte" or "long" or "unsignedlong";
                    bool boolean = bare is "boolean";

                    // A blocklist, not an allowlist: a string property may
                    // legitimately carry dateTime, duration or timedCommand,
                    // but never a numeric or boolean form.
                    bool agrees = declared switch
                    {
                        "string" => !numeric && !boolean,
                        "boolean" => boolean,
                        "number" or "integer" => !boolean && bare is not "string",
                        _ => true,
                    };

                    Assert.True(
                        agrees,
                        $"'{fileName}' property '{property.Name}' is declared '{declared}' " +
                        $"but its form says '{formType}'.");
                }
            }
        }

        private static JObject ParseSample(string fileName)
        {
            string path = Path.Combine(FindRepoFolder("Samples"), fileName);

            // Trim the BOM so this helper still works for files the BOM test
            // reports on; that violation is asserted separately.
            return JsonConvert.DeserializeObject<JObject>(File.ReadAllText(path).Trim('﻿'));
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
