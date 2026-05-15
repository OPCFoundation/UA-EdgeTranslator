
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Configuration;
    using Serilog;
    using System.Threading.Tasks;

    /// <summary>
    /// Headless implementation of <see cref="IApplicationMessageDlg"/>.
    ///
    /// The OPC UA SDK uses this dialog to prompt the operator for interactive
    /// decisions during application setup (e.g. "create a new application
    /// instance certificate?", "trust this peer certificate?"). On a server
    /// running unattended in a container we MUST refuse those prompts —
    /// auto-accepting them would silently bypass our certificate validation
    /// callback and our provisioning-mode logic, effectively trusting any
    /// peer that connects during setup.
    ///
    /// All accept/reject decisions are made instead by:
    ///   - <see cref="Program.OPCUAClientCertificateValidationCallback"/> (peer trust)
    ///   - <c>App.CheckApplicationInstanceCertificatesAsync(false, 0)</c> at startup
    ///     (own-certificate creation, with the silent flag set).
    /// </summary>
    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string _message = string.Empty;
        private bool _ask = false;

        public override void Message(string text, bool ask)
        {
            _message = text;
            _ask = ask;
        }

        public override Task<bool> ShowAsync()
        {
            if (_ask)
            {
                // The SDK is asking us to make an interactive decision. There
                // is no operator at the console, so the safe default is to
                // refuse and let the explicit code paths above handle it.
                Log.Logger.Warning("Suppressing interactive OPC UA prompt (answering NO): {Prompt}", _message);
                return Task.FromResult(false);
            }

            // Pure informational message — log it and return false (the return
            // value is ignored by the SDK in this case).
            Log.Logger.Information("OPC UA stack message: {Message}", _message);
            return Task.FromResult(false);
        }
    }
}
