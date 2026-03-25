
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Opc.Ua.Cloud;
    using Opc.Ua.Configuration;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using Serilog;
    using System;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;

    public class Program
    {
        public static ApplicationInstance App { get; private set; }

        public static ProtocolDriverRegistry Drivers { get; } = new();

        public static ConsoleTelemetry Telemetry { get; private set; } = new();

        public static async Task Main()
        {
            // make sure our directories exist
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "settings"));
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "drivers"));
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "nodesets"));

            // create OPC UA client app
            string appName = "UAEdgeTranslator";
            if (Environment.GetEnvironmentVariable("APP_NAME") != null)
            {
                appName = Environment.GetEnvironmentVariable("APP_NAME");
            }

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            App = new ApplicationInstance(Telemetry) {
                ApplicationName = appName,
                ApplicationType = ApplicationType.ClientAndServer,
                ConfigSectionName = "Ua.Edge.Translator"
            };

            await App.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);

            await App.CheckApplicationInstanceCertificatesAsync(false, 0).ConfigureAwait(false);

            // create OPC UA cert validator
            App.ApplicationConfiguration.CertificateValidator = new CertificateValidator(Telemetry);
            App.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(OPCUAClientCertificateValidationCallback);
            await App.ApplicationConfiguration.CertificateValidator.UpdateAsync(App.ApplicationConfiguration).ConfigureAwait(false);

            string issuerPath = Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "certs");
            if (!Directory.Exists(issuerPath))
            {
                Directory.CreateDirectory(issuerPath);
            }

            // load protocol drivers
            DriverLoadContext.LoadProtocolDrivers();
            Log.Logger.Information("Loaded {DriversCount} protocol drivers.", Drivers.AllDrivers.Count());

            // start the server
            await App.StartAsync(new UAServer()).ConfigureAwait(false);

            Log.Logger.Information("UA Edge Translator is running.");
            await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        }

        private static void OPCUAClientCertificateValidationCallback(CertificateValidator sender, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode != StatusCodes.BadCertificateUntrusted)
            {
                return;
            }

            string issuerCertsDir = Path.Combine(Directory.GetCurrentDirectory(), "pki", "issuer", "certs");
            bool provisioningMode = !Directory.EnumerateFiles(issuerCertsDir).Any();

            if (provisioningMode)
            {
                // No issuer cert on disk yet — accept everything during initial setup
                Log.Logger.Warning("Auto-accepting certificate in provisioning mode: [{Subject}]", e.Certificate?.Subject);
                e.Accept = true;
                return;
            }

            // Post-provisioning: accept only if the cert was signed by a CA
            // that the GDS explicitly pushed into our issuer store
            if (e.Certificate != null)
            {
                foreach (string certFile in Directory.EnumerateFiles(issuerCertsDir))
                {
                    try
                    {
                        using var issuerCert = X509CertificateLoader.LoadCertificateFromFile(certFile);
                        if (e.Certificate.Issuer == issuerCert.Subject)
                        {
                            Log.Logger.Information("Accepting certificate signed by provisioned CA: [{Subject}], Issuer: [{Issuer}]",
                                e.Certificate.Subject, issuerCert.Subject);
                            e.Accept = true;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error(ex, "Could not read issuer cert file: {File}", certFile);
                    }
                }
            }
        }
    }
}
