namespace Opc.Ua.Edge.Translator.Tests.Components
{
	using Bunit;
	using Microsoft.Extensions.DependencyInjection;
	using Opc.Ua.Edge.Translator.Components.Pages;
	using Opc.Ua.Edge.Translator.Diagnostics;
	using Opc.Ua.Edge.Translator.Tests.Integration;
	using System;
	using System.IO;
	using System.Linq;
	using System.Security.Cryptography;
	using System.Security.Cryptography.X509Certificates;
	using System.Threading.Tasks;
	using Xunit;

	/// <summary>
	/// Interaction tests for the Certificates page: trusting a rejected peer
	/// certificate and deleting a trusted one through the page buttons. Peer
	/// certificates are generated per test and dropped into the fixture's real
	/// pki folders, so the page drives the same file moves an operator would.
	/// </summary>
	[Collection(WorkingDirectoryCollection.Name)]
	public sealed class CertificatesPageTests : BunitContext, IAsyncLifetime
	{
		private OpcUaServerFixture _fixture;

		public Task InitializeAsync()
		{
			_fixture = new OpcUaServerFixture();

			Services.AddSingleton<DiagnosticsService>();
			Services.AddSingleton<AddressSpaceService>();

			return Task.CompletedTask;
		}

		async Task IAsyncLifetime.DisposeAsync()
		{
			if (_fixture != null)
			{
				await _fixture.DisposeAsync().ConfigureAwait(false);
				_fixture = null;
			}
		}

		[Fact]
		public void Trusting_a_rejected_certificate_moves_it_to_the_trusted_table()
		{
			string thumbprint = AddPeerCertificate("rejected", "CN=Rejected Peer, O=Tests");

			IRenderedComponent<Certificates> page = Render<Certificates>();
			Assert.Contains("Rejected Peer", page.Markup, StringComparison.Ordinal);

			page.FindAll("button.btn-trust").Single().Click();

			Assert.Contains("Certificate moved to the trusted store.", page.Find(".banner-success").TextContent, StringComparison.Ordinal);
			Assert.Contains("No rejected certificates", page.Markup, StringComparison.Ordinal);
			Assert.True(File.Exists(Path.Combine(StoreDirectory("trusted"), thumbprint + ".der")));
		}

		[Fact]
		public void Deleting_a_trusted_certificate_removes_it_from_the_store()
		{
			string thumbprint = AddPeerCertificate("trusted", "CN=Trusted Peer, O=Tests");

			IRenderedComponent<Certificates> page = Render<Certificates>();
			int deleteButtonsBefore = page.FindAll("button.btn-danger").Count;

			page.FindAll("tr")
				.Single(row => row.TextContent.Contains("Trusted Peer", StringComparison.Ordinal))
				.QuerySelector("button.btn-danger")
				.Click();

			Assert.Contains("Certificate deleted from the trusted store.", page.Find(".banner-success").TextContent, StringComparison.Ordinal);
			Assert.DoesNotContain("Trusted Peer", page.Markup, StringComparison.Ordinal);
			Assert.Equal(deleteButtonsBefore - 1, page.FindAll("button.btn-danger").Count);
			Assert.False(File.Exists(Path.Combine(StoreDirectory("trusted"), thumbprint + ".der")));
		}

		[Fact]
		public void A_failed_trust_shows_a_warning_banner()
		{
			AddPeerCertificate("rejected", "CN=Vanishing Peer, O=Tests");

			IRenderedComponent<Certificates> page = Render<Certificates>();

			// Remove the file behind the page's back: the button still targets
			// the stale row, and the service must report the miss gracefully.
			foreach (string file in Directory.EnumerateFiles(StoreDirectory("rejected")))
			{
				File.Delete(file);
			}

			page.FindAll("button.btn-trust").Single().Click();

			Assert.Contains(page.FindAll(".banner-warning"), b => b.TextContent.Contains("could not be found in the rejected store", StringComparison.Ordinal));
		}

		[Fact]
		public void Application_certificate_card_shows_details_and_download_link()
		{
			IRenderedComponent<Certificates> page = Render<Certificates>();

			CertificateInfo cert = Services.GetRequiredService<DiagnosticsService>().GetCertificateOverview().ApplicationCertificates[0];

			Assert.Contains("UAEdgeTranslatorTest", page.Find(".card-title").TextContent, StringComparison.Ordinal);
			Assert.Contains($"certificates/download/{cert.Thumbprint}", page.Find("a.btn-trust").GetAttribute("href"), StringComparison.Ordinal);
			Assert.Contains("days)", page.Markup, StringComparison.Ordinal);
		}

		private string StoreDirectory(string store)
		{
			string directory = Path.Combine(_fixture.WorkingDirectory, "pki", store, "certs");
			Directory.CreateDirectory(directory);
			return directory;
		}

		private string AddPeerCertificate(string store, string subject)
		{
			using RSA key = RSA.Create(2048);
			CertificateRequest request = new(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

			File.WriteAllBytes(Path.Combine(StoreDirectory(store), certificate.Thumbprint + ".der"), certificate.Export(X509ContentType.Cert));

			return certificate.Thumbprint;
		}
	}
}
