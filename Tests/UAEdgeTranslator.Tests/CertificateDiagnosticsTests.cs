namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Diagnostics;
    using System;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using Xunit;

    /// <summary>
    /// The pure helpers behind the dashboard's certificate, asset and address-space
    /// pages: certificate status and key size, status texts, scheme names and
    /// access-level formatting.
    /// </summary>
    public sealed class CertificateDiagnosticsTests
    {
        private static readonly MethodInfo s_describeCertificate = typeof(DiagnosticsService).GetMethod(
            "Describe", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(X509Certificate2), typeof(string) }, null);

        private static readonly MethodInfo s_describeResult = typeof(DiagnosticsService).GetMethod(
            "Describe", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(ServiceResult) }, null);

        private static readonly MethodInfo s_parseScheme = typeof(DiagnosticsService).GetMethod(
            "ParseScheme", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo s_formatAccessLevel = typeof(AddressSpaceService).GetMethod(
            "FormatAccessLevel", BindingFlags.NonPublic | BindingFlags.Static);

        private static X509Certificate2 RsaCertificate(string subject, DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            using RSA rsa = RSA.Create(2048);
            CertificateRequest request = new(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return request.CreateSelfSigned(notBefore, notAfter);
        }

        private static CertificateInfo Describe(X509Certificate2 certificate) =>
            (CertificateInfo)s_describeCertificate.Invoke(null, new object[] { certificate, "cert.der" });
        [Fact]
        public void A_valid_self_signed_rsa_certificate_is_described()
        {
            using X509Certificate2 certificate = RsaCertificate("CN=UA Edge Translator", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            CertificateInfo info = Describe(certificate);

            Assert.Equal("Valid", info.Status);
            Assert.Equal(2048, info.KeySize);
            Assert.True(info.SelfSigned);
            Assert.Equal("cert.der", info.FileName);
            Assert.Equal(certificate.Thumbprint, info.Thumbprint);
            Assert.InRange(info.DaysUntilExpiry, 360, 366);
        }

        [Fact]
        public void Certificates_close_to_or_past_their_end_date_are_flagged()
        {
            using X509Certificate2 expiring = RsaCertificate("CN=expiring", DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(10));
            using X509Certificate2 expired = RsaCertificate("CN=expired", DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));

            Assert.Equal("Expiring soon", Describe(expiring).Status);
            Assert.Equal("Expired", Describe(expired).Status);
            Assert.True(Describe(expired).DaysUntilExpiry < 0);
        }

        [Fact]
        public void The_key_size_of_an_ecdsa_certificate_is_reported()
        {
            using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            CertificateRequest request = new("CN=ecc", ecdsa, HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            Assert.Equal(256, Describe(certificate).KeySize);
        }
        [Fact]
        public void A_service_result_is_described_by_its_status_and_text()
        {
            string bare = (string)s_describeResult.Invoke(null, new object[] { new ServiceResult(StatusCodes.BadNotFound) });
            string withText = (string)s_describeResult.Invoke(null, new object[] { new ServiceResult(StatusCodes.BadNotFound, new LocalizedText("no such asset")) });

            Assert.Contains("BadNotFound", bare, StringComparison.Ordinal);
            Assert.DoesNotContain("(", bare, StringComparison.Ordinal);
            Assert.EndsWith("(no such asset)", withText, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("modbus+tcp://10.0.0.1:502", "modbus+tcp")]
        [InlineData("s7://192.168.0.10/0,1/", "s7")]
        [InlineData("no-scheme", "unknown")]
        [InlineData("", "unknown")]
        [InlineData(null, "unknown")]
        public void The_protocol_of_a_thing_base_is_its_scheme(string uri, string scheme)
        {
            Assert.Equal(scheme, s_parseScheme.Invoke(null, new object[] { uri }));
        }

        [Theory]
        [InlineData(0, "None")]
        [InlineData(1, "Read")]
        [InlineData(3, "Read, Write")]
        [InlineData(15, "Read, Write, HistoryRead, HistoryWrite")]
        public void Access_levels_are_listed_by_name(byte level, string expected)
        {
            string formatted = (string)s_formatAccessLevel.Invoke(null, new object[] { new DataValue(new Variant(level)) });

            Assert.Equal(expected, formatted);
        }
    }
}