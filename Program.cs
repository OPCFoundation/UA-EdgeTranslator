
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua;
    using Opc.Ua.Configuration;
    using Serilog;
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public class Program
    {
        public static ApplicationInstance App { get; private set; }

        public static async Task Main()
        {
            // setup logging
            string pathToLogFile = Directory.GetCurrentDirectory();
            if (Environment.GetEnvironmentVariable("LOG_FILE_PATH") != null)
            {
                pathToLogFile = Environment.GetEnvironmentVariable("LOG_FILE_PATH");
            }
            InitLogging(pathToLogFile);

            // create OPC UA client app
            string appName = "UAEdgeTranslator";
            if (Environment.GetEnvironmentVariable("APP_NAME") != null)
            {
                appName = Environment.GetEnvironmentVariable("APP_NAME");
            }

            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            App = new ApplicationInstance
            {
                ApplicationName = appName,
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "UA.Edge.Translator"
            };

            await App.LoadApplicationConfiguration(false).ConfigureAwait(false);
            await App.CheckApplicationInstanceCertificate(false, 0).ConfigureAwait(false);

            // create OPC UA cert validator
            App.ApplicationConfiguration.CertificateValidator = new CertificateValidator();
            App.ApplicationConfiguration.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(OPCUAClientCertificateValidationCallback);

            // start the server
            await App.Start(new UAServer()).ConfigureAwait(false);

            Log.Logger.Information("UA Edge Translator is running.");
            await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        }

        private static void OPCUAClientCertificateValidationCallback(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            // always trust the OPC UA client certificate
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = true;
            }
        }

        private static void InitLogging(string pathToLogFile)
        {
            LoggerConfiguration loggerConfiguration = new LoggerConfiguration();

#if DEBUG
            loggerConfiguration.MinimumLevel.Debug();
#else
            loggerConfiguration.MinimumLevel.Information();
#endif
            if (!Directory.Exists(pathToLogFile))
            {
                Directory.CreateDirectory(pathToLogFile);
            }

            // set logging sinks
            loggerConfiguration.WriteTo.Console();
            loggerConfiguration.WriteTo.File(Path.Combine(pathToLogFile, "uaedgetranslator.logfile.txt"), fileSizeLimitBytes: 1024 * 1024, rollOnFileSizeLimit: true, retainedFileCountLimit: 10);

            Log.Logger = loggerConfiguration.CreateLogger();
            Log.Logger.Information($"Log file is: {Path.Combine(pathToLogFile, "uaedgetranslator.logfile.txt")}");
        }
    }
}
