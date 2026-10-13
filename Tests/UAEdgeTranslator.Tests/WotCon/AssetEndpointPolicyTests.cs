namespace Opc.Ua.Edge.Translator.Tests.WotCon
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Opc.Ua.Edge.Translator.WotCon;
    using System;
    using System.Diagnostics;
    using System.Net;
    using Xunit;

    /// <summary>
    /// The cloud instance-metadata service must stay unreachable however its host
    /// name is spelled, including the fully qualified form with a trailing root dot.
    /// </summary>
    public sealed class AssetEndpointPolicyTests
    {
        [Theory]
        [InlineData("metadata.google.internal")]
        [InlineData("metadata.google.internal.")]
        [InlineData("METADATA.GOOGLE.INTERNAL.")]
        [InlineData("169.254.169.254")]
        [InlineData("[fd00:ec2::254]")]
        [InlineData("[::ffff:169.254.169.254]")]
        [InlineData("::ffff:a9fe:a9fe")]
        [InlineData("[fd00:0ec2:0000:0000:0000:0000:0000:0254]")]
        [InlineData("FD00:EC2:0:0:0:0:0:254")]
        public void Metadata_hosts_are_always_blocked(string host)
        {
            Assert.True(AssetEndpointPolicy.IsAlwaysBlockedHost(host));
        }

        [Theory]
        [InlineData("plc.plant.local")]
        [InlineData("plc.plant.local.")]
        [InlineData("192.168.1.10")]
        public void Plant_hosts_are_allowed(string host)
        {
            Assert.False(AssetEndpointPolicy.IsAlwaysBlockedHost(host));
        }

        [Theory]
        [InlineData("mock://metadata.google.internal:80")]
        [InlineData("mock://metadata.google.internal.:80")]
        [InlineData("mock://[::ffff:169.254.169.254]:80")]
        [InlineData("mock://[fd00:0ec2:0:0:0:0:0:254]:80")]
        public void Endpoints_naming_the_metadata_host_are_rejected(string endpoint)
        {
            ProtocolDriverRegistry drivers = new();
            drivers.Register(new MockProtocolDriver());

            ServiceResult result = AssetEndpointPolicy.Validate(endpoint, drivers);

            Assert.Equal(StatusCodes.BadSecurityChecksFailed, result.StatusCode.Code);
        }

        [Theory]
        [InlineData("mock://[::ffff:169.254.169.254]:80/1")]
        [InlineData("mock://[fd00:0ec2:0:0:0:0:0:254]:80/1")]
        public void Activation_rejects_equivalent_spellings_of_a_blocked_address(string endpoint)
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => AssetEndpointPolicy.ThrowIfBlocked(endpoint));

            Assert.Equal(StatusCodes.BadSecurityChecksFailed, ex.StatusCode);
        }

        [Fact]
        public void A_name_resolving_to_a_blocked_address_is_blocked()
        {
            Func<string, IPAddress[]> previous = AssetEndpointPolicy.ResolveHost;
            AssetEndpointPolicy.ResolveHost = host => host == "alias.example"
                ? new[] { IPAddress.Parse("169.254.169.254") }
                : Array.Empty<IPAddress>();
            try
            {
                Assert.True(AssetEndpointPolicy.IsAlwaysBlockedHost("alias.example"));
                Assert.False(AssetEndpointPolicy.IsAlwaysBlockedHost("plc.example"));
            }
            finally
            {
                AssetEndpointPolicy.ResolveHost = previous;
            }
        }

        [Fact]
        public void The_real_resolver_answers_within_its_timeout()
        {
            // The one test that performs a real DNS lookup.
            Stopwatch watch = Stopwatch.StartNew();
            IPAddress[] addresses = AssetEndpointPolicy.ResolveWithTimeout("localhost");
            watch.Stop();

            Assert.NotEmpty(addresses);
            Assert.True(watch.Elapsed < AssetEndpointPolicy.DnsTimeout + TimeSpan.FromSeconds(1), $"lookup took {watch.Elapsed}");
        }
    }
}
