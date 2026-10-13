namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using Opc.Ua.Identity;
    using Opc.Ua.Server;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Covers the server's user-authentication policy, installed by
    /// <see cref="UAServer.RegisterIdentityAuthenticators"/>.
    /// <para>
    /// SDK 2.0.0 replaced the <c>SessionManager.ImpersonateUser</c> event with an
    /// <see cref="IServerIdentityRegistry"/>. These tests preserve every assertion of
    /// the previous <c>ImpersonateUser</c> tests (same accepted/rejected cases and the
    /// same status codes) and drive them through a real
    /// <see cref="ServerIdentityRegistry"/>, so the registry's dispatch rules are
    /// exercised rather than mocked.
    /// </para>
    /// Joined to the working-directory collection so the tests don't race the
    /// integration fixture, which mutates <c>Program.OpcUaUsername</c> /
    /// <c>Program.OpcUaPassword</c>.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class UAServerAuthenticationTests
    {
        [Fact]
        public async Task Accepts_valid_username_token_with_system_configuration_identity()
        {
            string previousUser = Program.OpcUaUsername;
            string previousPass = Program.OpcUaPassword;
            try
            {
                SetProgramCreds("alice", "secret");

                AuthenticationResult result = await AuthenticateAsync(UserNameToken("alice", "secret"), UserTokenType.UserName);

                Assert.Equal(AuthenticationOutcome.Accepted, result.Outcome);
                Assert.NotNull(result.Identity);

                // The previous implementation granted SystemConfigurationIdentity; role
                // mappings from the role manager are layered on top of it, so the
                // wrapper must survive the migration for the session to keep its role.
                Assert.IsType<SystemConfigurationIdentity>(result.Identity);
            }
            finally
            {
                SetProgramCreds(previousUser, previousPass);
            }
        }

        [Fact]
        public async Task Rejects_anonymous_token_with_BadIdentityTokenInvalid()
        {
            AuthenticationResult result = await AuthenticateAsync(new AnonymousIdentityTokenHandler(), UserTokenType.Anonymous);

            AssertRejected(result, StatusCodes.BadIdentityTokenInvalid);
        }

        [Fact]
        public async Task Rejects_anonymous_even_when_sdk_default_authenticators_were_registered_first()
        {
            // The SDK's RegisterDefaultAuthenticators installs an anonymous authenticator
            // that accepts every anonymous token. Prove that threat is real first, so this
            // test cannot pass vacuously...
            ServerIdentityRegistry registry = new();
            registry.RegisterDefaultAuthenticators(
                (_, _) => ValueTask.FromResult<IUserIdentity>(null),
                null,
                null);

            AuthenticationResult before = await registry.AuthenticateAsync(
                Context(new AnonymousIdentityTokenHandler(), UserTokenType.Anonymous), default);
            Assert.Equal(AuthenticationOutcome.Accepted, before.Outcome);

            // ...then confirm our policy overrides it, because Register replaces any
            // existing registration for the same token type.
            UAServer.RegisterIdentityAuthenticators(registry);

            AuthenticationResult after = await registry.AuthenticateAsync(
                Context(new AnonymousIdentityTokenHandler(), UserTokenType.Anonymous), default);
            AssertRejected(after, StatusCodes.BadIdentityTokenInvalid);
        }

        [Theory]
        [InlineData(UserTokenType.Certificate)]
        [InlineData(UserTokenType.IssuedToken)]
        public async Task Rejects_other_unsupported_token_types_with_BadIdentityTokenInvalid(UserTokenType tokenType)
        {
            UAServer.UnsupportedTokenTypeAuthenticator authenticator = new(tokenType);

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                Context(new AnonymousIdentityTokenHandler(), tokenType), default);

            Assert.Equal(tokenType, authenticator.TokenType);
            AssertRejected(result, StatusCodes.BadIdentityTokenInvalid);
        }

        [Fact]
        public async Task Rejects_wrong_credentials_with_BadUserAccessDenied()
        {
            string previousUser = Program.OpcUaUsername;
            string previousPass = Program.OpcUaPassword;
            try
            {
                SetProgramCreds("alice", "secret");

                AuthenticationResult result = await AuthenticateAsync(UserNameToken("alice", "wrong"), UserTokenType.UserName);

                AssertRejected(result, StatusCodes.BadUserAccessDenied);
            }
            finally
            {
                SetProgramCreds(previousUser, previousPass);
            }
        }

        [Fact]
        public async Task Rejects_empty_username_with_BadIdentityTokenInvalid()
        {
            AuthenticationResult result = await AuthenticateAsync(UserNameToken(string.Empty, "secret"), UserTokenType.UserName);

            AssertRejected(result, StatusCodes.BadIdentityTokenInvalid);
        }

        [Fact]
        public async Task Rejects_empty_password_with_BadIdentityTokenRejected()
        {
            AuthenticationResult result = await AuthenticateAsync(UserNameToken("alice", string.Empty), UserTokenType.UserName);

            AssertRejected(result, StatusCodes.BadIdentityTokenRejected);
        }

        [Fact]
        public async Task Rejects_when_credentials_are_not_configured_with_BadUserAccessDenied()
        {
            string previousUser = Program.OpcUaUsername;
            string previousPass = Program.OpcUaPassword;
            try
            {
                SetProgramCreds(null, null);

                AuthenticationResult result = await AuthenticateAsync(UserNameToken("alice", "secret"), UserTokenType.UserName);

                AssertRejected(result, StatusCodes.BadUserAccessDenied);
            }
            finally
            {
                SetProgramCreds(previousUser, previousPass);
            }
        }

        // ---- helpers ----

        private static async Task<AuthenticationResult> AuthenticateAsync(IUserIdentityTokenHandler token, UserTokenType tokenType)
        {
            ServerIdentityRegistry registry = new();
            UAServer.RegisterIdentityAuthenticators(registry);

            return await registry.AuthenticateAsync(Context(token, tokenType), default);
        }

        private static AuthenticationContext Context(IUserIdentityTokenHandler token, UserTokenType tokenType)
        {
            return new AuthenticationContext(
                token,
                new UserTokenPolicy(tokenType),
                new EndpointDescription(),
                ServiceMessageContext.CreateEmpty(null),
                null,
                null);
        }

        private static UserNameIdentityTokenHandler UserNameToken(string user, string password)
        {
            return new UserNameIdentityTokenHandler(user, Encoding.UTF8.GetBytes(password), null);
        }

        private static void AssertRejected(AuthenticationResult result, StatusCode expected)
        {
            Assert.Equal(AuthenticationOutcome.Rejected, result.Outcome);
            Assert.Null(result.Identity);
            Assert.NotNull(result.Error);
            Assert.Equal(expected, result.Error.StatusCode);
        }

        private static void SetProgramCreds(string user, string pass)
        {
            typeof(Program).GetProperty("OpcUaUsername", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, user);
            typeof(Program).GetProperty("OpcUaPassword", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, pass);
        }
    }
}
