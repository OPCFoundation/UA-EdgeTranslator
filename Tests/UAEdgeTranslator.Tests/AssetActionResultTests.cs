namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Edge.Translator.Models;
    using System.Collections.Generic;
    using Xunit;

    /// <summary>
    /// A driver can report a successful action with any message, such as an HTTP
    /// response body, without it being taken for an error.
    /// </summary>
    public sealed class AssetActionResultTests
    {
        [Theory]
        [InlineData("ok")]
        [InlineData("OK")]
        [InlineData("success")]
        public void The_legacy_status_words_mean_success(string status)
        {
            Assert.True(AssetActionResult.FromStatus(status).IsSuccess);
        }

        [Theory]
        [InlineData("mock:reset:ok")]
        [InlineData("No Modbus form found for action 'x'.")]
        [InlineData("")]
        [InlineData(null)]
        public void Any_other_unflagged_status_is_a_failure(string status)
        {
            Assert.False(AssetActionResult.FromStatus(status).IsSuccess);
        }

        [Fact]
        public void An_explicit_success_keeps_its_message_and_outputs()
        {
            AssetActionResult result = AssetActionResult.Success("{\"state\":\"done\"}", new List<object> { 1 });

            Assert.True(result.IsSuccess);
            Assert.Equal("{\"state\":\"done\"}", result.Status);
            Assert.Single(result.Outputs);
        }

        [Fact]
        public void An_explicit_failure_overrides_a_success_word()
        {
            Assert.False(AssetActionResult.Failure("ok").IsSuccess);
        }
    }
}
