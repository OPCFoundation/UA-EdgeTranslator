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
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            App = new ApplicationInstance(Telemetry) {
                ApplicationType = ApplicationType.ClientAndServer,
                ConfigSectionName = "Ua.Edge.Translator"
            };

            await App.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);

            string appName = Environment.GetEnvironmentVariable("APP_NAME") ?? "UAEdgeTranslator";

            // override ApplicationUri so it matches the configurable application name
            App.ApplicationConfiguration.ApplicationName = appName;
            App.ApplicationConfiguration.ApplicationUri = "urn:" + appName;

            try
            {
                App.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName = $"CN={appName}, C=US, S=Arizona, O=OPC Foundation";
            }
            catch (ArgumentException ex)
            {
                Log.Logger.Error(ex, "Certificate subject name error. Please delete the 'pki' folder to allow the generation of new certs.");
                throw;
            }

            foreach (var cert in App.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificates)
            {
                cert.SubjectName = $"CN={appName}, C=US, S=Arizona, O=OPC Foundation";
            }

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

            // surface provisioning-mode state on startup so operators are aware that
            // the server will auto-accept untrusted client certificates until an
            // issuer certificate has been pushed by a GDS.
            if (!Directory.EnumerateFiles(issuerPath).Any())
            {
                Log.Logger.Warning("UA Edge Translator is starting in PROVISIONING MODE: no issuer certificates were found in '{IssuerPath}'. Untrusted client certificates will be auto-accepted until a GDS pushes an issuer certificate.", issuerPath);
            }

            // load protocol drivers
            DriverLoadContext.LoadProtocolDrivers();
            Log.Logger.Information("Loaded {DriversCount} protocol drivers.", Drivers.AllDrivers.Count());

            // start the server
            await App.StartAsync(new UAServer()).ConfigureAwait(false);

            Log.Logger.Information("UA Edge Translator is running. Press Ctrl+C to shut down.");

            // graceful shutdown on SIGINT / SIGTERM so resources (drivers, file managers,
            // telemetry, logs) get a chance to flush before the process exits.
            using var shutdownCts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                Log.Logger.Information("Shutdown signal received, stopping UA Edge Translator...");
                shutdownCts.Cancel();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                if (!shutdownCts.IsCancellationRequested)
                {
                    Log.Logger.Information("Process exit signal received, stopping UA Edge Translator...");
                    shutdownCts.Cancel();
                }
            };

            try
            {
                await Task.Delay(Timeout.Infinite, shutdownCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on graceful shutdown
            }
            finally
            {
                try
                {
                    if (App != null)
                    {
                        await App.StopAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "Error while stopping the OPC UA application.");
                }

                Telemetry?.Dispose();
            }
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
                // so a GDS can push the first issuer/trust list.
                Log.Logger.Warning("Auto-accepting certificate in provisioning mode: [{Subject}]", e.Certificate?.Subject);
                e.Accept = true;
                return;
            }

            // Once provisioning is complete, defer to the SDK's Part 12 validator,
            // which already validates against the on-disk Trusted/Issuer stores
            // and their CRLs (pushed and persisted by the GDS at
            // pki/trusted/{certs,crl} and pki/issuer/{certs,crl}).
            //
            // Do NOT auto-accept untrusted client certs here just because they
            // chain to something in pki/issuer/certs — clients must be explicitly
            // trusted (either listed in pki/trusted/certs, or issued by a CA in
            // the trusted store), and revocation must be honoured via the GDS
            // CRLs the SDK already consumes.
            Log.Logger.Warning(
                "Rejecting untrusted client certificate [{Subject}] (Issuer: [{Issuer}]). Trust the certificate via the GDS or by copying it from pki/rejected/certs to pki/trusted/certs.",
                e.Certificate?.Subject, e.Certificate?.Issuer);
        }
    }
}
