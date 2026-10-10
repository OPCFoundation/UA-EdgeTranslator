namespace Opc.Ua.Edge.Translator.Tests
{
	using Opc.Ua;
	using Opc.Ua.Edge.Translator;
	using Opc.Ua.Security.Certificates;
	using Serilog;
	using System;
	using System.IO;
	using System.Reflection;
	using Xunit;

	/// <summary>
	/// Covers <c>Program.OPCUAClientCertificateAcceptError</c>, the certificate trust hook.
	/// <para>
	/// SDK 2.0.0 replaced the <c>CertificateValidator.CertificateValidation</c> event, where
	/// acceptance was signalled by setting <c>e.Accept</c>, with the
	/// <see cref="ICertificateValidatorEx.AcceptError"/> callback, which returns
	/// <c>true</c> to accept. These tests preserve every case of the previous event tests:
	/// "Accept = true" is now a <c>true</c> return and "Accept left false" is <c>false</c>.
	/// The callback is bound as the exact delegate type <c>AcceptError</c> expects, so a
	/// signature mismatch fails here rather than at server start-up.
	/// </para>
	/// </summary>
	[Collection(WorkingDirectoryCollection.Name)]
	public class ProgramCertificateCallbackTests
	{
		[Fact]
		public void Callback_rejects_non_untrusted_errors()
		{
			using TestWorkingDirectory tmp = new();
			EnsureLoggerInitialized();

			Assert.False(InvokeCallback(StatusCodes.BadCertificateInvalid));
		}

		[Fact]
		public void Callback_auto_accepts_untrusted_in_provisioning_mode()
		{
			using TestWorkingDirectory tmp = new();
			EnsureLoggerInitialized();

			// No issuer certs on disk -> provisioning mode -> accepted.
			string issuerPath = Path.Combine(tmp.Path, "pki", "issuer", "certs");
			Directory.CreateDirectory(issuerPath);

			// Explicitly clear the override so a leaked value from another test (the
			// process-wide env var is shared) cannot suppress the auto-accept path.
			string previous = Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE");
			try
			{
				Environment.SetEnvironmentVariable("IGNORE_PROVISIONING_MODE", null);

				Assert.True(InvokeCallback(StatusCodes.BadCertificateUntrusted));
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

			// Once any issuer certs exist the callback must not auto-accept.
			Assert.False(InvokeCallback(StatusCodes.BadCertificateUntrusted));
		}

		[Fact]
		public void Callback_does_not_auto_accept_in_provisioning_when_ignore_flag_set()
		{
			using TestWorkingDirectory tmp = new();
			EnsureLoggerInitialized();

			// Empty issuer store -> provisioning mode, but IGNORE_PROVISIONING_MODE is set,
			// so the operator has opted out of auto-accept and untrusted clients must be
			// rejected.
			string issuerPath = Path.Combine(tmp.Path, "pki", "issuer", "certs");
			Directory.CreateDirectory(issuerPath);

			string previous = Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE");
			try
			{
				Environment.SetEnvironmentVariable("IGNORE_PROVISIONING_MODE", "1");

				Assert.False(InvokeCallback(StatusCodes.BadCertificateUntrusted));
			}
			finally
			{
				Environment.SetEnvironmentVariable("IGNORE_PROVISIONING_MODE", previous);
			}
		}

		private static bool InvokeCallback(StatusCode error)
		{
			MethodInfo method = typeof(Program).GetMethod(
				"OPCUAClientCertificateAcceptError",
				BindingFlags.NonPublic | BindingFlags.Static);
			Assert.NotNull(method);

			// Bind as the delegate type ICertificateValidatorEx.AcceptError expects.
			Func<Certificate, ServiceResult, bool> acceptError =
				method.CreateDelegate<Func<Certificate, ServiceResult, bool>>();

			// The certificate is only used for log messages; null is handled.
			return acceptError(null, new ServiceResult(error));
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
