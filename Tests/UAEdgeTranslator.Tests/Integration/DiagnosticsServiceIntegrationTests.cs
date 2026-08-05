namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Coverage for <see cref="DiagnosticsService"/>, the read-only provider
    /// behind every dashboard page.
    /// <para>
    /// It is exercised against the real running server rather than mocks because
    /// it deliberately holds no state: every call re-reads the live
    /// <see cref="ApplicationConfiguration"/>, the on-disk settings and pki
    /// folders and the active <see cref="UANodeManager"/>. The fixture provides
    /// all of those in an isolated temp working directory.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class DiagnosticsServiceIntegrationTests : IAsyncLifetime
    {
        private OpcUaServerFixture _fixture;
        private DiagnosticsService _service;

        public Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();
            _service = new DiagnosticsService();

            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            if (_fixture != null)
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
                _fixture = null;
            }
        }

        // ----- Overview ------------------------------------------------------

        [Fact]
        public void Server_overview_reports_the_live_application_identity()
        {
            ServerOverview overview = _service.GetServerOverview();

            Assert.Equal("UAEdgeTranslatorTest", overview.ApplicationName);
            Assert.Equal("urn:UAEdgeTranslatorTest", overview.ApplicationUri);
            Assert.False(string.IsNullOrEmpty(overview.Version));
            Assert.False(string.IsNullOrEmpty(overview.Runtime));
            Assert.False(string.IsNullOrEmpty(overview.HostName));
        }

        [Fact]
        public void Server_overview_reports_endpoints_and_counts()
        {
            ServerOverview overview = _service.GetServerOverview();

            Assert.NotEmpty(overview.Endpoints);
            Assert.Contains(overview.Endpoints, e => e.StartsWith("opc.tcp://", System.StringComparison.Ordinal));

            // The fixture registers exactly one driver (MockProtocolDriver).
            Assert.Equal(1, overview.DriverCount);

            // Counters are consistent with each other.
            Assert.Equal(_service.GetConnectedDevices().Count, overview.DeviceCount);
            Assert.Equal(_service.GetWoTFiles().Count, overview.WoTFileCount);
            Assert.True(overview.ConnectedDeviceCount <= overview.DeviceCount);
        }

        [Fact]
        public void Server_overview_reports_process_metrics_and_counters()
        {
            ServerOverview overview = _service.GetServerOverview();

            Assert.True(overview.MemoryWorkingSetMB > 0, "Working set should be a positive number of megabytes.");
            Assert.NotNull(overview.Counters);

            // Fresh fixture: nothing has failed yet.
            Assert.Equal(0, overview.Counters.TagReadErrors);
            Assert.Equal(0, overview.Counters.AssetReconnectFailures);
        }

        [Fact]
        public void Provisioning_mode_flags_are_consistent()
        {
            ServerOverview overview = _service.GetServerOverview();

            // The fixture sets IGNORE_PROVISIONING_MODE=1, so tag access must be
            // reported as allowed even though no issuer certificate exists.
            Assert.True(overview.IgnoreProvisioningMode);
            Assert.False(overview.TagAccessBlocked);
            Assert.False(_service.IsTagAccessBlocked());

            // TagAccessBlocked is exactly "provisioning AND NOT ignore".
            Assert.Equal(overview.ProvisioningMode && !overview.IgnoreProvisioningMode, overview.TagAccessBlocked);
        }

        // ----- OPC UA settings ----------------------------------------------

        [Fact]
        public void Opc_ua_settings_reflect_the_running_configuration()
        {
            OpcUaSettingsInfo settings = _service.GetOpcUaSettings();

            Assert.NotEmpty(settings.Endpoints);
            Assert.NotEmpty(settings.SecurityPolicies);

            // Every policy row is fully populated.
            Assert.All(settings.SecurityPolicies, p =>
            {
                Assert.False(string.IsNullOrEmpty(p.Mode));
                Assert.False(string.IsNullOrEmpty(p.Policy));
            });

            // Policy URIs are shortened to their trailing token for display.
            Assert.DoesNotContain(settings.SecurityPolicies, p => p.Policy.Contains('#', System.StringComparison.Ordinal));
        }

        [Fact]
        public void Opc_ua_settings_expose_limits_and_security_rows()
        {
            OpcUaSettingsInfo settings = _service.GetOpcUaSettings();

            Assert.NotEmpty(settings.SessionLimits);
            Assert.NotEmpty(settings.TransportQuotas);
            Assert.NotEmpty(settings.SecuritySettings);

            // Name/value rows must never be half-populated.
            Assert.All(settings.SessionLimits, s => Assert.False(string.IsNullOrEmpty(s.Name)));
            Assert.All(settings.TransportQuotas, s => Assert.False(string.IsNullOrEmpty(s.Name)));
            Assert.All(settings.SecuritySettings, s => Assert.False(string.IsNullOrEmpty(s.Name)));
        }

        // ----- Drivers -------------------------------------------------------

        [Fact]
        public void Protocol_drivers_describe_the_registered_driver()
        {
            IReadOnlyList<ProtocolDriverInfo> drivers = _service.GetProtocolDrivers();

            ProtocolDriverInfo mock = Assert.Single(drivers);

            Assert.Equal("mock", mock.Scheme);
            Assert.False(string.IsNullOrEmpty(mock.WoTBindingUri));
            Assert.Contains("MockProtocolDriver", mock.TypeName, System.StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(mock.Assembly));
            Assert.False(string.IsNullOrEmpty(mock.Version));
        }

        // ----- WoT files -----------------------------------------------------

        [Fact]
        public void WoT_files_parse_a_valid_thing_description()
        {
            WriteThingDescription("coverage-valid.jsonld", new
            {
                name = "CoverageAsset",
                title = "Coverage Asset",
                description = "A Thing Description written by the test.",
                @base = "modbus+tcp://127.0.0.1:502",
                properties = new
                {
                    First = new { type = "number" },
                    Second = new { type = "number" }
                },
                actions = new { Reset = new { } }
            });

            WoTFileInfo file = Assert.Single(
                _service.GetWoTFiles().Where(f => f.FileName == "coverage-valid.jsonld"));

            Assert.Null(file.ParseError);
            Assert.Equal("CoverageAsset", file.Name);
            Assert.Equal("Coverage Asset", file.Title);
            Assert.Equal("modbus+tcp://127.0.0.1:502", file.Base);
            Assert.Equal(2, file.PropertyCount);
            Assert.Equal(1, file.ActionCount);
            Assert.True(file.SizeBytes > 0);

            // PrettyJson is re-indented; RawJson is verbatim.
            Assert.Contains('\n', file.PrettyJson);
            Assert.False(string.IsNullOrEmpty(file.RawJson));
        }

        [Fact]
        public void WoT_files_report_a_parse_error_instead_of_throwing()
        {
            File.WriteAllText(
                Path.Combine(_fixture.WorkingDirectory, "settings", "coverage-broken.jsonld"),
                "{ this is not valid json");

            WoTFileInfo file = Assert.Single(
                _service.GetWoTFiles().Where(f => f.FileName == "coverage-broken.jsonld"));

            // The bad file must be listed with its error, not swallowed.
            Assert.False(string.IsNullOrEmpty(file.ParseError));
        }

        [Fact]
        public void WoT_files_are_sorted_by_file_name()
        {
            WriteThingDescription("coverage-zzz.jsonld", new { name = "Zzz" });
            WriteThingDescription("coverage-aaa.jsonld", new { name = "Aaa" });

            string[] names = _service.GetWoTFiles().Select(f => f.FileName).ToArray();

            Assert.Equal(names.OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase).ToArray(), names);
        }

        // ----- Devices -------------------------------------------------------

        [Fact]
        public void Connected_devices_resolve_their_protocol_from_the_thing_description()
        {
            IReadOnlyList<DeviceStatus> devices = _service.GetConnectedDevices();

            // Sorted by name, and every row fully populated.
            Assert.Equal(
                devices.Select(d => d.Name).OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase).ToArray(),
                devices.Select(d => d.Name).ToArray());

            Assert.All(devices, d =>
            {
                Assert.False(string.IsNullOrEmpty(d.Name));

                // Protocol is either resolved from a TD base URI or the explicit
                // "unknown" sentinel — never blank.
                Assert.False(string.IsNullOrEmpty(d.Protocol));
                Assert.True(d.TagCount >= 0);
            });
        }

        // ----- Certificates --------------------------------------------------

        [Fact]
        public void Certificate_overview_reports_the_application_certificate_and_stores()
        {
            CertificateOverview overview = _service.GetCertificateOverview();

            CertificateInfo cert = Assert.Single(overview.ApplicationCertificates);

            Assert.Contains("UAEdgeTranslatorTest", cert.Subject, System.StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(cert.Thumbprint));
            Assert.False(string.IsNullOrEmpty(cert.SignatureAlgorithm));
            Assert.True(cert.KeySize > 0);
            Assert.True(cert.NotAfter > cert.NotBefore);

            // Store paths are reported so an operator can find them on disk.
            Assert.False(string.IsNullOrEmpty(overview.TrustedStorePath));
            Assert.False(string.IsNullOrEmpty(overview.IssuerStorePath));
            Assert.False(string.IsNullOrEmpty(overview.RejectedStorePath));
        }

        [Fact]
        public void Application_certificate_download_never_contains_a_private_key()
        {
            // This is a security guarantee, not just a formatting detail: the
            // download must be public-only even though the source store holds
            // the private key.
            CertificateOverview overview = _service.GetCertificateOverview();
            string thumbprint = overview.ApplicationCertificates[0].Thumbprint;

            CertificateFile file = _service.GetApplicationCertificateFile(thumbprint);

            Assert.NotNull(file);
            Assert.EndsWith(".der", file.FileName, System.StringComparison.Ordinal);
            Assert.NotEmpty(file.Content);

            using X509Certificate2 exported = X509CertificateLoader.LoadCertificate(file.Content);
            Assert.False(exported.HasPrivateKey);
            Assert.Contains("UAEdgeTranslatorTest", exported.Subject, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Application_certificate_download_tolerates_spaces_in_the_thumbprint()
        {
            CertificateOverview overview = _service.GetCertificateOverview();
            string thumbprint = overview.ApplicationCertificates[0].Thumbprint;

            // Thumbprints copied out of Windows certificate dialogs are space
            // separated; the lookup normalises them.
            string spaced = string.Join(' ', Enumerable.Range(0, thumbprint.Length / 2)
                .Select(i => thumbprint.Substring(i * 2, 2)));

            Assert.NotNull(_service.GetApplicationCertificateFile(spaced));
        }

        [Fact]
        public void Application_certificate_download_returns_null_for_unknown_input()
        {
            Assert.Null(_service.GetApplicationCertificateFile(null));
            Assert.Null(_service.GetApplicationCertificateFile(string.Empty));
            Assert.Null(_service.GetApplicationCertificateFile("   "));
            Assert.Null(_service.GetApplicationCertificateFile("00112233445566778899AABBCCDDEEFF00112233"));
        }

        [Fact]
        public void Trusting_an_unknown_certificate_fails_gracefully()
        {
            TrustCertificateResult result = _service.TrustRejectedCertificate("00112233445566778899AABBCCDDEEFF00112233");

            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.Message));
        }

        [Fact]
        public void Untrusting_an_unknown_certificate_fails_gracefully()
        {
            TrustCertificateResult result = _service.UntrustCertificate("00112233445566778899AABBCCDDEEFF00112233");

            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.Message));
        }

        [Fact]
        public void Trust_operations_reject_a_blank_thumbprint()
        {
            Assert.False(_service.TrustRejectedCertificate(null).Success);
            Assert.False(_service.TrustRejectedCertificate(string.Empty).Success);
            Assert.False(_service.UntrustCertificate(null).Success);
            Assert.False(_service.UntrustCertificate(string.Empty).Success);
        }

        [Fact]
        public void A_rejected_certificate_can_be_trusted_and_then_untrusted()
        {
            // Exercises the full round trip against the real pki folders: the
            // file must physically move rejected -> trusted, then be deleted.
            string rejectedCerts = Path.Combine(_fixture.WorkingDirectory, "pki", "rejected", "certs");
            string trustedCerts = Path.Combine(_fixture.WorkingDirectory, "pki", "trusted", "certs");
            Directory.CreateDirectory(rejectedCerts);

            string sourceFile = FindOwnCertificateFile();

            using X509Certificate2 source = X509CertificateLoader.LoadCertificateFromFile(sourceFile);
            string thumbprint = source.Thumbprint;

            // The server's own certificate is already in the trusted store at
            // startup, so count files rather than asserting on presence: this
            // test owns exactly the one file it adds.
            int trustedBefore = CountTrustedFilesFor(trustedCerts, thumbprint);

            string rejectedFile = Path.Combine(rejectedCerts, "coverage-rejected.der");
            File.WriteAllBytes(rejectedFile, source.Export(X509ContentType.Cert));

            Assert.Contains(_service.GetCertificateOverview().RejectedCertificates, c => c.Thumbprint == thumbprint);

            TrustCertificateResult trusted = _service.TrustRejectedCertificate(thumbprint);
            Assert.True(trusted.Success, trusted.Message);

            // Moved, not copied.
            Assert.False(File.Exists(rejectedFile));
            Assert.Equal(trustedBefore + 1, CountTrustedFilesFor(trustedCerts, thumbprint));

            CertificateOverview afterTrust = _service.GetCertificateOverview();
            Assert.Contains(afterTrust.TrustedCertificates, c => c.Thumbprint == thumbprint);
            Assert.DoesNotContain(afterTrust.RejectedCertificates, c => c.Thumbprint == thumbprint);

            TrustCertificateResult untrusted = _service.UntrustCertificate(thumbprint);
            Assert.True(untrusted.Success, untrusted.Message);

            // Untrust removes the trusted copies for that thumbprint.
            Assert.True(
                CountTrustedFilesFor(trustedCerts, thumbprint) < trustedBefore + 1,
                "UntrustCertificate should have removed at least one trusted file.");
        }

        // ----- helpers -------------------------------------------------------

        private static int CountTrustedFilesFor(string trustedCerts, string thumbprint)
        {
            if (!Directory.Exists(trustedCerts))
            {
                return 0;
            }

            int count = 0;

            foreach (string file in Directory.EnumerateFiles(trustedCerts))
            {
                try
                {
                    using X509Certificate2 certificate = X509CertificateLoader.LoadCertificateFromFile(file);

                    if (string.Equals(certificate.Thumbprint, thumbprint, System.StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
                catch (System.Exception)
                {
                    // Not a certificate file; ignore.
                }
            }

            return count;
        }

        private void WriteThingDescription(string fileName, object body)
        {
            File.WriteAllText(
                Path.Combine(_fixture.WorkingDirectory, "settings", fileName),
                JsonConvert.SerializeObject(body));
        }

        private string FindOwnCertificateFile()
        {
            string ownCerts = Path.Combine(_fixture.WorkingDirectory, "pki", "own", "certs");

            Assert.True(Directory.Exists(ownCerts), $"Expected the own/certs store at '{ownCerts}'.");

            string file = Directory.EnumerateFiles(ownCerts)
                .FirstOrDefault(f => f.EndsWith(".der", System.StringComparison.OrdinalIgnoreCase)
                                  || f.EndsWith(".crt", System.StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(file);

            return file;
        }
    }
}
