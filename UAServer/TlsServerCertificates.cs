namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Opc.Ua.Security.Certificates;
    using System;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Produces <see cref="X509Certificate2"/> instances suitable for use as a TLS server
    /// certificate (for example by the Kestrel hosts in the LoRaWAN and OCPP drivers).
    /// </summary>
    /// <remarks>
    /// SDK 2.0.0 no longer exposes the loaded certificate on
    /// <see cref="CertificateIdentifier"/>; the application certificate is owned by the
    /// <see cref="ICertificateManager"/> and materialised with its private key through
    /// <see cref="ICertificateProvider"/>. A TLS server certificate is useless without its
    /// private key, and SDK 2.0.0 can hold that key detached from the certificate, so these
    /// helpers re-attach a detached key and fail fast when no key is available. Otherwise a
    /// keyless certificate would be accepted at startup and every TLS handshake would then
    /// fail at runtime, far from the cause.
    /// </remarks>
    public static class TlsServerCertificates
    {
        /// <summary>
        /// Gets the OPC UA application certificate, including its private key.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The certificate manager is not initialised yet, or no certificate with a
        /// private key is available.
        /// </exception>
        public static X509Certificate2 GetApplicationCertificate(ApplicationConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            ICertificateManager manager = configuration.CertificateManager
                ?? throw new InvalidOperationException(
                    "The OPC UA certificate manager is not initialised. CheckApplicationInstanceCertificatesAsync must run before TLS is configured.");

            CertificateIdentifier identifier = configuration.SecurityConfiguration?.ApplicationCertificate
                ?? throw new InvalidOperationException("No OPC UA application certificate is configured.");

            Certificate certificate = AsyncBridge.RunSync(
                () => manager.CertificateProvider
                    .GetPrivateKeyCertificateAsync(identifier, null, configuration.ApplicationUri)
                    .AsTask());

            if (certificate == null)
            {
                throw new InvalidOperationException("The OPC UA application certificate with its private key could not be loaded.");
            }

            return ToX509CertificateWithPrivateKey(certificate);
        }

        /// <summary>
        /// Converts an SDK certificate to an <see cref="X509Certificate2"/> that carries its
        /// private key, re-attaching a detached key when necessary.
        /// </summary>
        /// <exception cref="InvalidOperationException">No private key is available.</exception>
        public static X509Certificate2 ToX509CertificateWithPrivateKey(Certificate certificate)
        {
            ArgumentNullException.ThrowIfNull(certificate);

            X509Certificate2 x509 = certificate.AsX509Certificate2();

            if (!x509.HasPrivateKey && certificate.HasDetachedPrivateKey)
            {
                // The keys are deliberately not disposed: the copied certificate remains
                // bound to them and is used for the lifetime of the TLS server.
                RSA rsa = certificate.GetRSAPrivateKey();
                if (rsa != null)
                {
                    x509 = x509.CopyWithPrivateKey(rsa);
                }
                else
                {
                    ECDsa ecdsa = certificate.GetECDsaPrivateKey();
                    if (ecdsa != null)
                    {
                        x509 = x509.CopyWithPrivateKey(ecdsa);
                    }
                }
            }

            if (!x509.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    $"Certificate [{x509.Subject}] has no private key and cannot be used as a TLS server certificate.");
            }

            return x509;
        }
    }
}
