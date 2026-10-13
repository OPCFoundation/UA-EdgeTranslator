namespace Opc.Ua.Edge.Translator.Tests.WotCon
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Opc.Ua.Edge.Translator.WotCon;
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
        public void Endpoints_naming_the_metadata_host_are_rejected(string endpoint)
        {
            ProtocolDriverRegistry drivers = new();
            drivers.Register(new MockProtocolDriver());

            ServiceResult result = AssetEndpointPolicy.Validate(endpoint, drivers);

            Assert.Equal(StatusCodes.BadSecurityChecksFailed, result.StatusCode.Code);
        }
    }
}
