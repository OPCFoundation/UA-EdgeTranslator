namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua.Security.Certificates;
    using System;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using Xunit;

    /// <summary>
    /// Unit tests for <see cref="TlsServerCertificates"/>: the TLS endpoints
    /// (LoRaWAN, OCPP) must always receive a certificate that carries its
    /// private key, whether the SDK hands it over attached or detached.
    /// </summary>
    public sealed class TlsServerCertificatesTests
    {
        [Fact]
        public void An_attached_rsa_key_is_returned_as_is()
        {
            using RSA key = RSA.Create(2048);
            using X509Certificate2 source = CreateSelfSigned(new CertificateRequest("CN=attached-rsa", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using Certificate certificate = Certificate.From(source);

            using X509Certificate2 result = TlsServerCertificates.ToX509CertificateWithPrivateKey(certificate);

            Assert.True(result.HasPrivateKey);
            Assert.Equal(source.Thumbprint, result.Thumbprint);
        }

        [Fact]
        public void A_detached_rsa_key_is_reattached()
        {
            using RSA key = RSA.Create(2048);
            using X509Certificate2 withKey = CreateSelfSigned(new CertificateRequest("CN=detached-rsa", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(withKey.RawData);
            using Certificate publicCertificate = Certificate.From(publicOnly);
            using Certificate certificate = publicCertificate.CopyWithDetachedPrivateKey(key, false);

            Assert.True(certificate.HasDetachedPrivateKey);

            using X509Certificate2 result = TlsServerCertificates.ToX509CertificateWithPrivateKey(certificate);

            Assert.True(result.HasPrivateKey);
            Assert.NotNull(result.GetRSAPrivateKey());
            Assert.Equal(withKey.Thumbprint, result.Thumbprint);
        }

        [Fact]
        public void A_detached_ecdsa_key_is_reattached()
        {
            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using X509Certificate2 withKey = CreateSelfSigned(new CertificateRequest("CN=detached-ecdsa", key, HashAlgorithmName.SHA256));
            using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(withKey.RawData);
            using Certificate publicCertificate = Certificate.From(publicOnly);
            using Certificate certificate = publicCertificate.CopyWithDetachedPrivateKey(key, false);

            using X509Certificate2 result = TlsServerCertificates.ToX509CertificateWithPrivateKey(certificate);

            Assert.True(result.HasPrivateKey);
            Assert.NotNull(result.GetECDsaPrivateKey());
        }

        [Fact]
        public void A_certificate_without_any_private_key_is_refused()
        {
            using RSA key = RSA.Create(2048);
            using X509Certificate2 withKey = CreateSelfSigned(new CertificateRequest("CN=public-only", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(withKey.RawData);
            using Certificate certificate = Certificate.From(publicOnly);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => TlsServerCertificates.ToX509CertificateWithPrivateKey(certificate));

            Assert.Contains("CN=public-only", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Null_arguments_are_rejected()
        {
            Assert.Throws<ArgumentNullException>(() => TlsServerCertificates.ToX509CertificateWithPrivateKey(null));
            Assert.Throws<ArgumentNullException>(() => TlsServerCertificates.GetApplicationCertificate(null));
        }

        [Fact]
        public void A_configuration_without_a_certificate_manager_is_refused()
        {
            ApplicationConfiguration configuration = new();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => TlsServerCertificates.GetApplicationCertificate(configuration));

            Assert.Contains("certificate manager", ex.Message, StringComparison.Ordinal);
        }

        private static X509Certificate2 CreateSelfSigned(CertificateRequest request)
            => request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }
}
