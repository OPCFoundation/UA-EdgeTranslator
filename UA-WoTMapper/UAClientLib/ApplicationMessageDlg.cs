using Opc.Ua.Configuration;

namespace WotOpcUaMapper.UAClientLib
{
    /// <summary>
    /// Non-interactive approval dialog for the OPC UA application instance. The OPC UA SDK asks
    /// this dialog for approval when it needs to (re)create the application instance certificate,
    /// e.g. when an existing certificate on disk is invalid or no longer matches the configured
    /// ApplicationUri after a rebuild. Always answering "yes" lets the SDK regenerate a valid
    /// certificate on its own instead of throwing "the certificate ... is invalid".
    /// Mirrors the approach used by the UA Cloud Publisher.
    /// </summary>
    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string _message = string.Empty;

        public override void Message(string text, bool ask)
        {
            _message = text;
        }

        public override Task<bool> ShowAsync()
        {
            if (!string.IsNullOrEmpty(_message))
            {
                System.Console.WriteLine(_message);
            }

            // Always approve so the SDK can create/replace the application certificate unattended.
            return Task.FromResult(true);
        }
    }
}
