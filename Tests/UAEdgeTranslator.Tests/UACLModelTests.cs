namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using Opc.Ua.Cloud.Library.Models;
    using Xunit;

    public class UACLModelTests
    {
        [Fact]
        public void UANameSpace_default_construction_initializes_collections()
        {
            UANameSpace ns = new();

            Assert.Equal(string.Empty, ns.Title);
            Assert.Equal(License.Custom, ns.License);
            Assert.Equal(string.Empty, ns.CopyrightText);
            Assert.NotNull(ns.Contributor);
            Assert.NotNull(ns.Category);
            Assert.NotNull(ns.Nodeset);
            Assert.NotNull(ns.Keywords);
            Assert.Empty(ns.Keywords);
            Assert.NotNull(ns.SupportedLocales);
            Assert.Empty(ns.SupportedLocales);
            Assert.Equal(0u, ns.NumberOfDownloads);
            Assert.Null(ns.AdditionalProperties);
            Assert.Null(ns.DocumentationUrl);
            Assert.Null(ns.IconUrl);
            Assert.Null(ns.LicenseUrl);
            Assert.Null(ns.PurchasingInformationUrl);
            Assert.Null(ns.ReleaseNotesUrl);
            Assert.Null(ns.TestSpecificationUrl);
        }

        [Fact]
        public void UANameSpace_setters_round_trip()
        {
            UANameSpace ns = new()
            {
                Title = "T",
                License = License.MIT,
                CopyrightText = "(c)",
                Description = "desc",
                Keywords = new[] { "a", "b" },
                SupportedLocales = new[] { "en" },
                NumberOfDownloads = 42,
                ValidationStatus = "ok",
                AdditionalProperties = new[] { new UAProperty { Name = "k", Value = "v" } },
                DocumentationUrl = new Uri("https://example.com/docs"),
                IconUrl = new Uri("https://example.com/i.png"),
                LicenseUrl = new Uri("https://example.com/l"),
                PurchasingInformationUrl = new Uri("https://example.com/p"),
                ReleaseNotesUrl = new Uri("https://example.com/r"),
                TestSpecificationUrl = new Uri("https://example.com/t")
            };

            Assert.Equal("T", ns.Title);
            Assert.Equal(License.MIT, ns.License);
            Assert.Equal("(c)", ns.CopyrightText);
            Assert.Equal("desc", ns.Description);
            Assert.Equal(new[] { "a", "b" }, ns.Keywords);
            Assert.Equal(new[] { "en" }, ns.SupportedLocales);
            Assert.Equal(42u, ns.NumberOfDownloads);
            Assert.Equal("ok", ns.ValidationStatus);
            Assert.Single(ns.AdditionalProperties);
            Assert.Equal("k", ns.AdditionalProperties[0].Name);
            Assert.Equal("v", ns.AdditionalProperties[0].Value);
            Assert.Equal("https://example.com/docs", ns.DocumentationUrl.ToString());
            Assert.Equal("https://example.com/i.png", ns.IconUrl.ToString());
            Assert.Equal("https://example.com/l", ns.LicenseUrl.ToString());
            Assert.Equal("https://example.com/p", ns.PurchasingInformationUrl.ToString());
            Assert.Equal("https://example.com/r", ns.ReleaseNotesUrl.ToString());
            Assert.Equal("https://example.com/t", ns.TestSpecificationUrl.ToString());
        }

        [Fact]
        public void Organisation_default_construction_uses_empty_name()
        {
            Organisation org = new();
            Assert.Equal(string.Empty, org.Name);
            Assert.Null(org.Description);
            Assert.Null(org.LogoUrl);
            Assert.Null(org.ContactEmail);
            Assert.Null(org.Website);
        }

        [Fact]
        public void Organisation_equals_compares_name_only()
        {
            Organisation a = new() { Name = "ACME", Description = "x", ContactEmail = "a@b.c" };
            Organisation b = new() { Name = "ACME", Description = "y" };
            Organisation c = new() { Name = "Other" };

            Assert.True(a.Equals(b));
            Assert.False(a.Equals(c));
            Assert.False(a.Equals(null));
            Assert.False(a.Equals("ACME"));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Category_default_construction_uses_empty_name()
        {
            Category cat = new();
            Assert.Equal(string.Empty, cat.Name);
            Assert.Null(cat.Description);
            Assert.Null(cat.IconUrl);
        }

        [Fact]
        public void Category_equals_compares_name_only()
        {
            Category a = new() { Name = "Sensors" };
            Category b = new() { Name = "Sensors", Description = "ignored" };
            Category c = new() { Name = "Actuators" };

            Assert.True(a.Equals(b));
            Assert.False(a.Equals(c));
            Assert.False(a.Equals(null));
            Assert.False(a.Equals(42));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Nodeset_default_construction_uses_safe_defaults()
        {
            Nodeset ns = new();
            Assert.Equal(string.Empty, ns.NodesetXml);
            Assert.Equal(0u, ns.Identifier);
            Assert.Null(ns.NamespaceUri);
            Assert.Equal(string.Empty, ns.Version);
            Assert.Equal(DateTime.MinValue, ns.PublicationDate);
            Assert.Equal(DateTime.MinValue, ns.LastModifiedDate);
        }

        [Fact]
        public void Nodeset_setters_round_trip()
        {
            DateTime when = new(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            Nodeset ns = new()
            {
                NodesetXml = "<xml/>",
                Identifier = 99,
                NamespaceUri = new Uri("urn:test"),
                Version = "1.2.3",
                PublicationDate = when,
                LastModifiedDate = when
            };

            Assert.Equal("<xml/>", ns.NodesetXml);
            Assert.Equal(99u, ns.Identifier);
            Assert.Equal(new Uri("urn:test"), ns.NamespaceUri);
            Assert.Equal("1.2.3", ns.Version);
            Assert.Equal(when, ns.PublicationDate);
            Assert.Equal(when, ns.LastModifiedDate);
        }
    }
}
