
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Opc.Ua.Identity;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public partial class UAServer : ReverseConnectServer
    {
        /// <summary>
        /// Constructor for a server started directly through an ApplicationInstance
        /// rather than the generic host; registers the UA Edge Translator node manager itself.
        /// </summary>
        public UAServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            AddNodeManager(new UANodeManagerFactory());
        }

        /// <summary>
        /// Constructor used by the generic host's server factory. The UA Edge Translator
        /// node manager is registered with the host through <see cref="UANodeManagerFactory"/>.
        /// </summary>
        [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
        public UAServer(ITelemetryContext telemetry, TimeProvider timeProvider, HostedOpcUaApplication application)
            : base(telemetry, timeProvider)
        {
            ArgumentNullException.ThrowIfNull(application);
            application.Server = this;
        }

        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties
            {
                ManufacturerName = "OPC Foundation",
                ProductName = "UA Edge Translator",
                ProductUri = "http://opcfoundation.org/UA/EdgeTranslator",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };

            return properties;
        }

        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            base.OnServerStarting(configuration);

            // In SDK 2.0 the CertificateValidation event was replaced by a per-error
            // AcceptError callback on the certificate manager, which exists by now.
            if (configuration?.CertificateManager != null)
            {
                configuration.CertificateManager.AcceptError = Program.OPCUAClientCertificateAcceptError;
            }
        }

        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            RegisterIdentityAuthenticators(server.IdentityRegistry);
        }

        /// <summary>
        /// Installs the server's user-authentication policy into <paramref name="registry"/>.
        /// </summary>
        /// <remarks>
        /// SDK 2.0.0 replaced the <c>SessionManager.ImpersonateUser</c> event with an
        /// <see cref="IServerIdentityRegistry"/> that dispatches by token type. This
        /// reproduces the previous policy exactly: only username/password tokens can
        /// authenticate, and every other token type is rejected with
        /// <c>BadIdentityTokenInvalid</c>.
        /// <para>
        /// Each token type is registered explicitly rather than relying on defaults,
        /// for two reasons that are easy to get wrong:
        /// </para>
        /// <list type="bullet">
        /// <item><see cref="IServerIdentityRegistry.Register"/> <em>replaces</em> any
        /// existing registration for that token type, so this deterministically
        /// overrides any authenticator the SDK pre-registered. In particular the SDK's
        /// default anonymous authenticator accepts every anonymous token.</item>
        /// <item>A token type with no authenticator yields <c>NotHandled</c>, which
        /// falls back to the legacy <c>ImpersonateUser</c> event. Returning an explicit
        /// rejection stops dispatch there, so that fallback is never reached.</item>
        /// </list>
        /// <para>
        /// This deliberately does not depend on which token policies the endpoint
        /// advertises: today only <c>UserName_1</c> is configured, but if an Anonymous
        /// policy were added later it must still be rejected in code, as it was before.
        /// </para>
        /// </remarks>
        public static void RegisterIdentityAuthenticators(IServerIdentityRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new UserNameAuthenticator());
            registry.Register(new UnsupportedTokenTypeAuthenticator(UserTokenType.Anonymous));
            registry.Register(new UnsupportedTokenTypeAuthenticator(UserTokenType.Certificate));
            registry.Register(new UnsupportedTokenTypeAuthenticator(UserTokenType.IssuedToken));
        }

        /// <summary>
        /// Authenticates username/password tokens against the credentials configured via
        /// <c>OPCUA_USERNAME</c> / <c>OPCUA_PASSWORD</c>.
        /// </summary>
        public sealed class UserNameAuthenticator : IUserTokenAuthenticator
        {
            /// <summary>
            /// Wraps this authenticator in the SDK's <see cref="UserNamePasswordAuthenticator"/>.
            /// The hosted server's startup check only recognizes that type as covering a
            /// UserName token policy; registering this class directly makes it warn that
            /// the policy has no matching authenticator.
            /// </summary>
            public static UserNamePasswordAuthenticator CreateHosted()
            {
                return new UserNamePasswordAuthenticator((token, ct) =>
                {
                    AuthenticationResult result = VerifyPassword(token);
                    if (result.Outcome != AuthenticationOutcome.Accepted)
                    {
                        throw new ServiceResultException(result.Error ?? new ServiceResult(StatusCodes.BadUserAccessDenied));
                    }

                    return ValueTask.FromResult(result.Identity);
                });
            }

            public UserTokenType TokenType => UserTokenType.UserName;

            public string IssuedTokenProfileUri => null;

            public ValueTask<AuthenticationResult> AuthenticateAsync(AuthenticationContext context, CancellationToken ct)
            {
                ArgumentNullException.ThrowIfNull(context);

                // The registry dispatches by token type, so this is defensive: anything
                // that is not a username token is rejected exactly as before.
                if (context.TokenHandler is not UserNameIdentityTokenHandler userNameToken)
                {
                    return ValueTask.FromResult(Reject(StatusCodes.BadIdentityTokenInvalid,
                        $"Not supported user token type: {context.TokenHandler?.TokenType}."));
                }

                return ValueTask.FromResult(VerifyPassword(userNameToken));
            }

            private static AuthenticationResult VerifyPassword(UserNameIdentityTokenHandler userNameToken)
            {
                // The SDK now decrypts the token before invoking the authenticator, so the
                // cleartext password is exposed on the handler (it used to be read from
                // UserNameIdentityToken.DecryptedPassword, which no longer exists).
                string userName = userNameToken.UserName;
                string password = userNameToken.DecryptedPassword == null
                    ? null
                    : Encoding.UTF8.GetString(userNameToken.DecryptedPassword);

                if (string.IsNullOrEmpty(userName))
                {
                    return Reject(StatusCodes.BadIdentityTokenInvalid,
                        "Security token is not a valid username token. An empty username is not accepted.");
                }

                if (string.IsNullOrEmpty(password))
                {
                    return Reject(StatusCodes.BadIdentityTokenRejected,
                        "Security token is not a valid username token. An empty password is not accepted.");
                }

                string configuredUsername = Program.OpcUaUsername;
                string configuredPassword = Program.OpcUaPassword;
                if (string.IsNullOrEmpty(configuredUsername) || string.IsNullOrEmpty(configuredPassword))
                {
                    // Should be impossible — Program.ValidateRequiredEnvironment is
                    // called before the OPC UA stack is started — but defend against
                    // the property being cleared at runtime.
                    Log.Logger.Warning("Authentication rejected: OPC UA credentials are not configured.");
                    return Reject(StatusCodes.BadUserAccessDenied, userName);
                }

                // Constant-time comparison over SHA-256 hashes to avoid leaking
                // username/password length or content via timing side channels.
                if (FixedTimeEqualsHashed(userName, configuredUsername)
                 && FixedTimeEqualsHashed(password, configuredPassword))
                {
                    IUserIdentity identity = new SystemConfigurationIdentity(new UserIdentity(userNameToken));

                    Log.Logger.Information("Username Token Accepted: {0}", identity.DisplayName);
                    return AuthenticationResult.Accept(identity);
                }

                return Reject(StatusCodes.BadUserAccessDenied, userName);
            }
        }

        /// <summary>
        /// Rejects a token type the server does not support. Registered for every
        /// non-username token type; see <see cref="RegisterIdentityAuthenticators"/>.
        /// </summary>
        public sealed class UnsupportedTokenTypeAuthenticator : IUserTokenAuthenticator
        {
            public UnsupportedTokenTypeAuthenticator(UserTokenType tokenType)
            {
                TokenType = tokenType;
            }

            public UserTokenType TokenType { get; }

            public string IssuedTokenProfileUri => null;

            public ValueTask<AuthenticationResult> AuthenticateAsync(AuthenticationContext context, CancellationToken ct)
            {
                return ValueTask.FromResult(Reject(StatusCodes.BadIdentityTokenInvalid,
                    $"Not supported user token type: {TokenType}."));
            }
        }

        private static AuthenticationResult Reject(StatusCode statusCode, string message)
        {
            return AuthenticationResult.Reject(new ServiceResult(statusCode, new LocalizedText(message)));
        }

        private static bool FixedTimeEqualsHashed(string left, string right)
        {
            byte[] leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left ?? string.Empty));
            byte[] rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right ?? string.Empty));
            return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
        }
    }
}
