using Opc.Ua;
using Opc.Ua.Configuration;
using StatusCodes = Opc.Ua.StatusCodes;

namespace WotOpcUaMapper.UAClientLib
{
    /// <summary>
    /// Owns the single OPC UA <see cref="ApplicationInstance"/> and its configuration/certificate,
    /// initialized once and shared by all <see cref="UAClient"/> instances. Registered as a singleton.
    /// </summary>
    public class OpcUaApplication
    {
        private readonly IWebHostEnvironment _env;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _initialized;

        public ApplicationInstance? App { get; private set; }
        public ITelemetryContext Telemetry { get; } = DefaultTelemetry.Create(builder => builder.AddConsole());

        public OpcUaApplication(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task<ApplicationInstance> GetAppAsync()
        {
            if (_initialized && App != null)
            {
                return App;
            }

            await _initLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_initialized && App != null)
                {
                    return App;
                }

                // Provide a non-interactive approval dialog so the SDK can (re)generate the
                // application certificate unattended when the one on disk is missing or invalid,
                // rather than throwing "the certificate ... is invalid". Mirrors UA Cloud Publisher.
                ApplicationInstance.MessageDlg = new ApplicationMessageDlg();

                var app = new ApplicationInstance(Telemetry)
                {
                    ApplicationName = "UAWoTMapper",
                    ApplicationType = ApplicationType.ClientAndServer
                };

                var configPath = Path.Combine(_env.ContentRootPath, "Application.Config.xml");

                ApplicationConfiguration config = await app.LoadApplicationConfigurationAsync(configPath, false).ConfigureAwait(false);

                // With the MessageDlg above approving, the SDK creates or replaces the application
                // certificate as needed. A false result means it still could not produce a valid one.
                bool certOk = await app.CheckApplicationInstanceCertificatesAsync(false, 0).ConfigureAwait(false);
                if (!certOk)
                {
                    throw new InvalidOperationException("OPC UA application instance certificate is invalid and could not be created.");
                }

                config.CertificateValidator = new CertificateValidator(Telemetry);
                config.CertificateValidator.CertificateValidation += (validator, e) =>
                {
                    if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
                    {
                        e.Accept = true;
                    }
                };
                await config.CertificateValidator.UpdateAsync(config).ConfigureAwait(false);

                App = app;
                _initialized = true;
                return app;
            }
            finally
            {
                _initLock.Release();
            }
        }
    }
}
