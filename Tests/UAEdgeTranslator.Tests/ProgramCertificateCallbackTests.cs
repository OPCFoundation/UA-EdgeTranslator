namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator;
    using Serilog;
    using System;
    using System.IO;
    using System.Reflection;
    using Xunit;

    /// <summary>
    /// Reflection-driven coverage for <see cref="Program.OPCUAClientCertificateValidationCallback"/>.
    /// Only the public side-effects (Accept = true in provisioning mode, untouched
    /// otherwise) are observed — we never instantiate a real X.509 chain.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public class ProgramCertificateCallbackTests
    {
        private static readonly Type _sut = typeof(Program);

        [Fact]
        public void Callback_returns_immediately_for_non_untrusted_errors()
        {
            // Constructing CertificateValidationEventArgs is fragile; use reflection
            // and pass a non-BadCertificateUntrusted status. The callback should
            // simply return without touching anything.
            using TestWorkingDirectory tmp = new();
            EnsureLoggerInitialized();

            CertificateValidator validator = new(new Opc.Ua.Cloud.ConsoleTelemetry());
            ServiceResultException sre = new(StatusCodes.BadCertificateInvalid);

            CertificateValidationEventArgs args = CreateArgs(sre);

            InvokeCallback(validator, args);

            // Accept defaults to false for non-untrusted statuses.
            Assert.False(GetAccept(args));
        }

        [Fact]
        public void Callback_auto_accepts_untrusted_in_provisioning_mode()
        {
            using TestWorkingDirectory tmp = new();
            EnsureLoggerInitialized();

            // No issuer certs on disk -> provisioning mode -> Accept=true.
            string issuerPath = Path.Combine(tmp.Path, "pki", "issuer", "certs");
            Directory.CreateDirectory(issuerPath);

            // Explicitly clear the override so a leaked value from another test (the
            // process-wide env var is shared) cannot suppress the auto-accept path.
            string previous = Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE");
            try
            {
                Environment.SetEnvironmentVariable("IGNORE_PROVISIONING_MODE", null);

                CertificateValidator validator = new(new Opc.Ua.Cloud.ConsoleTelemetry());
                ServiceResultException sre = new(StatusCodes.BadCertificateUntrusted);
                CertificateValidationEventArgs args = CreateArgs(sre);

                InvokeCallback(validator, args);

                Assert.True(GetAccept(args));
            }
            finally
            {
                Environment.SetEnvironmentVariable("IGNORE_PROVISIONING_MODE", previous);
            }
        }

        [Fact]
        public void Callback_does_not_auto_accept_when_issuer_certs_present()
        {
            using TestWorkingDirectory tmp = new();
            EnsureLoggerInitialized();

            string issuerPath = Path.Combine(tmp.Path, "pki", "issuer", "certs");
            Directory.CreateDirectory(issuerPath);
            File.WriteAllText(Path.Combine(issuerPath, "marker.cer"), "stub");

            CertificateValidator validator = new(new Opc.Ua.Cloud.ConsoleTelemetry());
            ServiceResultException sre = new(StatusCodes.BadCertificateUntrusted);
            CertificateValidationEventArgs args = CreateArgs(sre);

            InvokeCallback(validator, args);

            // Once any issuer certs exist the callback must not auto-accept.
            Assert.False(GetAccept(args));
        }

        [Fact]
        public void Callback_does_not_auto_accept_in_provisioning_when_ignore_flag_set()
        {
            using TestWorkingDirectory tmp = new();
            EnsureLoggerInitialized();

            // Empty issuer store -> provisioning mode, but IGNORE_PROVISIONING_MODE
            // is set, so the operator has opted out of auto-accept and untrusted
            // clients must be rejected (Accept stays false).
            string issuerPath = Path.Combine(tmp.Path, "pki", "issuer", "certs");
            Directory.CreateDirectory(issuerPath);

            string previous = Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE");
            try
            {
                Environment.SetEnvironmentVariable("IGNORE_PROVISIONING_MODE", "1");

                CertificateValidator validator = new(new Opc.Ua.Cloud.ConsoleTelemetry());
                ServiceResultException sre = new(StatusCodes.BadCertificateUntrusted);
                CertificateValidationEventArgs args = CreateArgs(sre);

                InvokeCallback(validator, args);

                Assert.False(GetAccept(args));
            }
            finally
            {
                Environment.SetEnvironmentVariable("IGNORE_PROVISIONING_MODE", previous);
            }
        }

        private static void InvokeCallback(CertificateValidator validator, CertificateValidationEventArgs args)
        {
            MethodInfo m = _sut.GetMethod(
                "OPCUAClientCertificateValidationCallback",
                BindingFlags.NonPublic | BindingFlags.Static);

            m.Invoke(null, new object[] { validator, args });
        }

        private static CertificateValidationEventArgs CreateArgs(ServiceResultException error)
        {
            // CertificateValidationEventArgs lives in the SDK and exposes
            // an internal/public-ish constructor. Use the most permissive
            // constructor available and bail out cleanly otherwise.
            ServiceResult sr = new ServiceResult(error);

            ConstructorInfo[] ctors = typeof(CertificateValidationEventArgs)
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (ConstructorInfo ctor in ctors)
            {
                ParameterInfo[] parameters = ctor.GetParameters();
                if (parameters.Length == 2
                    && typeof(ServiceResult).IsAssignableFrom(parameters[0].ParameterType))
                {
                    return (CertificateValidationEventArgs)ctor.Invoke(new object[] { sr, null });
                }
            }

            throw new InvalidOperationException("Could not find a usable CertificateValidationEventArgs constructor.");
        }

        private static bool GetAccept(CertificateValidationEventArgs args)
        {
            PropertyInfo prop = typeof(CertificateValidationEventArgs)
                .GetProperty("Accept", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return (bool)prop.GetValue(args);
        }

        private static void EnsureLoggerInitialized()
        {
            // The callback writes through Serilog's static Log.Logger. Make sure
            // it has a sink so SinkException doesn't blow up on parallel tests.
            if (Log.Logger == null || Log.Logger.GetType().Name == "SilentLogger")
            {
                Log.Logger = new LoggerConfiguration().CreateLogger();
            }
        }
    }
}
