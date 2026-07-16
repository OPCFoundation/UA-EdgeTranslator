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

                var app = new ApplicationInstance(Telemetry)
                {
                    ApplicationName = "WotOpcUaMapper",
                    ApplicationType = ApplicationType.ClientAndServer
                };

                var configPath = Path.Combine(_env.ContentRootPath, "Application.Config.xml");

                ApplicationConfiguration config = await app.LoadApplicationConfigurationAsync(configPath, false).ConfigureAwait(false);

                await app.CheckApplicationInstanceCertificatesAsync(false, 0).ConfigureAwait(false);

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
