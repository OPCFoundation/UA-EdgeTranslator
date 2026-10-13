namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Opc.Ua.Cloud;
    using Opc.Ua.Configuration;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Security.Certificates;
    using Opc.Ua.Server;
    using Serilog;
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    public class Program
    {
        public static ApplicationInstance App { get; private set; }

        private static readonly TaskCompletionSource s_serverStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Completes once the OPC UA server is running, i.e. <see cref="App"/> and its
        /// application certificate are available. Protocol drivers are loaded before the
        /// server starts, so anything they start that needs the certificate awaits this.
        /// </summary>
        public static Task ServerStarted => s_serverStarted.Task;

        public static ProtocolDriverRegistry Drivers { get; } = new();

        public static ConsoleTelemetry Telemetry { get; private set; } = new();

        // OPCUA_USERNAME / OPCUA_PASSWORD are validated and snapshotted at
        // startup (see ValidateRequiredEnvironment) so they cannot change at
        // runtime, and so SessionManager_ImpersonateUser doesn't have to take
        // an environment-variable read on every login attempt.
        public static string OpcUaUsername { get; private set; }

        public static string OpcUaPassword { get; private set; }

        public static async Task Main()
        {
            // Validate required configuration BEFORE we start standing up the
            // OPC UA stack. Missing credentials are an operational mistake, not
            // a runtime condition — fail fast with a clear message instead of
            // booting a server that will silently reject every client login.
            ValidateRequiredEnvironment();

            LogStartupVersion();

            // make sure our directories exist
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "settings"));
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "drivers"));
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "nodesets"));

            string issuerPath = Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "certs");
            if (!Directory.Exists(issuerPath))
            {
                Directory.CreateDirectory(issuerPath);
            }

            // surface provisioning-mode state on startup so operators are aware of how
            // untrusted client certificates will be handled until an issuer certificate
            // is present in the issuer store.
            if (!Directory.EnumerateFiles(issuerPath).Any())
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE")))
                {
                    Log.Logger.Warning("UA Edge Translator is starting with IGNORE_PROVISIONING_MODE set and no issuer certificates in '{IssuerPath}'. Asset-tag access is allowed, but provisioning auto-accept is suppressed: untrusted client certificates are REJECTED and must be trusted manually (via the Certificates dashboard or by copying them from pki/rejected/certs to pki/trusted/certs).", issuerPath);
                }
                else
                {
                    Log.Logger.Warning("UA Edge Translator is starting in PROVISIONING MODE: no issuer certificates were found in '{IssuerPath}'. Untrusted client certificates will be auto-accepted until a GDS pushes an issuer certificate.", issuerPath);
                }
            }

            // load protocol drivers
            DriverLoadContext.LoadProtocolDrivers();
            Log.Logger.Information("Loaded {DriversCount} protocol drivers.", Drivers.AllDrivers.Count());

            string appName = Environment.GetEnvironmentVariable("APP_NAME") ?? "UAEdgeTranslator";

            // Host the OPC UA server, the WoT Connectivity registry and the diagnostics
            // dashboard in one web application. The configuration file is applied as-is,
            // then the application name and certificate subject are overridden to match APP_NAME.
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            WebApplicationBuilder builder = DiagnosticsWebHost.CreateBuilder();
            DiagnosticsWebHost.ConfigureBuilder(builder, DiagnosticsWebHost.UrlFor());
            HostedOpcUaApplication opcUa = builder.Services.AddEdgeTranslatorServer(
                Telemetry,
                Path.Combine(Directory.GetCurrentDirectory(), "Ua.Edge.Translator.Config.xml"),
                configuration =>
                {
                    ApplyApplicationName(configuration, appName);
                    ValidateUserTokenPolicies(configuration);
                });
            opcUa.Created += app => App = app;
            builder.Services.AddWotRegistry(opcUa, Path.Combine(Directory.GetCurrentDirectory(), "settings", "wot-registry"));

            WebApplication app = builder.Build();
            DiagnosticsWebHost.MapEndpoints(app);

            IHostApplicationLifetime lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            opcUa.StartupTasks.Add(_ =>
            {
                s_serverStarted.TrySetResult();
                Log.Logger.Information("UA Edge Translator is running. Diagnostics UI is available on http://localhost:8081.");
                return Task.CompletedTask;
            });
            lifetime.ApplicationStopping.Register(() => Log.Logger.Information("Shutdown signal received, stopping UA Edge Translator..."));

            try
            {
                // Runs until SIGINT / SIGTERM; the host then stops the registry and the OPC UA server.
                await app.RunAsync().ConfigureAwait(false);
            }
            finally
            {
                await app.DisposeAsync().ConfigureAwait(false);
                Telemetry?.Dispose();
            }
        }

        /// <summary>
        /// Checks that the endpoints offer username/password login, the only identity
        /// UA Edge Translator accepts (OPCUA_USERNAME / OPCUA_PASSWORD). Clients cannot
        /// log in otherwise, so this explains the problem and the fix instead of letting
        /// every session fail later.
        /// </summary>
        internal static void ValidateUserTokenPolicies(ApplicationConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var policies = configuration.ServerConfiguration?.UserTokenPolicies.ToArray() ?? Array.Empty<UserTokenPolicy>();
            if (!policies.Any(p => p.TokenType == UserTokenType.UserName))
            {
                Log.Logger.Error(
                    "OPC UA clients will not be able to log in: no endpoint in 'Ua.Edge.Translator.Config.xml' offers username/password login, " +
                    "which is the only login UA Edge Translator accepts (OPCUA_USERNAME / OPCUA_PASSWORD). " +
                    "Fix: add <ua:UserTokenPolicy><ua:TokenType>UserName_1</ua:TokenType></ua:UserTokenPolicy> under ServerConfiguration/UserTokenPolicies.");
            }

            foreach (UserTokenPolicy policy in policies.Where(p => p.TokenType != UserTokenType.UserName))
            {
                Log.Logger.Warning(
                    "'Ua.Edge.Translator.Config.xml' offers {TokenType} login, but UA Edge Translator rejects it: only username/password " +
                    "(OPCUA_USERNAME / OPCUA_PASSWORD) is accepted, so clients choosing {TokenType} fail with BadIdentityTokenInvalid. " +
                    "Fix: remove the {TokenType} UserTokenPolicy from ServerConfiguration/UserTokenPolicies.",
                    policy.TokenType, policy.TokenType, policy.TokenType);
            }
        }

        /// <summary>
        /// Applies the APP_NAME override to a loaded configuration: the application name,
        /// URI and certificate subjects all follow it.
        /// </summary>
        internal static void ApplyApplicationName(ApplicationConfiguration configuration, string appName)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            configuration.ApplicationName = appName;
            configuration.ApplicationUri = "urn:" + appName;

            string subjectName = $"CN={appName}, C=US, S=Arizona, O=OPC Foundation";
            try
            {
                configuration.SecurityConfiguration.ApplicationCertificate.SubjectName = subjectName;
            }
            catch (ArgumentException ex)
            {
                Log.Logger.Error(ex, "Certificate subject name error. Please delete the 'pki' folder to allow the generation of new certs.");
                throw;
            }

            foreach (var cert in configuration.SecurityConfiguration.ApplicationCertificates)
            {
                cert.SubjectName = subjectName;
            }
        }

        private static void ValidateRequiredEnvironment()
        {
            string username = Environment.GetEnvironmentVariable("OPCUA_USERNAME");
            string password = Environment.GetEnvironmentVariable("OPCUA_PASSWORD");

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                const string message =
                    "OPCUA_USERNAME and OPCUA_PASSWORD environment variables MUST be set. " +
                    "UA Edge Translator refuses to start without configured credentials.";
                Log.Logger.Fatal(message);
                throw new InvalidOperationException(message);
            }

            OpcUaUsername = username;
            OpcUaPassword = password;
        }

        private static void LogStartupVersion()
        {
            // Log the build identity and the runtime/OS at startup so that
            // operators can correlate a deployed container against a specific
            // commit/release without having to exec into it.
            var asm = typeof(Program).Assembly;
            string informationalVersion =
                asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm.GetName().Version?.ToString()
                ?? "unknown";

            Log.Logger.Information(
                "UA Edge Translator starting. Version={Version}, Runtime={Runtime}, OS={OS}, Architecture={Arch}",
                informationalVersion,
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture);
        }

        // AcceptError hook for ICertificateValidatorEx (SDK 2.0.0). Returning true accepts
        // the error, false rejects it. This replaces the old CertificateValidation event,
        // where acceptance was signalled by setting e.Accept and rejection by leaving it
        // alone; the decision logic below is otherwise unchanged.
        internal static bool OPCUAClientCertificateAcceptError(Certificate certificate, ServiceResult error)
        {
            if (error.StatusCode != StatusCodes.BadCertificateUntrusted)
            {
                return false;
            }

            string issuerCertsDir = Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "certs");
            bool provisioningMode = !Directory.EnumerateFiles(issuerCertsDir).Any();

            // IGNORE_PROVISIONING_MODE is an explicit operator opt-out of provisioning
            // mode. As well as unblocking tag access (see UANodeManager.OnReadValue), it
            // means the operator is taking manual control of trust — so we must NOT
            // auto-accept untrusted clients even though the issuer store is still empty.
            // Treated as "set" when non-empty, matching the check used elsewhere.
            bool ignoreProvisioning = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE"));

            if (provisioningMode && !ignoreProvisioning)
            {
                // No issuer cert on disk yet — accept everything during initial setup
                // so a GDS can push the first issuer/trust list.
                Log.Logger.Warning("Auto-accepting certificate in provisioning mode: [{Subject}]", certificate?.Subject);
                return true;
            }

            // Reject the untrusted client. We reach here in two cases:
            //
            //  1. Provisioning is complete (issuer certs present): defer to the SDK's
            //     Part 12 validator, which already validates against the on-disk
            //     Trusted/Issuer stores and their CRLs (pushed and persisted by the GDS
            //     at pki/trusted/{certs,crl} and pki/issuer/{certs,crl}). Do NOT
            //     auto-accept untrusted client certs just because they chain to
            //     something in pki/issuer/certs — clients must be explicitly trusted.
            //
            //  2. The issuer store is empty but IGNORE_PROVISIONING_MODE is set: the
            //     operator opted out of provisioning auto-accept and trusts clients
            //     manually, so an empty issuer store no longer means "accept all".
            //
            // In both cases we return false and let the SDK reject, since
            // AutoAcceptUntrustedCertificates is false in the configuration.
            if (provisioningMode)
            {
                Log.Logger.Warning(
                    "Rejecting untrusted client certificate [{Subject}] (Issuer: [{Issuer}]): IGNORE_PROVISIONING_MODE is set, so provisioning auto-accept is suppressed. Trust the certificate via the Certificates dashboard or by copying it from pki/rejected/certs to pki/trusted/certs.",
                    certificate?.Subject, certificate?.Issuer);
                return false;
            }

            Log.Logger.Warning(
                "Rejecting untrusted client certificate [{Subject}] (Issuer: [{Issuer}]). Trust the certificate via the GDS or by copying it from pki/rejected/certs to pki/trusted/certs.",
                certificate?.Subject, certificate?.Issuer);

            return false;
        }
    }
}
