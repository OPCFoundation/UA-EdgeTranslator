namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.Models;
    using Xunit;

    public class AssetTagTests
    {
        [Fact]
        public void Defaults_match_documented_contract()
        {
            AssetTag tag = new();

            Assert.Null(tag.Name);
            Assert.Equal(0, tag.UnitID);
            Assert.Null(tag.Address);
            Assert.Null(tag.Type);
            Assert.False(tag.IsBigEndian);
            Assert.False(tag.SwapPerWord);
            Assert.Equal(1.0f, tag.Multiplier);
            Assert.Null(tag.BitMask);
            Assert.Equal(1000, tag.PollingInterval);
            Assert.Null(tag.Entity);
            Assert.Null(tag.MappedUAExpandedNodeID);
            Assert.Null(tag.MappedUAFieldPath);
        }

        [Fact]
        public void Properties_round_trip_assigned_values()
        {
            AssetTag tag = new()
            {
                Name = "temperature",
                UnitID = 7,
                Address = "/temp",
                Type = "xsd:float",
                IsBigEndian = true,
                SwapPerWord = true,
                Multiplier = 2.5f,
                BitMask = "0xFF00",
                PollingInterval = 250,
                Entity = "holding",
                MappedUAExpandedNodeID = "ns=2;s=temperature",
                MappedUAFieldPath = "value"
            };

            Assert.Equal("temperature", tag.Name);
            Assert.Equal(7, tag.UnitID);
            Assert.Equal("/temp", tag.Address);
            Assert.Equal("xsd:float", tag.Type);
            Assert.True(tag.IsBigEndian);
            Assert.True(tag.SwapPerWord);
            Assert.Equal(2.5f, tag.Multiplier);
            Assert.Equal("0xFF00", tag.BitMask);
            Assert.Equal(250, tag.PollingInterval);
            Assert.Equal("holding", tag.Entity);
            Assert.Equal("ns=2;s=temperature", tag.MappedUAExpandedNodeID);
            Assert.Equal("value", tag.MappedUAFieldPath);
        }
    }
}
