
namespace Opc.Ua.Edge.Translator
{
    using Opc.Ua.Configuration;
    using Serilog;
    using System;
    using System.Threading.Tasks;

    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string message = string.Empty;
        private bool ask = false;

        public override void Message(string text, bool ask)
        {
            this.message = text;
            this.ask = ask;
        }

        public override async Task<bool> ShowAsync()
        {
            message += " -> yes!";
            Log.Logger.Information(message);

            // always return yes
            return await Task.FromResult(true).ConfigureAwait(false);
        }
    }
}
