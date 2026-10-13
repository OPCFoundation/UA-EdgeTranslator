namespace Opc.Ua.Edge.Translator.Tests.WotCon
{
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.WotCon;
    using Xunit;

    /// <summary>
    /// A TD's base is not a unique Thing identity: two registry resources may describe
    /// different Things on the same endpoint, and each must keep its own TD.
    /// </summary>
    public sealed class ThingDescriptionCacheTests
    {
        private const string Base = "mock://device-1:1502/1";

        [Fact]
        public void Things_sharing_a_base_keep_their_own_description()
        {
            ThingDescriptionCache cache = new();
            cache.Set("/groups/td/resources/pump", Td("pump"));
            cache.Set("/groups/td/resources/valve", Td("valve"));

            Assert.Equal("pump", cache.GetOrCreate("/groups/td/resources/pump", Base).Name);
            Assert.Equal("valve", cache.GetOrCreate("/groups/td/resources/valve", Base).Name);
        }

        [Fact]
        public void An_unknown_resource_falls_back_to_the_base_then_to_a_minimal_description()
        {
            ThingDescriptionCache cache = new();
            cache.Set(Td("pump"));

            Assert.Equal("pump", cache.GetOrCreate("/groups/td/resources/unknown", Base).Name);

            ThingDescription minimal = cache.GetOrCreate(null, "mock://other-host:1502/1");
            Assert.Equal("other-host", minimal.Name);
        }

        private static string Td(string name) =>
            "{ \"@context\": [\"https://www.w3.org/2022/wot/td/v1.1\"], \"@type\": [\"Thing\"], \"id\": \"urn:" + name + "\", \"name\": \"" + name + "\", \"title\": \"" + name + "\", \"base\": \"" + Base + "\" }";
    }
}
