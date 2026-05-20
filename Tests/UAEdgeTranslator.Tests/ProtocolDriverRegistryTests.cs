namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Xunit;

    public class ProtocolDriverRegistryTests
    {
        [Fact]
        public void Register_then_TryGetByUri_returns_the_driver()
        {
            ProtocolDriverRegistry registry = new();
            MockProtocolDriver driver = new();
            registry.Register(driver);

            bool found = registry.TryGetByUri("mock://device-1:1502/1", out var resolved);

            Assert.True(found);
            Assert.Same(driver, resolved);
        }

        [Fact]
        public void TryGetByUri_is_case_insensitive_on_scheme()
        {
            ProtocolDriverRegistry registry = new();
            registry.Register(new MockProtocolDriver());

            Assert.True(registry.TryGetByUri("MOCK://device-1:1502/1", out var resolved));
            Assert.NotNull(resolved);
            Assert.Equal(MockProtocolDriver.MockScheme, resolved.Scheme);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("no-scheme-here")]
        [InlineData("://missing-scheme")]
        public void TryGetByUri_rejects_malformed_or_missing_uri(string uri)
        {
            ProtocolDriverRegistry registry = new();
            registry.Register(new MockProtocolDriver());

            Assert.False(registry.TryGetByUri(uri, out var resolved));
            Assert.Null(resolved);
        }

        [Fact]
        public void TryGetByUri_returns_false_when_scheme_is_unknown()
        {
            ProtocolDriverRegistry registry = new();
            registry.Register(new MockProtocolDriver());

            Assert.False(registry.TryGetByUri("modbus+tcp://1.2.3.4:502/1", out var resolved));
            Assert.Null(resolved);
        }

        [Fact]
        public void Register_with_same_scheme_overrides_previous_entry()
        {
            ProtocolDriverRegistry registry = new();
            MockProtocolDriver first = new();
            MockProtocolDriver second = new();

            registry.Register(first);
            registry.Register(second);

            Assert.True(registry.TryGetByUri("mock://x:1/1", out var resolved));
            Assert.Same(second, resolved);
            Assert.NotSame(first, resolved);
        }

        [Fact]
        public void AllDrivers_returns_every_registered_driver()
        {
            ProtocolDriverRegistry registry = new();
            registry.Register(new MockProtocolDriver());

            int count = 0;
            foreach (var driver in registry.AllDrivers)
            {
                count++;
                Assert.False(string.IsNullOrWhiteSpace(driver.Scheme));
                Assert.False(string.IsNullOrWhiteSpace(driver.WoTBindingUri));
            }

            Assert.Equal(1, count);
        }
    }
}
