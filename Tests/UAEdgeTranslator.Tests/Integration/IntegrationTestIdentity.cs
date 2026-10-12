namespace Opc.Ua.Edge.Translator.Tests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Opc.Ua;
    using Opc.Ua.Configuration;
    using Opc.Ua.Identity;
    using Opc.Ua.Server;

    /// <summary>
    /// Test-only identity configuration for the integration fixtures.
    /// </summary>
    internal static class IntegrationTestIdentity
    {
        /// <summary>
        /// Lets the integration suite open anonymous sessions against a fixture server.
        /// </summary>
        /// <remarks>
        /// Production <see cref="UAServer"/> rejects anonymous tokens (see
        /// <see cref="UAServer.RegisterIdentityAuthenticators"/>), which is the right
        /// contract for production but blocks the suite from exercising the address
        /// space directly. Each fixture runs its own server behind a per-fixture
        /// loopback port and private PKI directory, so relaxing this is safe here.
        /// <para>
        /// SDK 2.0.0 dispatches authentication through
        /// <see cref="IServerIdentityRegistry"/>, and <c>Register</c> replaces any
        /// existing registration for the same token type, so registering an accepting
        /// anonymous authenticator overrides the production rejection on this server
        /// instance only. This replaces the previous approach of clearing the
        /// compiler-generated <c>ImpersonateUser</c> event field via reflection, which
        /// was version-fragile and swallowed failures; this uses the public API and
        /// fails loudly if the server is not available.
        /// </para>
        /// </remarks>
        public static void AllowAnonymousSessions(ApplicationInstance app)
        {
            ArgumentNullException.ThrowIfNull(app);

            if (app.Server is not StandardServer server || server.CurrentInstance == null)
            {
                throw new InvalidOperationException(
                    "The OPC UA server must be started before anonymous sessions can be enabled for tests.");
            }

            server.CurrentInstance.IdentityRegistry.Register(new AcceptAnonymousAuthenticator());

            // The fixtures use SecurityPolicy None and anonymous sessions, which the
            // WoT-Con management authorization correctly refuses in production.
            Opc.Ua.Edge.Translator.WotCon.AssetManagementAuthorization.DisabledForTests = true;
        }

        private sealed class AcceptAnonymousAuthenticator : IUserTokenAuthenticator
        {
            public UserTokenType TokenType => UserTokenType.Anonymous;

            public string IssuedTokenProfileUri => null;

            public ValueTask<AuthenticationResult> AuthenticateAsync(AuthenticationContext context, CancellationToken ct)
            {
                return ValueTask.FromResult(AuthenticationResult.Accept(new UserIdentity()));
            }
        }
    }
}
